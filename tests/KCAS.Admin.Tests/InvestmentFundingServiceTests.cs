using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InvestmentFundingServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Codex_connection_preserves_reviews_and_cash_flows_and_is_reused_by_returns()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (source, destination) = await FixtureAsync(db);
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<InvestmentFundingService>();
            var connection = await service.RecordAsync(destination.ClientId, destination.Id, source.Id,
                "Synthetic dated transfer and receiving statement", "Supported personal-to-joint capital continuation", actor, "Codex");
            Assert.Equal("Codex", connection.PerformedBy);
            Assert.Equal(actor.Identity!.Name, connection.RecordedBy);
            var review = await db.ClientInvestmentReconciliationReviews.AsNoTracking().SingleAsync(x => x.ClientInvestmentAccountId == source.Id);
            Assert.Equal(ClientInvestmentReconciliationOutcomes.HistoricalSurrendered, review.Outcome);
            Assert.Null(review.RelatedClientInvestmentAccountId);
            Assert.Equal(5, await db.ClientInvestmentTransactions.CountAsync(x => x.ClientInvestmentAccountId == source.Id || x.ClientInvestmentAccountId == destination.Id));
            var report = await scope.ServiceProvider.GetRequiredService<InvestmentReturnService>().LoadAsync(destination.ClientId, destination.Id, true);
            Assert.True(report!.LongTerm.IsAvailable, string.Join("; ", report.LongTerm.Issues));
            Assert.Equal(4, report.LongTerm.CashFlows.Count(x => x.IsInternalTransfer));
            Assert.Equal(50m, report.LongTerm.Gain);
        }
        finally { await CleanupAsync(db, source, destination); }
    }

    [Fact]
    public async Task Hidden_family_source_is_not_exposed_or_linkable_by_non_admin()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (source, destination) = await FixtureAsync(db);
        source.Client.ExcludeFromComplianceLists = true; await db.SaveChangesAsync();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<InvestmentFundingService>();
            Assert.DoesNotContain(await service.CandidatesAsync(destination.ClientId, destination.Id, actor), x => x.Id == source.Id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordAsync(destination.ClientId, destination.Id, source.Id, "Evidence", "Continuity", actor));
            var admin = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
            await service.RecordAsync(destination.ClientId, destination.Id, source.Id, "Evidence", "Continuity", admin);
            var report = await scope.ServiceProvider.GetRequiredService<InvestmentReturnService>().LoadAsync(destination.ClientId, destination.Id, false);
            Assert.Single(report!.History);
            Assert.Empty(report.FundingHistory);
        }
        finally { await CleanupAsync(db, source, destination); }
    }

    [Fact]
    public async Task Unrelated_accounts_and_missing_evidence_are_rejected()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (source, destination) = await FixtureAsync(db);
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<InvestmentFundingService>();
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(destination.ClientId, destination.Id, source.Id, " ", "Continuity", actor));
            source.Client.KanaanId = "Unrelated-" + Guid.NewGuid().ToString("N")[..8]; await db.SaveChangesAsync();
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(destination.ClientId, destination.Id, source.Id, "Evidence", "Continuity", actor));
            Assert.False(await db.InvestmentFundingConnections.AnyAsync(x => x.SourceAccountId == source.Id));
        }
        finally { await CleanupAsync(db, source, destination); }
    }

    [Fact]
    public async Task Funding_package_uses_portable_identifiers_preserves_performer_and_warns_about_missing_live_history()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (source, destination) = await FixtureAsync(db);
        var root = destination.Client;
        var originalSourceId = source.Id;
        var originalDestinationId = destination.Id;
        var originalFamilyId = root.KanaanId;
        ClientReviewPackage package;
        try
        {
            await scope.ServiceProvider.GetRequiredService<InvestmentFundingService>().RecordAsync(destination.ClientId, destination.Id, source.Id,
                "Synthetic transfer proof", "Full capital continuation in two instalments", actor, "Codex");
            package = new() { Client = new() { KanaanId = root.KanaanId }, FundingConnections = await InvestmentFundingTransfer.ExportAsync(db, root, default) };
            InvestmentFundingTransfer.Validate(package);
            Assert.Single(package.FundingConnections);
            package.FundingConnections.Add(package.FundingConnections[0]);
            Assert.Throws<ValidationException>(() => InvestmentFundingTransfer.Validate(package));
            package.FundingConnections.RemoveAt(1);
        }
        finally { await CleanupAsync(db, source, destination); }

        var warnings = new List<string>();
        await InvestmentFundingTransfer.InspectAsync(db, package, warnings, null, default);
        Assert.Single(warnings);
        Assert.Contains("not blocked", warnings[0]);
        (source, destination) = await FixtureAsync(db, originalFamilyId);
        source.Client.LegacyClientId = package.FundingConnections[0].Source.LegacyClientId;
        destination.Client.LegacyClientId = package.FundingConnections[0].Destination.LegacyClientId;
        source.AccountNumber = package.FundingConnections[0].Source.AccountNumber;
        destination.AccountNumber = package.FundingConnections[0].Destination.AccountNumber;
        destination.Client.FundValuations.Single().InvestmentUniqueNumber = destination.AccountNumber;
        source.LegacyInvestmentAccountId = package.FundingConnections[0].Source.LegacyAccountId;
        destination.LegacyInvestmentAccountId = package.FundingConnections[0].Destination.LegacyAccountId;
        // Source/live have different primary keys but identical historical data.
        await db.SaveChangesAsync();
        try
        {
            Assert.NotEqual(originalSourceId, source.Id); Assert.NotEqual(originalDestinationId, destination.Id);
            warnings.Clear();
            await InvestmentFundingTransfer.InspectAsync(db, package, warnings, actor.Identity!.Name, default);
            await db.SaveChangesAsync();
            Assert.Empty(warnings);
            var imported = await db.InvestmentFundingConnections.AsNoTracking().SingleAsync(x => x.SourceAccountId == source.Id);
            Assert.Equal(destination.Id, imported.DestinationAccountId);
            Assert.Equal("Codex", imported.PerformedBy);
            await InvestmentFundingTransfer.InspectAsync(db, package, warnings, actor.Identity.Name, default);
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.InvestmentFundingConnections.CountAsync(x => x.SourceAccountId == source.Id));
            source.Transactions.First().InvestmentAmountZar += 1; await db.SaveChangesAsync();
            await InvestmentFundingTransfer.InspectAsync(db, package, warnings, null, default);
            Assert.Contains(warnings, x => x.Contains("cash flows differ"));
        }
        finally { await CleanupAsync(db, source, destination); }
    }

    private static async Task<(ClientInvestmentAccount Source, ClientInvestmentAccount Destination)> FixtureAsync(ApplicationDbContext db, string? family = null)
    {
        family ??= "F" + Guid.NewGuid().ToString("N")[..10];
        var start = new DateOnly(2023, 1, 1);
        var name = "Funding test " + Guid.NewGuid().ToString("N");
        var personal = new Client { DisplayName = name + " personal", SurnameOrEntityName = name, KanaanId = family, LegacyClientId = Random.Shared.Next(1_000_000_000, 2_000_000_000) };
        var joint = new Client { DisplayName = name + " joint", SurnameOrEntityName = name, KanaanId = family, LegacyClientId = Random.Shared.Next(1_000_000_000, 2_000_000_000) };
        var source = new ClientInvestmentAccount { Client = personal, AccountNumber = name + " source", Administrator = "Synthetic",
            LegacyInvestmentAccountId = Random.Shared.Next(1_000_000_000, 2_000_000_000), InvestmentDate = start, SurrenderDate = start.AddDays(365) };
        source.Transactions.Add(Movement(start, investment: 100));
        source.Transactions.Add(Movement(start.AddDays(350), withdrawal: 80));
        source.Transactions.Add(Movement(start.AddDays(365), withdrawal: 30));
        var destination = new ClientInvestmentAccount { Client = joint, AccountNumber = name + " receiving", Administrator = "Synthetic",
            LegacyInvestmentAccountId = Random.Shared.Next(1_000_000_000, 2_000_000_000), InvestmentDate = start.AddDays(375) };
        destination.Transactions.Add(Movement(start.AddDays(375), investment: 80));
        destination.Transactions.Add(Movement(start.AddDays(405), investment: 30));
        personal.InvestmentAccounts.Add(source); joint.InvestmentAccounts.Add(destination);
        joint.FundValuations.Add(new() { LegacyFundId = Random.Shared.Next(1_000_000_000, 2_000_000_000), InvestmentUniqueNumber = destination.AccountNumber, Administrator = "Synthetic", AmountZar = 150, ValuationDate = start.AddDays(730) });
        db.Clients.AddRange(personal, joint); await db.SaveChangesAsync();
        db.ClientInvestmentReconciliationReviews.Add(new() { ClientId = personal.Id, ClientInvestmentAccountId = source.Id,
            Outcome = ClientInvestmentReconciliationOutcomes.HistoricalSurrendered, AppliedSurrenderDate = source.SurrenderDate,
            EvidenceReference = "Synthetic full closure", Reason = "Historical closure", ReviewedBy = "Codex",
            SnapshotSha256 = InvestmentReconciliationService.CalculateSnapshot(source, []) });
        await db.SaveChangesAsync();
        return (source, destination);
    }
    private static ClientInvestmentTransaction Movement(DateOnly date, decimal? investment = null, decimal? withdrawal = null) => new() {
        TransactionDate = date, InvestmentAmountZar = investment, WithdrawalAmountZar = withdrawal, InvestmentFrequency = "Once Off", IsFinal = true };
    private static async Task CleanupAsync(ApplicationDbContext db, ClientInvestmentAccount source, ClientInvestmentAccount destination)
    {
        await db.InvestmentFundingConnections.Where(x => x.SourceAccountId == source.Id || x.DestinationAccountId == destination.Id).ExecuteDeleteAsync();
        await db.ClientFundValuations.Where(x => x.ClientId == source.ClientId || x.ClientId == destination.ClientId).ExecuteDeleteAsync();
        await db.Clients.Where(x => x.Id == source.ClientId || x.Id == destination.ClientId).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
    }
}
