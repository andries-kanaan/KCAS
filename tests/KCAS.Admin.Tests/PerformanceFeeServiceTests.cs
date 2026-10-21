using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class PerformanceFeeServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Statement_preparation_records_scope_review_but_public_drafts_cannot_forge_its_resolution()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var client = new Client { DisplayName = "Synthetic wrap entry " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "ENTRY-" + Guid.NewGuid().ToString("N"), InvestmentDate = new(2010, 1, 1) };
        db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var document = PerformanceFeeScopeEntryTests.Document();
            document.ParserVersion = PerformanceFeeStatementReader.CurrentVersion;
            foreach (var row in document.Rows) row.Account = account.AccountNumber;
            document.Periods.Single().Account = account.AccountNumber;
            db.PerformanceFeeStatements.Add(new() { PerformanceFeeProfileId = id, FileName = document.FileName,
                Sha256 = document.Sha256, DocumentJson = FeeJson.Write(document), ImportedBy = actor.Identity!.Name! });
            await db.SaveChangesAsync();
            var profile = await service.LoadAsync(id, actor);
            await service.PrepareStatementsAsync(id, profile.Version, actor);
            profile = await service.LoadAsync(id, actor);
            var input = FeeJson.Read<FeePreparation>(profile.PreparationJson);
            var point = Assert.Single(input.ReviewPoints); Assert.False(point.Resolved);
            var forged = FeeJson.Read<FeePreparation>(profile.PreparationJson);
            var fake = forged.ReviewPoints.Single();
            fake.ResolvedBy = "Not a KI decision"; fake.ResolvedAtUtc = DateTime.UtcNow; fake.ResolutionReference = "Forged";
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareAsync(id, profile.Version, forged, "Cannot forge", actor));
            var workspace = await service.ClientWorkspaceAsync(id, actor);
            await service.PrepareAllClientStatementsAsync(id, workspace.Version, [], "", actor);
            profile = await service.LoadAsync(id, actor);
            Assert.Equal(FeeJson.Write(input.ReviewPoints), FeeJson.Write(FeeJson.Read<FeePreparation>(profile.PreparationJson).ReviewPoints));
            Assert.Equal(FeeClaimStages.Draft, Assert.Single(profile.Claims).Status);
        }
        finally { await Cleanup(db, id, account, client, null); }
    }

    [Fact]
    public async Task Per_wrap_claim_exports_and_checkpoint_freeze_protect_each_wrap_separately()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic multi-wrap KI", Email = actor.Identity!.Name, IsActive = true };
        var client = new Client { DisplayName = "Synthetic separate-wrap client " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "SYNTHETIC-MULTI", InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var input = PerformanceFeeCalculatorTests.Input();
            var loss = PerformanceFeeCalculatorTests.Input(); loss.ClosingValue = 80000;
            var gain = PerformanceFeeCalculatorTests.Input(); gain.ScopeRationale = "C:\\private\\synthetic-evidence";
            input.Wraps = [new() { Code = "KE1", Input = gain }, new() { Code = "KA6", Input = loss }];
            var row = await service.LoadAsync(id, actor);
            var claimId = await service.PrepareAsync(id, row.Version, input, "Synthetic independent wrap calculation", actor);
            row = await service.LoadAsync(id, actor); await service.ApproveAsync(id, claimId, row.Version, "Synthetic KI verifies independent scopes", actor);
            var bytes = await scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>().PackageAsync([claimId], actor);
            using var zip = new ZipArchive(new MemoryStream(bytes));
            using var reader = new StreamReader(zip.GetEntry("Claim schedule.csv")!.Open());
            var csv = await reader.ReadToEndAsync(); Assert.Contains("KE1", csv); Assert.Contains("KA6", csv); Assert.DoesNotContain("private", csv);
            using var pdf = zip.GetEntry((await service.LoadAsync(id, actor)).Claims.Single().Reference + ".pdf")!.Open();
            using var buffer = new MemoryStream(); await pdf.CopyToAsync(buffer); Assert.DoesNotContain("private", Encoding.Latin1.GetString(buffer.ToArray()));
            foreach (var stage in new[] { FeeClaimStages.Submitted, FeeClaimStages.Accepted, FeeClaimStages.Deducted })
            {
                row = await service.LoadAsync(id, actor);
                await service.RecordAsync(id, claimId, row.Version, stage, DateOnly.FromDateTime(DateTime.Today), "Synthetic " + stage, "Actual synthetic event", stage == FeeClaimStages.Deducted ? 2242.5m : null, actor);
            }
            row = await service.LoadAsync(id, actor);
            var cp = System.Text.Json.JsonSerializer.Deserialize<FeeCheckpoint>(row.CheckpointJson!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(107000m, cp.Wraps.Single(x => x.Code == "KA6").Checkpoint.Benchmark);
            input.From = cp.Date; input.To = cp.Date.AddDays(365);
            foreach (var wrap in input.Wraps)
            {
                var child = wrap.Input; var frozen = cp.Wraps.Single(x => x.Code == wrap.Code).Checkpoint;
                child.From = input.From; child.To = input.To; child.OpeningValue = frozen.Value; child.OpeningBenchmark = frozen.Benchmark;
                child.OpeningHighWaterMark = frozen.HighWaterMark; child.Benchmarks = [new() { From = child.From.Value, To = child.To.Value, CpiPercent = 4, Reference = "Synthetic new CPI" }];
            }
            input.Wraps[1].Input.OpeningBenchmark = 80000;
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareAsync(id, row.Version, input, "Cannot reset losing wrap", actor));
            input.Wraps[1].Input.OpeningBenchmark = 107000;
            await service.PrepareAsync(id, row.Version, input, "Preserve each wrap checkpoint", actor);
        }
        finally { await Cleanup(db, id, account, client, assignment); }
    }

    [Fact]
    public async Task Statement_preview_import_review_and_preparation_preserve_source_and_permissions()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var reader = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Reports);
        var client = new Client { DisplayName = "Synthetic statement client " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic", ExcludeFromComplianceLists = false };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "SYNTHETIC-IA", FundName = "Kanaan Local Wrap Funds", InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var row = await service.LoadAsync(id, actor);
            var bytes = PerformanceFeeStatementTests.Workbook();
            var preview = await service.PreviewStatementAsync(id, bytes, "synthetic.xlsx", actor);
            Assert.False(preview.AlreadyImported); Assert.Empty(preview.OtherAccounts);
            Assert.False(await db.PerformanceFeeStatements.AnyAsync(x => x.PerformanceFeeProfileId == id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewStatementAsync(id, bytes, "synthetic.xlsx", reader));
            await Assert.ThrowsAsync<ValidationException>(() => service.PreviewStatementAsync(id, PerformanceFeeStatementTests.Workbook(account: "OTHER"), "other.xlsx", actor));
            var statementId = await service.ImportStatementAsync(id, row.Version, bytes, "synthetic.xlsx", actor);
            await Assert.ThrowsAsync<ValidationException>(() => service.ImportStatementAsync(id, row.Version, bytes, "synthetic.xlsx", actor));
            row = await service.LoadAsync(id, actor);
            Assert.Equal(statementId, await service.ImportStatementAsync(id, row.Version, bytes, "synthetic.xlsx", actor));
            Assert.Equal(bytes, (await service.DownloadStatementAsync(id, statementId, actor)).Content);
            Assert.Single((await service.StatementsAsync(id, actor)).Receipts);
            Assert.Equal(3, (await service.StatementsAsync(id, actor)).Ledger.Rows.Count);
            var claimId = await service.PrepareStatementsAsync(id, row.Version, actor);
            row = await service.LoadAsync(id, actor);
            var claim = row.Claims.Single(x => x.Id == claimId);
            var input = FeeJson.Read<FeePreparation>(claim.InputJson);
            Assert.Single(input.Wraps); Assert.Empty(input.Wraps[0].Input.Movements);
            Assert.False(FeeJson.Read<FeeCalculationResult>(claim.ResultJson).IsReady);
            var source = (await service.StatementsAsync(id, actor)).Ledger.Rows.Single(x => x.Kind == "Reinvestment");
            await service.ReviewStatementRowAsync(id, row.Version, source.Key, "Unknown", "Synthetic evidence ambiguity retained", actor);
            row = await service.LoadAsync(id, actor);
            Assert.Contains(FeeJson.Read<FeeCalculationResult>(row.Claims.Single().ResultJson).Issues, x => x.Contains("classification changed"));
            Assert.Contains(row.Events, x => x.Action == "StatementRowReviewed" && x.RecordedBy == actor.Identity!.Name);
            var (http, _) = await AuthenticatedHttp(scope, actor);
            using (http)
            {
                var page = await http.GetStringAsync($"/investments/performance-fees/{id}/statements");
                Assert.Contains("Administrator statements", page); Assert.Contains("synthetic.xlsx", page);
                Assert.Contains("Prepare draft from statements", page);
            }
            Assert.False(await db.ClientInvestmentTransactions.AnyAsync(x => x.ClientInvestmentAccountId == account.Id));
            Assert.False(await db.ClientFundValuations.AnyAsync(x => x.ClientId == client.Id));
        }
        finally { await Cleanup(db, id, account, client, null); }
    }

    [Fact]
    public async Task Scoped_preparation_approval_export_deduction_and_payment_preserve_checkpoint_and_audit()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var exports = scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>();
        var compliance = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var ki = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic fee KI", Email = ki.Identity!.Name, IsActive = true };
        var client = new Client { DisplayName = "Synthetic fee client " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic", KanaanId = "TEST", ClientFolder = "Synthetic private folder" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "SYNTHETIC-FEE", FundName = "Synthetic SA wrap", Administrator = "Synthetic administrator", InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var profileId = 0;
        try
        {
            profileId = await service.RegisterAsync(account.Id, ki);
            Assert.Equal(profileId, await service.RegisterAsync(account.Id, ki));
            var row = await service.LoadAsync(profileId, compliance);
            var originalVersion = row.Version;
            var handoff = await service.RequestCodexAsync(profileId, row.Version, ki);
            Assert.Contains("Top-ups do not reset", handoff); Assert.Contains("actual benchmark segments", handoff);
            row = await service.LoadAsync(profileId, compliance);
            var requestVersion = row.Version;
            Assert.Equal(handoff, await service.HandoffAsync(profileId, compliance));
            Assert.Equal(requestVersion, (await service.LoadAsync(profileId, compliance)).Version);
            var actions = await scope.ServiceProvider.GetRequiredService<UserActionService>().LoadAsync(compliance);
            Assert.Contains(actions, x => x.Key == $"pdf-codex:{profileId}");
            var input = PerformanceFeeCalculatorTests.Input();
            input.MandateReference = "C:\\private\\synthetic-mandate.pdf";
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareAsync(profileId, originalVersion, input, "Stale result", compliance, true));
            var claimId = await service.PrepareAsync(profileId, row.Version, input, "Actual synthetic mandate and complete cash ledger reconciled", compliance, true);
            row = await service.LoadAsync(profileId, ki);
            Assert.Contains(row.Events, e => e.Action == "CalculationPrepared" && e.Performer == "Codex" && e.RecordedBy == compliance.Identity!.Name);
            Assert.Null(row.CodexRequestVersion);
            Assert.Null(await service.HandoffAsync(profileId, compliance));
            Assert.Contains(await scope.ServiceProvider.GetRequiredService<UserActionService>().LoadAsync(ki), x => x.Key == $"pdf-approve:{claimId}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveAsync(profileId, claimId, row.Version, "Not a KI", compliance));
            await Assert.ThrowsAsync<ValidationException>(() => exports.PackageAsync([claimId], ki));
            await service.ApproveAsync(profileId, claimId, row.Version, "Mandate convention, scope and calculation verified", ki);
            var approvedVersion = row.Version;
            row = await service.LoadAsync(profileId, ki);
            var claim = row.Claims.Single(); Assert.Equal(FeeClaimStages.Approved, claim.Status); Assert.Null(row.CheckpointJson);
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareAsync(profileId, row.Version, input, "Cannot overwrite approved calculation", ki));
            var zipBytes = await exports.PackageAsync([claimId], ki);
            using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
            {
                Assert.Equal(4, zip.Entries.Count); Assert.NotNull(zip.GetEntry("Administrator request.pdf")); Assert.NotNull(zip.GetEntry("Claim schedule.csv"));
                using var csv = new StreamReader(zip.GetEntry("Claim schedule.csv")!.Open());
                Assert.DoesNotContain("private", await csv.ReadToEndAsync());
                using var manifest = new StreamReader(zip.GetEntry("manifest.json")!.Open());
                Assert.DoesNotContain("private", await manifest.ReadToEndAsync());
                using var pdf = zip.GetEntry(claim.Reference + ".pdf")!.Open(); using var buffer = new MemoryStream(); await pdf.CopyToAsync(buffer);
                Assert.DoesNotContain("private", Encoding.Latin1.GetString(buffer.ToArray()));
            }
            var fresh = await service.LoadAsync(profileId, ki);
            Assert.Equal(row.Version, fresh.Version); Assert.Equal(FeeClaimStages.Approved, fresh.Claims.Single().Status);
            var draftPdf = await exports.PreviewAsync(profileId, claimId, ki);
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(draftPdf, 0, 8));
            var qa = Environment.GetEnvironmentVariable("KCAS_FEE_QA_PATH");
            if (!string.IsNullOrWhiteSpace(qa)) { Directory.CreateDirectory(qa); await File.WriteAllBytesAsync(Path.Combine(qa, "draft.pdf"), draftPdf); await File.WriteAllBytesAsync(Path.Combine(qa, "submission.zip"), zipBytes); }
            var today = DateOnly.FromDateTime(DateTime.Today);
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(profileId, claimId, approvedVersion, FeeClaimStages.Submitted, today, "Synthetic send", "Stale action", null, ki));
            await service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Submitted, today, "Synthetic sent email", "Actually submitted", null, ki);
            row = await service.LoadAsync(profileId, ki);
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Deducted, today, "Synthetic", "Cannot skip acceptance", 2242.5m, ki));
            await service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Accepted, today, "Synthetic acceptance", "Accepted exact claim", null, ki);
            row = await service.LoadAsync(profileId, ki);
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Deducted, today, "Synthetic wrong amount", "Must reconcile", 1, ki));
            await service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Deducted, today, "Synthetic deducted statement", "Approved fee deducted", 2242.5m, ki);
            row = await service.LoadAsync(profileId, ki);
            Assert.NotNull(row.CheckpointJson); Assert.Equal(new DateOnly(2023, 1, 1), row.NextReviewDate);
            await service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Paid, today, "Synthetic bank receipt part 1", "Actual partial receipt", 1000m, ki);
            row = await service.LoadAsync(profileId, ki); Assert.Equal(FeeClaimStages.Deducted, row.Claims.Single().Status);
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Paid, today, "Synthetic bank receipt part 1", "Duplicate partial receipt", 500m, ki));
            await service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Paid, today, "Synthetic bank receipt part 2", "Actual final receipt", 1242.5m, ki);
            row = await service.LoadAsync(profileId, ki); Assert.Equal(FeeClaimStages.Paid, row.Claims.Single().Status);
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(profileId, claimId, row.Version, FeeClaimStages.Paid, today, "Synthetic duplicate receipt", "Must not duplicate", 2242.5m, ki));
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareAsync(profileId, row.Version, input, "Cannot charge inception again", ki));
            await AssertPages(scope, compliance, profileId);
            await AssertPages(scope, ki, profileId);
        }
        finally { await Cleanup(db, profileId, account, client, assignment); }
    }

    [Fact]
    public async Task Zero_fee_preserves_hurdle_and_hidden_clients_remain_admin_only_even_via_exports()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var exports = scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>();
        var admin = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var reader = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Reports);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic fee admin KI", Email = admin.Identity!.Name, IsActive = true };
        var client = new Client { DisplayName = "Synthetic private fee client", SurnameOrEntityName = "Synthetic", ExcludeFromComplianceLists = true };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "SYNTHETIC-HIDDEN", InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, admin);
            Assert.DoesNotContain(await service.ListAsync(reader), x => x.Id == id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadAsync(id, reader));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RegisterAsync(account.Id, reader));
            var input = PerformanceFeeCalculatorTests.Input(); input.ClosingValue = 80000;
            var row = await service.LoadAsync(id, admin);
            var claimId = await service.PrepareAsync(id, row.Version, input, "Synthetic zero-fee catch-up", admin);
            row = await service.LoadAsync(id, admin);
            await service.ApproveAsync(id, claimId, row.Version, "Zero fee, protect unrecovered performance", admin);
            row = await service.LoadAsync(id, admin);
            Assert.Equal(FeeClaimStages.NoFee, row.Claims.Single().Status); Assert.Contains("107000", row.CheckpointJson);
            await Assert.ThrowsAsync<ValidationException>(() => exports.PackageAsync([claimId], admin));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exports.PreviewAsync(id, claimId, reader));
            await Assert.ThrowsAsync<ValidationException>(() => service.PrepareAsync(id, row.Version, input, "Cannot reset hurdle", admin));
            var checkpoint = System.Text.Json.JsonSerializer.Deserialize<FeeCheckpoint>(row.CheckpointJson!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            input.From = checkpoint.Date; input.To = checkpoint.Date.AddDays(365); input.OpeningValue = checkpoint.Value; input.OpeningBenchmark = checkpoint.Benchmark; input.OpeningHighWaterMark = checkpoint.HighWaterMark;
            input.NoPriorFeeConfirmed = false; input.ClosingValue = 105000; input.Benchmarks = [new() { From = input.From.Value, To = input.To.Value, CpiPercent = 4, Reference = "Synthetic next CPI" }];
            var next = await service.PrepareAsync(id, row.Version, input, "Carry loss forward correctly", admin);
            row = await service.LoadAsync(id, admin); Assert.Equal(0m, FeeJson.Read<FeeCalculationResult>(row.Claims.Single(x => x.Id == next).ResultJson).Total);
            input.To = input.From.Value.AddDays(30); input.Benchmarks[0].To = input.To.Value;
            var early = await service.PrepareAsync(id, row.Version, input, "Save short period before next scheduled anniversary", admin);
            row = await service.LoadAsync(id, admin);
            Assert.Contains(FeeJson.Read<FeeCalculationResult>(row.Claims.Single(x => x.Id == early).ResultJson).Issues, x => x.Contains("next anniversary"));
            await Assert.ThrowsAsync<ValidationException>(() => service.ApproveAsync(id, early, row.Version, "Cannot prematurely reset annual cycle", admin));
        }
        finally { await Cleanup(db, id, account, client, assignment); }
    }

    [Fact]
    public async Task Administrator_return_preserves_original_and_requires_a_new_KI_approved_claim()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var exports = scope.ServiceProvider.GetRequiredService<PerformanceFeeExports>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic returned fee KI", Email = actor.Identity!.Name, IsActive = true };
        var client = new Client { DisplayName = "Synthetic returned fee client", SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "SYNTHETIC-RETURN", InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0;
        try
        {
            id = await service.RegisterAsync(account.Id, actor);
            var row = await service.LoadAsync(id, actor);
            var incomplete = new FeePreparation();
            var draftId = await service.PrepareAsync(id, row.Version, incomplete, "Save genuine missing evidence", actor);
            row = await service.LoadAsync(id, actor);
            Assert.NotEmpty(FeeJson.Read<FeeCalculationResult>(row.Claims.Single().ResultJson).Issues);
            await Assert.ThrowsAsync<ValidationException>(() => service.ApproveAsync(id, draftId, row.Version, "Cannot approve missing terms", actor));
            var input = PerformanceFeeCalculatorTests.Input();
            var claimId = await service.PrepareAsync(id, row.Version, input, "Completed synthetic evidence", actor);
            Assert.Equal(draftId, claimId);
            row = await service.LoadAsync(id, actor); await service.ApproveAsync(id, claimId, row.Version, "Verified synthetic claim", actor);
            row = await service.LoadAsync(id, actor);
            await service.RecordAsync(id, claimId, row.Version, FeeClaimStages.Submitted, DateOnly.FromDateTime(DateTime.Today), "Synthetic submission", "Actual send", null, actor);
            row = await service.LoadAsync(id, actor);
            var originalJson = row.Claims.Single().InputJson;
            await service.RecordAsync(id, claimId, row.Version, FeeClaimStages.Returned, DateOnly.FromDateTime(DateTime.Today), "Synthetic administrator return", "Administrator requested a supported correction", null, actor);
            row = await service.LoadAsync(id, actor); Assert.Null(row.CheckpointJson);
            await Assert.ThrowsAsync<ValidationException>(() => exports.PackageAsync([claimId], actor));
            input.ClosingValue = 125000;
            var replacement = await service.PrepareAsync(id, row.Version, input, "Actual corrected statement, replaces returned claim", actor);
            Assert.NotEqual(claimId, replacement);
            row = await service.LoadAsync(id, actor);
            Assert.Equal(originalJson, row.Claims.Single(x => x.Id == claimId).InputJson);
            Assert.Equal(FeeClaimStages.Draft, row.Claims.Single(x => x.Id == replacement).Status);
            await service.ApproveAsync(id, replacement, row.Version, "Corrected actual statement verified", actor);
            Assert.NotEmpty(await exports.PackageAsync([replacement], actor));
        }
        finally { await Cleanup(db, id, account, client, assignment); }
    }

    [Fact]
    public async Task Initial_anniversary_discovery_excludes_passive_historical_new_and_hidden_accounts()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var adviser = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var admin = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var client = new Client { DisplayName = "Synthetic discovery " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var hidden = new Client { DisplayName = "Synthetic hidden discovery", SurnameOrEntityName = "Synthetic", ExcludeFromComplianceLists = true };
        var accounts = new List<ClientInvestmentAccount>
        {
            new() { Client = client, FundName = "Equities SA", InvestmentDate = today.AddYears(-3), AccountNumber = "SYNTHETIC-DUE" },
            new() { Client = client, FundName = "Global SA", InvestmentDate = today.AddYears(-1).AddDays(20), AccountNumber = "SYNTHETIC-SOON" },
            new() { Client = client, FundName = "Compulsory SA", InvestmentDate = today.AddMonths(-3), AccountNumber = "SYNTHETIC-NEW" },
            new() { Client = client, FundName = "Stable SA", InvestmentDate = today.AddYears(-3), AccountNumber = "SYNTHETIC-PASSIVE" },
            new() { Client = client, FundName = "Global SA", InvestmentDate = today.AddYears(-3), SurrenderDate = today.AddDays(-1), AccountNumber = "SYNTHETIC-ENDED" },
            new() { Client = hidden, FundName = "Global SA", InvestmentDate = today.AddYears(-3), AccountNumber = "SYNTHETIC-PRIVATE" }
        };
        db.ClientInvestmentAccounts.AddRange(accounts); await db.SaveChangesAsync();
        var profileId = 0;
        try
        {
            var ownIds = accounts.Select(x => x.Id).ToHashSet();
            var discovered = (await service.InitialReviewsAsync(adviser)).Where(x => ownIds.Contains(x.Id)).ToArray();
            Assert.Equal(new[] { accounts[0].Id, accounts[1].Id }.Order(), discovered.Select(x => x.Id).Order());
            Assert.Contains(await service.InitialReviewsAsync(admin), x => x.Id == accounts[5].Id);
            var (authenticated, _) = await AuthenticatedHttp(scope, adviser);
            using var http = authenticated;
            foreach (var account in accounts.Take(2))
            {
                var selected = await http.GetStringAsync($"/investments/performance-fees?accountId={account.Id}");
                Assert.Contains("Investment PDF preparation", selected);
                Assert.Contains(account.AccountNumber!, selected);
                Assert.Contains("Request Codex preparation", selected);
                Assert.Contains("Prepare manually", selected);
                Assert.DoesNotContain("No matching registered fee profiles", selected);
                Assert.DoesNotContain("Initial anniversary reviews", selected);
                Assert.DoesNotContain("Find investment", selected);
                Assert.DoesNotContain(accounts.First(x => x.Id != account.Id).AccountNumber!, selected);
                Assert.False(await db.PerformanceFeeProfiles.AnyAsync(x => x.ClientInvestmentAccountId == account.Id));
            }
            var central = await http.GetStringAsync("/investments/performance-fees");
            Assert.Contains("Initial anniversary reviews", central);
            Assert.Contains(accounts[0].AccountNumber!, central);
            Assert.Contains(accounts[1].AccountNumber!, central);
            profileId = await service.RegisterAsync(accounts[0].Id, adviser);
            Assert.Equal(profileId, Assert.Single(await service.ListAsync(adviser, accounts[0].Id)).Id);
            Assert.Empty(await service.ListAsync(adviser, accounts[1].Id));
            Assert.DoesNotContain(await service.InitialReviewsAsync(adviser), x => x.Id == accounts[0].Id);
            Assert.Contains(await scope.ServiceProvider.GetRequiredService<UserActionService>().LoadAsync(adviser), x => x.Key == $"pdf-anniversary:{profileId}");
            var existing = await http.GetAsync($"/investments/performance-fees?accountId={accounts[0].Id}");
            if (existing.StatusCode == System.Net.HttpStatusCode.Redirect)
                Assert.EndsWith($"/investments/performance-fees/{profileId}", existing.Headers.Location!.ToString());
            else
            {
                Assert.Equal(System.Net.HttpStatusCode.OK, existing.StatusCode);
                var html = await existing.Content.ReadAsStringAsync();
                Assert.Contains($"href=\"/investments/performance-fees/{profileId}\">Open fee review</a>", html);
                Assert.DoesNotContain("Initial anniversary reviews", html);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.PerformanceFeeProfiles.Where(x => x.Id == profileId).ExecuteDeleteAsync();
            var ids = accounts.Select(x => x.Id).ToArray();
            await db.ClientInvestmentAccounts.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Clients.Where(x => x.Id == client.Id || x.Id == hidden.Id).ExecuteDeleteAsync();
        }
    }

    internal async Task<(HttpClient Http, ClaimsPrincipal User)> AuthenticatedHttp(IServiceScope scope, ClaimsPrincipal actor)
    {
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!))!;
        var webPrincipal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user);
        var cookies = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = cookies.TicketDataFormat.Protect(new AuthenticationTicket(webPrincipal, new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, IdentityConstants.ApplicationScheme));
        var http = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await http.GetAsync("/investments/performance-fees")).StatusCode);
        http.DefaultRequestHeaders.Add("Cookie", $"{cookies.Cookie.Name}={ticket}");
        return (http, webPrincipal);
    }

    private async Task AssertPages(IServiceScope scope, ClaimsPrincipal actor, int id)
    {
        var (authenticated, webPrincipal) = await AuthenticatedHttp(scope, actor);
        using var http = authenticated;
        Assert.Contains("Annual PDF claims", await http.GetStringAsync("/investments/performance-fees"));
        var convention = await http.GetStringAsync("/investments/performance-fees/convention");
        Assert.Contains("PDF convention and VAT", convention);
        Assert.DoesNotContain("Calculation convention evidence and rationale<textarea", convention);
        Assert.Contains("Client PDF claim", await http.GetStringAsync($"/investments/performance-fees/{id}"));
        Assert.Contains("Signed mandate", await http.GetStringAsync($"/investments/performance-fees/{id}/prepare"));
        if (webPrincipal.HasClaim(KcasClaimTypes.Permission, KcasPermissions.ClientsView))
        {
            var service = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
            var profile = await service.LoadAsync(id, actor);
            var account = profile.Account;
            var url = $"/clients/{account.ClientId}?tab=investments";
            var link = $"href=\"/investments/performance-fees?accountId={account.Id}\"";
            foreach (var tab in new[] { "overview", "notes", "investments" })
            {
                var html = System.Net.WebUtility.HtmlDecode(await http.GetStringAsync($"/clients/{account.ClientId}?tab={tab}&returnTo=%2Fclients"));
                Assert.Contains($"href=\"/clients/{account.ClientId}?tab=investments&returnTo=%2Fclients\">View investments</a>", html);
                Assert.DoesNotContain("href=\"#investments\">View investments</a>", html);
                Assert.Contains($"href=\"/investments/summary?kanaanId={Uri.EscapeDataString(account.Client.KanaanId!)}\">Family investments (Kanaan ID {account.Client.KanaanId})</a>", html);
                Assert.DoesNotContain("kanaanId=client.KanaanId", html);
                Assert.DoesNotContain("kanaanId=model.", html);
            }
            var historical = await http.GetStringAsync(url);
            Assert.Contains("Historical investments", historical);
            Assert.Contains(link, historical);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var valuation = new ClientFundValuation { ClientId = account.ClientId, LegacyFundId = -Random.Shared.Next(1, int.MaxValue), FundName = account.FundName ?? "Synthetic SA", InvestmentUniqueNumber = account.AccountNumber,
                Administrator = account.Administrator, AmountZar = 120000, ValuationDate = DateOnly.FromDateTime(DateTime.Today) };
            db.ClientFundValuations.Add(valuation); await db.SaveChangesAsync();
            try
            {
                var current = await http.GetStringAsync(url);
                Assert.DoesNotContain("Historical investments (", current);
                Assert.Contains(link, current);
            }
            finally { await db.ClientFundValuations.Where(x => x.Id == valuation.Id).ExecuteDeleteAsync(); }
            var familyPage = await http.GetStringAsync($"/investments/summary?kanaanId={Uri.EscapeDataString(account.Client.KanaanId!)}");
            Assert.Contains("Investment Summary (IS)", familyPage);
            Assert.DoesNotContain("kanaanId=client.KanaanId", familyPage);
        }
    }

    private static async Task Cleanup(ApplicationDbContext db, int id, ClientInvestmentAccount account, Client client, GovernanceRoleAssignment? assignment)
    {
        db.ChangeTracker.Clear();
        await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteDeleteAsync();
        await db.ClientInvestmentAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync();
        await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
        if (assignment is not null) await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync();
    }
}
