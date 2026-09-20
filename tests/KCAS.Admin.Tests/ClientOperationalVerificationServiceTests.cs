using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientOperationalVerificationServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Lifecycle_classification_requires_reason_and_duplicate_target()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var client = NewClient("Lifecycle source");
        var canonical = NewClient("Lifecycle canonical");
        db.AddRange(client, canonical);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ClassifyLifecycleAsync(client.Id, new(ClientLifecycleStatuses.Current, null, ""), "reviewer@example.test"));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ClassifyLifecycleAsync(client.Id, new(ClientLifecycleStatuses.Duplicate, null, "Duplicate found."), "reviewer@example.test"));

        await service.ClassifyLifecycleAsync(
            client.Id,
            new(ClientLifecycleStatuses.Duplicate, canonical.Id, "Same identity confirmed from source folder."),
            "reviewer@example.test");

        var saved = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.Equal(ClientLifecycleStatuses.Duplicate, saved.LifecycleStatus);
        Assert.Equal(canonical.Id, saved.DuplicateOfClientId);
        Assert.False(saved.IsActive);
        Assert.Equal("reviewer@example.test", saved.LifecycleReviewedBy);

        var portfolioItem = (await service.LoadPortfolioAsync(ClientLifecycleStatuses.Duplicate))
            .Single(item => item.ClientId == client.Id);
        Assert.True(portfolioItem.IsReviewResolved);
        Assert.True(portfolioItem.HasCanonicalClient);
        Assert.False(portfolioItem.HasCompletedAssessment);
    }

    [Fact]
    public async Task Replacement_remains_pending_until_human_accepts_and_then_applies()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var client = NewClient("Original display");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var itemId = await service.AddVerificationItemAsync(
            client.Id,
            new(
                ClientVerificationFields.DisplayName,
                ClientVerificationChangeTypes.Replace,
                "Verified display",
                @"Client folder\identity.pdf",
                "Identity document uses the full verified name.",
                true),
            "codex-assisted@example.test");

        Assert.Equal("Original display", (await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id)).DisplayName);
        Assert.Equal(1, await service.CountBlockingPendingAsync(client.Id));

        await service.VerifyAsync(itemId, "andries@example.test", "Compared with the identity document.");

        var savedClient = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        var savedItem = await db.ClientVerificationItems.AsNoTracking().SingleAsync(item => item.Id == itemId);
        Assert.Equal("Verified display", savedClient.DisplayName);
        Assert.Equal(ClientVerificationStatuses.Verified, savedItem.Status);
        Assert.Equal("andries@example.test", savedItem.DecidedBy);
        Assert.NotNull(savedItem.AppliedAtUtc);
        Assert.Equal(0, await service.CountBlockingPendingAsync(client.Id));
    }

    [Fact]
    public async Task Stale_recommendation_cannot_overwrite_a_later_edit()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var client = NewClient("Original");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var itemId = await service.AddVerificationItemAsync(
            client.Id,
            new(
                ClientVerificationFields.DisplayName,
                ClientVerificationChangeTypes.Replace,
                "Proposal",
                "source.pdf",
                "Recommend a controlled change.",
                true),
            "codex-assisted@example.test");

        client.DisplayName = "Edited elsewhere";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAsync(itemId, "andries@example.test", "Attempt stale decision."));
        Assert.Equal(ClientVerificationStatuses.Pending,
            (await db.ClientVerificationItems.AsNoTracking().SingleAsync(item => item.Id == itemId)).Status);
    }

    [Fact]
    public async Task Portfolio_marks_clients_with_completed_assessments_as_transfer_candidates()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var completed = NewClient("Operational completed");
        completed.LifecycleStatus = ClientLifecycleStatuses.Current;
        var draftOnly = NewClient("Operational draft");
        draftOnly.LifecycleStatus = ClientLifecycleStatuses.Current;
        var methodology = new RiskMethodologyVersion
        {
            Name = $"Operational portfolio method {Guid.NewGuid():N}",
            Status = ComplianceStatuses.Draft
        };
        db.AddRange(completed, draftOnly, methodology);
        await db.SaveChangesAsync();
        db.ClientRiskAssessments.AddRange(
            new ClientRiskAssessment
            {
                ClientId = completed.Id,
                RiskMethodologyVersionId = methodology.Id,
                Status = ClientRiskAssessmentStatuses.Finalised
            },
            new ClientRiskAssessment
            {
                ClientId = draftOnly.Id,
                RiskMethodologyVersionId = methodology.Id,
                Status = ClientRiskAssessmentStatuses.Draft
            });
        await db.SaveChangesAsync();

        var portfolio = await service.LoadPortfolioAsync(ClientLifecycleStatuses.Current);

        Assert.True(portfolio.Single(item => item.ClientId == completed.Id).HasCompletedAssessment);
        Assert.False(portfolio.Single(item => item.ClientId == draftOnly.Id).HasCompletedAssessment);
    }

    [Fact]
    public async Task Portfolio_current_value_matches_clients_list_and_excludes_surrendered_accounts()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var search = scope.ServiceProvider.GetRequiredService<ClientSearchService>();
        var client = NewClient("Portfolio valuation test");
        client.LifecycleStatus = ClientLifecycleStatuses.Current;
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        db.ClientInvestmentAccounts.AddRange(
            new ClientInvestmentAccount { ClientId = client.Id, AccountNumber = "CURRENT-1" },
            new ClientInvestmentAccount
            {
                ClientId = client.Id,
                AccountNumber = "SURRENDERED-1",
                SurrenderDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1))
            });
        db.ClientFundValuations.AddRange(
            new ClientFundValuation
            {
                ClientId = client.Id,
                LegacyFundId = -1,
                InvestmentUniqueNumber = "CURRENT-1",
                AmountZar = 12_500m
            },
            new ClientFundValuation
            {
                ClientId = client.Id,
                LegacyFundId = -2,
                InvestmentUniqueNumber = "SURRENDERED-1",
                AmountZar = 99_000m
            });
        await db.SaveChangesAsync();

        var portfolioValue = (await service.LoadPortfolioAsync(ClientLifecycleStatuses.Current))
            .Single(item => item.ClientId == client.Id).TotalCurrentValueZar;
        var clientsValue = (await search.SearchAsync(new ClientSearchRequest(KanaanId: client.KanaanId)))
            .Single(item => item.Id == client.Id).TotalCurrentValueZar;

        Assert.Equal(12_500m, portfolioValue);
        Assert.Equal(clientsValue, portfolioValue);
    }

    private static Client NewClient(string name) => new()
    {
        KanaanId = $"VERIFY-{Guid.NewGuid():N}"[..22],
        FullName = name,
        SurnameOrEntityName = name,
        DisplayName = name
    };
}
