using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using System.Xml.Linq;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class PerformanceFeeClientTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Published_CPI_import_is_idempotent_rejects_conflicts_and_stale_drafts_cannot_be_approved()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var reader = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Reports);
        var client = new Client { DisplayName = "Synthetic CPI " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "CPI-" + Guid.NewGuid().ToString("N"), InvestmentDate = new(1990, 1, 1) };
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", Email = actor.Identity!.Name, PersonName = "Synthetic CPI KI", IsActive = true };
        db.ClientInvestmentAccounts.Add(account); db.GovernanceRoleAssignments.Add(assignment); await db.SaveChangesAsync();
        var id = 0;
        const string code = "199011-19901215";
        var csv = "Month,PublishedOn,AnnualPercent,Reference\n1990-11-01,1990-12-15,4,Synthetic historical CPI source";
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ImportBenchmarksAsync(csv, reader));
            await service.ImportBenchmarksAsync(csv, actor); await service.ImportBenchmarksAsync(csv, actor);
            Assert.Single(await db.ComplianceReferenceValues.Where(x => x.Category == "PerformanceFeeCpi" && x.Code == code).ToListAsync());
            await Assert.ThrowsAsync<ValidationException>(() => service.ImportBenchmarksAsync(csv.Replace(",4,", ",5,"), actor));
            id = await service.RegisterAsync(account.Id, actor);
            var input = PerformanceFeeCalculatorTests.Input(); input.Inception = new(1990, 1, 1); input.From = new(1991, 1, 1); input.To = new(1991, 1, 2);
            input.MandateEffectiveFrom = input.Inception; input.UsePublishedCpi = true; input.Benchmarks.Clear();
            var p = await service.LoadAsync(id, actor);
            var claim = await service.PrepareAsync(id, p.Version, input, "Synthetic sourced CPI calculation", actor);
            p = await service.LoadAsync(id, actor);
            Assert.True(FeeJson.Read<FeeCalculationResult>(p.Claims.Single().ResultJson).IsReady);
            await service.ImportBenchmarksAsync("Month,PublishedOn,AnnualPercent,Reference\n1990-11-01,1991-01-01,5,Synthetic actual later correction", actor);
            p = await service.LoadAsync(id, actor);
            await Assert.ThrowsAsync<ValidationException>(() => service.ApproveAsync(id, claim, p.Version, "Must not approve stale CPI", actor));
            Assert.Null((await service.LoadAsync(id, actor)).Claims.Single().ApprovedAtUtc);
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteDeleteAsync();
            await db.ClientInvestmentAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync(); await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
            await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync();
            await db.ComplianceReferenceValues.Where(x => x.Category == "PerformanceFeeCpi" && (x.Code == code || x.Code == "199011-19910101")).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Automatic_review_includes_all_wrap_accounts_and_zero_rate_approval_never_resets_a_HWM()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var exports = scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic zero-fee KI", Email = actor.Identity!.Name, IsActive = true };
        var client = new Client { DisplayName = "Synthetic 0% client " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "ZERO-" + Guid.NewGuid().ToString("N"), InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var profile = await service.LoadAsync(id, actor);
            await service.PrepareAsync(id, profile.Version, PerformanceFeeZeroRateTests.ZeroInput(), "Synthetic signed zero-rate setup", actor);
            var workspace = await service.ClientWorkspaceAsync(id, actor);
            var source = new FeeStatementUpload("stable-original.xlsx", PerformanceFeeStatementTests.Workbook(account: account.AccountNumber!, holding: "Stable (KP1)"));
            await service.ImportClientStatementsAsync(id, workspace.Version, [source], actor);
            workspace = await service.ClientWorkspaceAsync(id, actor);
            Assert.Null(workspace.Accounts.Single().Exclusion);
            var claims = await service.PrepareAllClientStatementsAsync(id, workspace.Version, [], "", actor);
            workspace = await service.ClientWorkspaceAsync(id, actor);
            await service.ApproveClientClaimsAsync(id, workspace.Version, claims.ToArray(), "Signed 0% mandate verified", actor);
            profile = await service.LoadAsync(id, actor);
            Assert.Null(profile.CheckpointJson); Assert.Equal(FeeClaimStages.NoFee, profile.Claims.Single().Status);
            Assert.Equal(new DateOnly(2023, 1, 1), profile.NextReviewDate);
            workspace = await service.ClientWorkspaceAsync(id, actor);
            Assert.NotNull(workspace.Accounts.Single().PreparationHold);
            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.PrepareAllClientStatementsAsync(id, workspace.Version, [], "", actor));
            var report = await exports.ClientReportAsync(id, claims.ToArray(), actor);
            using (var zip = new ZipArchive(new MemoryStream(report)))
            {
                using var summary = zip.GetEntry("Client fee review.pdf")!.Open(); using var buffer = new MemoryStream(); await summary.CopyToAsync(buffer);
                Assert.Contains("Verified 0%", Encoding.Latin1.GetString(buffer.ToArray()));
                Assert.DoesNotContain("DRAFT - NOT FOR SUBMISSION", Encoding.Latin1.GetString(buffer.ToArray()));
            }
            var qa = Environment.GetEnvironmentVariable("KCAS_FEE_CLIENT_QA_PATH");
            if (!string.IsNullOrWhiteSpace(qa)) await File.WriteAllBytesAsync(Path.Combine(qa, "approved-zero-report.zip"), report);
            var retained = FeeJson.Write(new FeeCheckpoint(PerformanceFeeCalculatorTests.Start, 100000, 130000, 140000, "Synthetic retained earlier protection"));
            await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.CheckpointJson, retained));
            profile = await service.LoadAsync(id, actor);
            var laterZero = PerformanceFeeZeroRateTests.ZeroInput(); laterZero.From = new(2022, 1, 1); laterZero.To = new(2023, 1, 1);
            laterZero.StatementHistory = (await service.StatementsAsync(id, actor)).Ledger.History;
            laterZero.ClosingReference = "Synthetic actual 2023 closing statement";
            var later = await service.PrepareAsync(id, profile.Version, laterZero, "Second supported zero-rate review", actor);
            profile = await service.LoadAsync(id, actor);
            await service.ApproveAsync(id, later, profile.Version, "Zero rate confirmed; prior protection retained", actor);
            Assert.Equal(retained, (await service.LoadAsync(id, actor)).CheckpointJson);
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteDeleteAsync();
            await db.ClientInvestmentAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync(); await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
            await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Two_files_feed_one_client_review_with_exact_account_matches_idempotency_and_atomic_KI_approval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var reader = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Reports);
        var key = Guid.NewGuid().ToString("N");
        var client = new Client { DisplayName = "Synthetic combined PDF " + key, SurnameOrEntityName = "Synthetic" };
        var accounts = new[] { new ClientInvestmentAccount { Client = client, AccountNumber = "A-" + key, InvestmentDate = PerformanceFeeCalculatorTests.Start },
            new ClientInvestmentAccount { Client = client, AccountNumber = "B-" + key, InvestmentDate = PerformanceFeeCalculatorTests.Start } };
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic client PDF KI", Email = actor.Identity!.Name, IsActive = true };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.AddRange(accounts); await db.SaveChangesAsync();
        var profileIds = new List<int>();
        try
        {
            foreach (var account in accounts)
            {
                var id = await service.RegisterAsync(account.Id, actor); profileIds.Add(id);
                var p = await service.LoadAsync(id, actor);
                await service.PrepareAsync(id, p.Version, PerformanceFeeCalculatorTests.Input(), "Verified synthetic fee setup", actor);
            }
            var anchor = profileIds[0];
            var workspace = await service.ClientWorkspaceAsync(anchor, actor); Assert.Empty(workspace.Sources);
            var originals = new List<FeeStatementUpload> { new("period-one.xlsx", MultiAccount(false, accounts.Select(x => x.AccountNumber!).Append("NOT-THIS-CLIENT").ToArray())),
                new("overlap-two.xlsx", MultiAccount(true, accounts.Select(x => x.AccountNumber!).ToArray())) };
            var qaPath = Environment.GetEnvironmentVariable("KCAS_FEE_CLIENT_QA_PATH");
            if (!string.IsNullOrWhiteSpace(qaPath))
            {
                Directory.CreateDirectory(qaPath);
                await File.WriteAllBytesAsync(Path.Combine(qaPath, "qa-first.xlsx"), MultiAccount(false, "QA-PDF-IA", "QA-PDF-IB", "UNMATCHED"));
                await File.WriteAllBytesAsync(Path.Combine(qaPath, "qa-second.xlsx"), MultiAccount(true, "QA-PDF-IA", "QA-PDF-IB"));
            }
            var preview = await service.ClientWorkspaceAsync(anchor, actor, originals);
            Assert.Equal(3, preview.Accounts.Count); Assert.Equal(2, preview.Accounts.Count(x => x.Exclusion == null));
            Assert.Contains(preview.Accounts, x => x.Number == "NOT-THIS-CLIENT" && x.Account == null && x.Exclusion != null);
            Assert.False(await db.PerformanceFeeStatements.AnyAsync(x => profileIds.Contains(x.PerformanceFeeProfileId)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClientWorkspaceAsync(anchor, reader, originals));
            await Assert.ThrowsAsync<ValidationException>(() => service.ImportClientStatementsAsync(anchor, workspace.Version,
                [originals[0], new("wrong.xlsx", PerformanceFeeStatementTests.Workbook(account: "UNRELATED"))], actor));
            Assert.False(await db.PerformanceFeeStatements.AnyAsync(x => profileIds.Contains(x.PerformanceFeeProfileId)));
            await service.ImportClientStatementsAsync(anchor, workspace.Version, originals, actor);
            await Assert.ThrowsAsync<ValidationException>(() => service.ImportClientStatementsAsync(anchor, workspace.Version, originals, actor));
            workspace = await service.ClientWorkspaceAsync(anchor, actor);
            Assert.Equal(2, workspace.Sources.Count);
            await service.ImportClientStatementsAsync(anchor, workspace.Version, originals, actor);
            Assert.Equal(2, (await service.ClientWorkspaceAsync(anchor, actor)).Sources.Count);
            foreach (var item in workspace.Accounts.Where(x => x.Exclusion == null)) { Assert.Equal(3, item.Ledger.Rows.Count); Assert.Equal(3, item.Ledger.DuplicateRows); }
            foreach (var source in workspace.Sources)
                Assert.Equal(originals.Single(x => x.FileName == source.Receipt.FileName).Content, (await service.DownloadStatementAsync(source.ProfileId, source.Receipt.Id, actor)).Content);
            var claims = await service.PrepareClientStatementsAsync(anchor, workspace.Version, accounts.Select(x => x.Id).ToArray(), [], "", actor);
            workspace = await service.ClientWorkspaceAsync(anchor, actor);
            Assert.Equal(2, claims.Count);
            foreach (var item in workspace.Accounts.Where(x => x.Exclusion == null))
            {
                var claim = item.Profile!.Claims.Single(x => claims.Contains(x.Id));
                Assert.Equal("KCAS", claim.Performer);
                Assert.True(FeeJson.Read<FeeCalculationResult>(claim.ResultJson).IsReady, claim.ResultJson);
                Assert.Empty(FeeJson.Read<FeePreparation>(claim.InputJson).AccountDependencies);
            }
            var savedWorkspaceVersion = workspace.Version;
            var savedDrafts = workspace.Accounts.Where(x => x.Profile != null).ToDictionary(x => x.Profile!.Id,
                x => (x.Profile!.Version, x.Profile.UpdatedAtUtc, Claim: x.Profile.Claims.Single().Version,
                    x.Profile.Claims.Single().PreparedAtUtc, x.Profile.Claims.Single().ResultJson));
            var eventCount = await db.PerformanceFeeEvents.CountAsync(x => profileIds.Contains(x.PerformanceFeeProfileId));
            var reused = await service.PrepareAllClientStatementsAsync(anchor, workspace.Version, [], "", actor);
            Assert.Equal(claims.Order(), reused.Order());
            workspace = await service.ClientWorkspaceAsync(anchor, actor);
            Assert.Equal(savedWorkspaceVersion, workspace.Version);
            foreach (var item in workspace.Accounts.Where(x => x.Profile != null))
                Assert.Equal(savedDrafts[item.Profile!.Id], (item.Profile.Version, item.Profile.UpdatedAtUtc,
                    item.Profile.Claims.Single().Version, item.Profile.Claims.Single().PreparedAtUtc, item.Profile.Claims.Single().ResultJson));
            Assert.Equal(eventCount, await db.PerformanceFeeEvents.CountAsync(x => profileIds.Contains(x.PerformanceFeeProfileId)));
            var bytes = await scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>().ClientPreviewAsync(anchor, claims.ToArray(), actor);
            using (var zip = new ZipArchive(new MemoryStream(bytes))) Assert.Equal(3, zip.Entries.Count);
            var exports = scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>();
            var concise = await exports.ClientSummaryAsync(anchor, claims.ToArray(), actor);
            Assert.Contains("DRAFT - NOT FOR SUBMISSION", Encoding.Latin1.GetString(concise));
            Assert.DoesNotContain("Internal preparation evidence", Encoding.Latin1.GetString(concise));
            await Assert.ThrowsAsync<ValidationException>(() => exports.ClientSummaryAsync(anchor, claims.ToArray(), actor, false));
            await Assert.ThrowsAsync<ValidationException>(() => exports.ClientSummaryAsync(anchor, [claims[0], claims[0]], actor));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exports.ClientSummaryAsync(anchor, [int.MaxValue], actor));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exports.ClientSummaryAsync(anchor, claims.ToArray(), reader, false));
            var (summaryHttp, _) = await new PerformanceFeeServiceTests(factory).AuthenticatedHttp(scope, actor);
            using var summaryClient = summaryHttp;
            var summaryUrl = $"/investments/performance-fees/client-summary.pdf?profileId={anchor}&ids={string.Join(',', claims)}";
            var summaryResponse = await summaryClient.GetAsync(summaryUrl);
            Assert.Equal(System.Net.HttpStatusCode.OK, summaryResponse.StatusCode);
            Assert.Equal("application/pdf", summaryResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await summaryClient.GetAsync(summaryUrl + "&draft=false")).StatusCode);
            var page = await summaryClient.GetStringAsync($"/investments/performance-fees/{anchor}/client-review");
            Assert.Contains("Approval summary", page); Assert.Contains("Approve ready calculations (2)", page);
            Assert.Contains("Summary PDF", page); Assert.Contains("Detailed review package", page);
            Assert.True(page.IndexOf("Approval summary", StringComparison.Ordinal) < page.IndexOf("Detailed calculations and preparation", StringComparison.Ordinal));
            var summaryQa = Environment.GetEnvironmentVariable("KCAS_FEE_SUMMARY_QA_PATH");
            if (!string.IsNullOrWhiteSpace(summaryQa))
            { Directory.CreateDirectory(summaryQa); await File.WriteAllTextAsync(Path.Combine(summaryQa, "client-review-draft.html"), page); }
            if (!string.IsNullOrWhiteSpace(qaPath)) await File.WriteAllBytesAsync(Path.Combine(qaPath, "combined-draft.zip"), bytes);
            // Model an evidenced connected pair, then ensure either side's edit blocks both approvals.
            var linked = new List<FeePreparation>();
            foreach (var id in profileIds) linked.Add(FeeJson.Read<FeePreparation>((await service.LoadAsync(id, actor)).PreparationJson));
            var hashes = linked.Select(x => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FeeJson.Write(x))))).ToArray();
            for (var i = 0; i < profileIds.Count; i++)
            {
                linked[i].AccountDependencies = [new(accounts[1-i].Id, hashes[1-i])];
                var p = await service.LoadAsync(profileIds[i], actor);
                await service.PrepareAsync(p.Id, p.Version, linked[i], "Synthetic verified connection contract", actor);
            }
            var changed = await service.LoadAsync(profileIds[0], actor); var changedInput = FeeJson.Read<FeePreparation>(changed.PreparationJson);
            changedInput.Wraps[0].Input.FeePercent = 14;
            await service.PrepareAsync(changed.Id, changed.Version, changedInput, "Synthetic terms update", actor);
            foreach (var id in profileIds)
            { var p = await service.LoadAsync(id, actor); await Assert.ThrowsAsync<ValidationException>(() => service.ApproveAsync(id, p.Claims.Single().Id, p.Version, "Stale linked inputs", actor)); }
            workspace = await service.ClientWorkspaceAsync(anchor, actor);
            claims = await service.PrepareClientStatementsAsync(anchor, workspace.Version, accounts.Select(x => x.Id).ToArray(), [], "", actor);
            workspace = await service.ClientWorkspaceAsync(anchor, actor);
            await service.ApproveClientClaimsAsync(anchor, workspace.Version, claims.ToArray(), "Synthetic verified KI batch", actor);
            foreach (var id in profileIds) Assert.Equal(FeeClaimStages.Approved, (await service.LoadAsync(id, actor)).Claims.Single().Status);
            concise = await exports.ClientSummaryAsync(anchor, claims.ToArray(), actor, false);
            Assert.Contains("Claim authority", Encoding.Latin1.GetString(concise));
            Assert.DoesNotContain("DRAFT - NOT FOR SUBMISSION", Encoding.Latin1.GetString(concise));
            Assert.Equal(System.Net.HttpStatusCode.OK, (await summaryClient.GetAsync(summaryUrl + "&draft=false")).StatusCode);
            page = await summaryClient.GetStringAsync($"/investments/performance-fees/{anchor}/client-review");
            Assert.Contains("Detailed submission package", page);
            Assert.DoesNotContain("Approve ready calculations (", page);
            if (!string.IsNullOrWhiteSpace(summaryQa)) await File.WriteAllTextAsync(Path.Combine(summaryQa, "client-review-approved.html"), page);
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareClientStatementsAsync(anchor, workspace.Version, accounts.Select(x => x.Id).ToArray(), [], "", actor));
            Assert.False(await db.ClientInvestmentTransactions.AnyAsync(x => accounts.Select(a => a.Id).Contains(x.ClientInvestmentAccountId)));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.PerformanceFeeProfiles.Where(x => profileIds.Contains(x.Id)).ExecuteDeleteAsync();
            var ids = accounts.Select(x => x.Id).ToArray(); await db.ClientInvestmentAccounts.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
            await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Unchanged_incomplete_drafts_are_reused_but_new_sources_terms_and_CPI_rebuild_them()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var client = new Client { DisplayName = "Synthetic cached fee " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "CACHE-" + Guid.NewGuid().ToString("N"), InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        const string cpiCode = "199311-19931215";
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var workspace = await service.ClientWorkspaceAsync(id, actor);
            var upload = new FeeStatementUpload("original.xlsx", PerformanceFeeStatementTests.Workbook(account: account.AccountNumber!));
            await service.ImportClientStatementsAsync(id, workspace.Version, [upload], actor);
            async Task<string> Prepare()
            {
                var current = await service.ClientWorkspaceAsync(id, actor);
                await service.PrepareAllClientStatementsAsync(id, current.Version, [], "", actor);
                return (await service.LoadAsync(id, actor)).Claims.Single().Version;
            }
            var first = await Prepare();
            Assert.False(FeeJson.Read<FeeCalculationResult>((await service.LoadAsync(id, actor)).Claims.Single().ResultJson).IsReady);
            Assert.Equal(first, await Prepare());
            workspace = await service.ClientWorkspaceAsync(id, actor);
            await service.ImportClientStatementsAsync(id, workspace.Version, [upload], actor);
            Assert.Equal(first, await Prepare());

            var profile = await service.LoadAsync(id, actor);
            var input = FeeJson.Read<FeePreparation>(profile.PreparationJson);
            input.Wraps[0].Input.FeePercent = 12;
            await service.PrepareAsync(id, profile.Version, input, "Synthetic newly evidenced terms", actor);
            var manualVersion = (await service.LoadAsync(id, actor)).Claims.Single().Version;
            var changedTerms = await Prepare();
            Assert.NotEqual(manualVersion, changedTerms);
            Assert.Equal(changedTerms, await Prepare());

            await service.ImportBenchmarksAsync("Month,PublishedOn,AnnualPercent,Reference\n1993-11-01,1993-12-15,4,Synthetic cache invalidation source", actor);
            var changedCpi = await Prepare();
            Assert.NotEqual(changedTerms, changedCpi);
            Assert.Equal(changedCpi, await Prepare());

            workspace = await service.ClientWorkspaceAsync(id, actor);
            await service.ImportClientStatementsAsync(id, workspace.Version,
                [new("second-period.xlsx", PerformanceFeeStatementTests.Workbook(account: account.AccountNumber!, shift: 3))], actor);
            var changedSources = await Prepare();
            Assert.NotEqual(changedCpi, changedSources);
            Assert.Equal(changedSources, await Prepare());
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteDeleteAsync();
            await db.ClientInvestmentAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync();
            await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
            await db.ComplianceReferenceValues.Where(x => x.Category == "PerformanceFeeCpi" && x.Code == cpiCode).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Preserved_originals_are_reparsed_without_read_only_writes_and_legacy_drafts_cannot_be_approved()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var client = new Client { DisplayName = "Synthetic parser upgrade " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "UPGRADE-" + Guid.NewGuid().ToString("N"), InvestmentDate = new(2000, 1, 1) };
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", Email = actor.Identity!.Name, PersonName = "Synthetic parser KI", IsActive = true };
        db.ClientInvestmentAccounts.Add(account); db.GovernanceRoleAssignments.Add(assignment); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var profile = await service.LoadAsync(id, actor);
            await service.PrepareAsync(id, profile.Version, PerformanceFeeCalculatorTests.Input(), "Synthetic pre-summary draft", actor);
            var workspace = await service.ClientWorkspaceAsync(id, actor);
            var original = PerformanceFeeHistoryTests.SummaryWorkbook(zero: true, account: account.AccountNumber!);
            await service.ImportClientStatementsAsync(id, workspace.Version, [new("preserved.xlsx", original)], actor);
            var statement = await db.PerformanceFeeStatements.AsNoTracking().SingleAsync(x => x.PerformanceFeeProfileId == id);
            var legacy = FeeJson.Read<FeeStatementDocument>(statement.DocumentJson); legacy.ParserVersion = 0; legacy.Periods.Clear();
            await db.PerformanceFeeStatements.Where(x => x.Id == statement.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.DocumentJson, FeeJson.Write(legacy)));
            workspace = await service.ClientWorkspaceAsync(id, actor);
            Assert.Single(workspace.Accounts.Single().Ledger.History.Periods);
            Assert.True(workspace.Accounts.Single().RequiresStatementRebuild);
            Assert.Equal(0, FeeJson.Read<FeeStatementDocument>((await db.PerformanceFeeStatements.AsNoTracking().SingleAsync(x => x.Id == statement.Id)).DocumentJson).ParserVersion);
            profile = await service.LoadAsync(id, actor);
            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ApproveAsync(id, profile.Claims.Single().Id, profile.Version, "Must reject an old interpretation", actor));
            Assert.Contains("Statement summaries", ex.Message);
            await service.PrepareAllClientStatementsAsync(id, workspace.Version, [], "", actor);
            workspace = await service.ClientWorkspaceAsync(id, actor);
            Assert.False(workspace.Accounts.Single().RequiresStatementRebuild);
            var saved = await db.PerformanceFeeStatements.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
            Assert.Equal(PerformanceFeeStatementReader.CurrentVersion, FeeJson.Read<FeeStatementDocument>(saved.DocumentJson).ParserVersion);
            Assert.Equal(original, saved.Content); Assert.Equal(statement.Sha256, saved.Sha256); Assert.Equal(statement.ImportedAtUtc, saved.ImportedAtUtc);
            Assert.Equal(new DateOnly(2000, 1, 1), (await db.ClientInvestmentAccounts.AsNoTracking().SingleAsync(x => x.Id == account.Id)).InvestmentDate);
            Assert.Equal(new DateOnly(2021, 7, 1), workspace.Accounts.Single().Profile!.InceptionDate);
            Assert.Null(workspace.Accounts.Single().Profile!.Claims.Single().ApprovedAtUtc);
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteDeleteAsync();
            await db.ClientInvestmentAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync(); await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
            await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync();
        }
    }

    internal static byte[] MultiAccount(bool shifted, params string[] accounts)
    {
        var template = PerformanceFeeStatementTests.Workbook(account: accounts[0], shift: shifted ? 3 : 0);
        using var input = new ZipArchive(new MemoryStream(template));
        var sheet = XDocument.Load(input.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = sheet.Root!.Element(ns + "sheetData")!; var originalRows = data.Elements().ToArray();
        foreach (var (account, index) in accounts.Skip(1).Select((a, i) => (a, i + 1)))
        {
            foreach (var row in originalRows)
            {
                var copy = new XElement(row); var number = (int)row.Attribute("r")! + index * 20; copy.SetAttributeValue("r", number);
                foreach (var cell in copy.Elements()) cell.SetAttributeValue("r", new string(cell.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()) + number);
                foreach (var text in copy.Descendants(ns + "t")) text.Value = text.Value.Replace(accounts[0], account, StringComparison.Ordinal);
                data.Add(copy);
            }
        }
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, true))
            foreach (var entry in input.Entries)
            { using var target = zip.CreateEntry(entry.FullName).Open(); if (entry.FullName.EndsWith("sheet1.xml")) sheet.Save(target); else { using var source = entry.Open(); source.CopyTo(target); } }
        return result.ToArray();
    }
}
