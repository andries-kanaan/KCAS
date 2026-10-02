using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KCAS.Admin.Tests;

public sealed partial class EmployeeComplianceServiceTests
{
    [Fact]
    public async Task Employee_transfer_baseline_maps_different_ids_accounts_and_live_files_without_granting_roles()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        using var fixture = TransferFixture(scope);
        var id = await fixture.Source.SaveProfileAsync(Profile(), "Synthetic baseline", admin);
        var subject = await ActorAsync(scope, KcasRoles.ReadOnly);
        var p = await fixture.Source.LoadAsync(id, admin);
        await fixture.Source.LinkAccountAsync(id, p.Profile.Version, subject.FindFirstValue(ClaimTypes.NameIdentifier)!, "Synthetic identity", admin);
        var reference = @"Z:\Kanaan Trust\Compliance\Training\" + Guid.NewGuid().ToString("N") + ".txt";
        foreach (var resolver in new[] { fixture.SourceFiles, fixture.LiveFiles })
        {
            var path = resolver.Resolve(reference); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "Synthetic training evidence");
        }
        p = await fixture.Source.LoadAsync(id, admin);
        await fixture.Source.LinkEvidenceAsync(id, p.Profile.Version, new() { Title = "Training fixture", Category = "Training", EvidencePath = reference, SourceNote = "Actual synthetic file, not a clearance" }, admin);
        p = await fixture.Source.LoadAsync(id, admin);
        var content = await ExportBytesAsync(fixture.Export, [id], admin);
        Assert.DoesNotContain(p.Profile.LegalName, Encoding.UTF8.GetString(content));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var originalUser = await users.FindByIdAsync(subject.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var email = originalUser!.Email!;
        originalUser.Email = originalUser.UserName = Guid.NewGuid().ToString("N") + "@example.test";
        Assert.True((await users.UpdateAsync(originalUser)).Succeeded);
        var liveUser = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        Assert.True((await users.CreateAsync(liveUser)).Succeeded);
        Assert.True((await users.AddToRoleAsync(liveUser, KcasRoles.ReadOnly)).Succeeded);
        await RemoveSourceEmployeesAsync(scope, [id]);
        var preview = await fixture.Import.PreviewAsync(content, "TestPwd7", admin);
        Assert.True(preview.CanApply, string.Join(" ", preview.Conflicts));
        Assert.Equal("Create employee", Assert.Single(preview.Rows).Action);
        Assert.Equal(1, await fixture.Import.ApplyAsync(content, "TestPwd7", preview.TargetStamp, "Synthetic live import", admin));
        var target = Assert.Single(await fixture.Live.RegisterAsync(admin), x => x.Profile.TransferKey == p.Profile.TransferKey);
        Assert.NotEqual(id, target.Profile.Id);
        Assert.Null(target.LatestReview);
        var livePage = await fixture.Live.LoadAsync(target.Profile.Id, admin);
        Assert.Equal(liveUser.Id, Assert.Single(livePage.Accounts).Id);
        Assert.Equal(new[] { KcasRoles.ReadOnly }, await users.GetRolesAsync(liveUser));
        var doc = Assert.Single(await fixture.Live.EvidenceAsync(target.Profile.Id, admin));
        var opened = await fixture.Live.OpenDocumentAsync(doc.Id, admin);
        Assert.NotNull(opened); await opened.Value.Stream.DisposeAsync();
        var repeat = await fixture.Import.PreviewAsync(content, "TestPwd7", admin);
        Assert.Equal("Already imported", Assert.Single(repeat.Rows).Action);
        Assert.Equal(0, await fixture.Import.ApplyAsync(content, "TestPwd7", repeat.TargetStamp, "Repeat test import", admin));
        Assert.Single(await fixture.Live.EvidenceAsync(target.Profile.Id, admin));
    }

    [Fact]
    public async Task Employee_transfer_restores_missing_files_and_blocks_changed_live_evidence_before_apply()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        using var fixture = TransferFixture(scope);
        var id = await fixture.Source.SaveProfileAsync(Profile(), "Evidence fixture", admin);
        var reference = "Training/" + Guid.NewGuid().ToString("N") + ".txt";
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.SourceFiles.Resolve(reference))!);
        await File.WriteAllTextAsync(fixture.SourceFiles.Resolve(reference), "Synthetic evidence");
        var page = await fixture.Source.LoadAsync(id, admin);
        await fixture.Source.LinkEvidenceAsync(id, page.Profile.Version, new() { Title = "Fixture", Category = "Identity", EvidencePath = reference, SourceNote = "Fixture source" }, admin);
        page = await fixture.Source.LoadAsync(id, admin);
        var bytes = await ExportBytesAsync(fixture.Export, [id], admin);
        await RemoveSourceEmployeesAsync(scope, [id]);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.LiveFiles.Resolve(reference))!);
        await File.WriteAllTextAsync(fixture.LiveFiles.Resolve(reference), "Different live evidence");
        var blocked = await fixture.Import.PreviewAsync(bytes, "TestPwd7", admin);
        Assert.False(blocked.CanApply); Assert.Contains(blocked.Conflicts, x => x.Contains("differs"));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Import.ApplyAsync(bytes, "TestPwd7", blocked.TargetStamp, "Blocked", admin));
        File.Delete(fixture.LiveFiles.Resolve(reference));
        var ready = await fixture.Import.PreviewAsync(bytes, "TestPwd7", admin);
        Assert.True(ready.CanApply); Assert.Contains(ready.Warnings, x => x.Contains("restored"));
        Assert.Equal(1, await fixture.Import.ApplyAsync(bytes, "TestPwd7", ready.TargetStamp, "Restore test evidence", admin));
        var target = Assert.Single(await fixture.Live.RegisterAsync(admin), x => x.Profile.TransferKey == page.Profile.TransferKey);
        var doc = Assert.Single(await fixture.Live.EvidenceAsync(target.Profile.Id, admin));
        Assert.StartsWith("KCAS employee transfers/Evidence/", doc.EvidencePath);
        Assert.Equal("Synthetic evidence", await File.ReadAllTextAsync(fixture.LiveFiles.Resolve(doc.EvidencePath)));
    }

    [Fact]
    public async Task Employee_transfer_keeps_full_approved_history_and_requires_actual_live_access_verification()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        using var fixture = TransferFixture(scope);
        var reviewer = await LinkedReviewerAsync(scope, fixture.Source, admin);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reviewerUserId = reviewer.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var reviewerId = await db.EmployeeAccountLinks.Where(x => x.UserId == reviewerUserId).Select(x => x.EmployeeProfileId).SingleAsync();
        var id = await fixture.Source.SaveProfileAsync(Profile(), "Approved fixture", admin);
        await CompletePrerequisitesAsync(fixture.Source, id, admin);
        var page = await fixture.Source.LoadAsync(id, admin);
        await fixture.Source.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version, "Approved", "Actual synthetic uninvolved decision", "", 24, reviewer);
        page = await fixture.Source.LoadAsync(id, admin);
        var single = await ExportBytesAsync(fixture.Export, [id], admin);
        var bytes = await ExportBytesAsync(fixture.Export, [id, reviewerId], admin);
        await RemoveSourceEmployeesAsync(scope, [id, reviewerId]);
        var incomplete = await fixture.Import.PreviewAsync(single, "TestPwd7", admin);
        Assert.False(incomplete.CanApply);
        Assert.Contains(incomplete.Conflicts, x => x.Contains("reviewer") && x.Contains("missing"));
        var preview = await fixture.Import.PreviewAsync(bytes, "TestPwd7", admin);
        Assert.True(preview.CanApply, string.Join(" ", preview.Conflicts));
        Assert.Equal(2, await fixture.Import.ApplyAsync(bytes, "TestPwd7", preview.TargetStamp, "Transfer approval history", admin));
        var target = Assert.Single(await fixture.Live.RegisterAsync(admin), x => x.Profile.TransferKey == page.Profile.TransferKey);
        var live = await fixture.Live.LoadAsync(target.Profile.Id, admin);
        Assert.Equal("Approved", live.CurrentReview!.Status);
        Assert.Equal(page.CurrentReview!.CompletedAtUtc, live.CurrentReview.CompletedAtUtc);
        Assert.Equal(page.CurrentReview.NextReviewDate, live.CurrentReview.NextReviewDate);
        Assert.Equal(4, live.Checks.Count);
        Assert.Equal(page.Checks.Select(x => x.Performer), live.Checks.Select(x => x.Performer));
        var decision = Assert.Single(live.Decisions);
        Assert.NotEqual(reviewerId, decision.ReviewerEmployeeProfileId);
        Assert.Equal(page.Decisions.Single().ReviewerName, decision.ReviewerName);
        Assert.NotEqual(live.Profile.Version, live.CurrentReview.ProfileVersion);
        Assert.Contains(live.Blockers, x => x.Contains("fresh review"));
        Assert.Contains(live.Tasks, x => x.Kind == "TransferReview" && x.Status == "Open");
    }

    [Fact]
    public async Task Employee_transfer_updates_unchanged_imports_but_preserves_new_live_work()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        using var fixture = TransferFixture(scope);
        var id = await fixture.Source.SaveProfileAsync(Profile(), "Version one", admin);
        var page = await fixture.Source.LoadAsync(id, admin);
        var first = await ExportBytesAsync(fixture.Export, [id], admin);
        var edit = fixture.SourceEdit(page.Profile);
        edit.Responsibilities = "New sourced responsibilities";
        await fixture.Source.SaveProfileAsync(edit, "Source correction", admin);
        var second = await ExportBytesAsync(fixture.Export, [id], admin);
        edit = fixture.SourceEdit((await fixture.Source.LoadAsync(id, admin)).Profile); edit.AuthorityLimits = "Third source revision";
        await fixture.Source.SaveProfileAsync(edit, "Source correction three", admin);
        var third = await ExportBytesAsync(fixture.Export, [id], admin);
        await RemoveSourceEmployeesAsync(scope, [id]);
        foreach (var bytes in new[] { first, second })
        {
            var preview = await fixture.Import.PreviewAsync(bytes, "TestPwd7", admin);
            Assert.True(preview.CanApply, string.Join(" ", preview.Conflicts));
            Assert.Equal(1, await fixture.Import.ApplyAsync(bytes, "TestPwd7", preview.TargetStamp, "Next source revision", admin));
        }
        var target = Assert.Single(await fixture.Live.RegisterAsync(admin), x => x.Profile.TransferKey == page.Profile.TransferKey);
        Assert.Equal("New sourced responsibilities", target.Profile.Responsibilities);
        var older = await fixture.Import.PreviewAsync(first, "TestPwd7", admin);
        Assert.Equal("Already imported", Assert.Single(older.Rows).Action);
        Assert.Equal(0, await fixture.Import.ApplyAsync(first, "TestPwd7", older.TargetStamp, "Previously applied old package", admin));
        var localEdit = fixture.SourceEdit(target.Profile); localEdit.AuthorityLimits = "Actual live-only change";
        await fixture.Live.SaveProfileAsync(localEdit, "Do not overwrite live work", admin);
        var blocked = await fixture.Import.PreviewAsync(third, "TestPwd7", admin);
        Assert.False(blocked.CanApply); Assert.Contains(blocked.Conflicts, x => x.Contains("live employee work changed"));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Import.ApplyAsync(third, "TestPwd7", blocked.TargetStamp, "Attempt overwrite", admin));
        Assert.Equal("Actual live-only change", (await fixture.Live.LoadAsync(target.Profile.Id, admin)).Profile.AuthorityLimits);
    }

    [Fact]
    public async Task Employee_transfer_blocks_stale_preview_and_bad_encryption()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        using var fixture = TransferFixture(scope);
        var id = await fixture.Source.SaveProfileAsync(Profile(), "Stale fixture", admin);
        var page = await fixture.Source.LoadAsync(id, admin);
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Export.ExportAsync([id], "short", "No short passphrase", admin));
        var bytes = await ExportBytesAsync(fixture.Export, [id], admin);
        await RemoveSourceEmployeesAsync(scope, [id]);
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Import.PreviewAsync(bytes, "WrongPW", admin));
        var corrupt = bytes.ToArray(); corrupt[^1] ^= 1;
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Import.PreviewAsync(corrupt, "TestPwd7", admin));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Import.PreviewAsync(bytes[..12], "TestPwd7", admin));
        var preview = await fixture.Import.PreviewAsync(bytes, "TestPwd7", admin);
        var competing = fixture.SourceEdit(page.Profile); competing.Id = null; competing.Version = null;
        await fixture.Live.SaveProfileAsync(competing, "Concurrent live employee", admin);
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Import.ApplyAsync(bytes, "TestPwd7", preview.TargetStamp, "Stale preview", admin));
    }

    [Fact]
    public async Task Employee_transfers_require_current_admin_at_service_page_and_download()
    {
        using var scope = factory.Services.CreateScope();
        var manager = await ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        using var fixture = TransferFixture(scope);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Export.ExportAsync([1], "TestPwd7", "Not administrator", manager));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Import.DownloadAsync(Guid.NewGuid().ToString("N"), manager));
        var http = await HttpClientAsync(scope, manager);
        foreach (var route in new[] { "/compliance/employees/transfers", "/compliance/employees/transfers/" + Guid.NewGuid().ToString("N") + "/download" })
            Assert.True((await http.GetAsync(route)).StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Redirect);
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(admin.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await userManager.RemoveFromRoleAsync(user!, KcasRoles.Administrator)).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Export.ExportAsync([1], "TestPwd7", "Stale cookie", admin));
    }

    private static async Task<byte[]> ExportBytesAsync(EmployeeTransferService service, int[] ids, ClaimsPrincipal actor)
    {
        var exported = await service.ExportAsync(ids, "TestPwd7", "Synthetic transfer fixture", actor);
        var download = await service.DownloadAsync(exported.PackageId, actor);
        Assert.NotNull(download);
        await using var stream = download.Value.Content; using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
    private static TransferTestFixture TransferFixture(IServiceScope scope) => new(scope);
    private static async Task RemoveSourceEmployeesAsync(IServiceScope scope, int[] ids)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.ChangeTracker.Clear();
        var reviews = await db.EmployeeComplianceReviews.Where(x => ids.Contains(x.EmployeeProfileId)).Select(x => x.Id).ToListAsync();
        await db.EmployeeReviewDecisions.Where(x => reviews.Contains(x.EmployeeComplianceReviewId)).ExecuteDeleteAsync();
        foreach (var check in await db.EmployeeComplianceChecks.Where(x => reviews.Contains(x.EmployeeComplianceReviewId)).OrderByDescending(x => x.Id).ToListAsync())
        { db.EmployeeComplianceChecks.Remove(check); await db.SaveChangesAsync(); }
        await db.EmployeeAccessConfirmations.Where(x => reviews.Contains(x.EmployeeComplianceReviewId)).ExecuteDeleteAsync();
        await db.EmployeeComplianceReviews.Where(x => reviews.Contains(x.Id)).ExecuteDeleteAsync();
        await db.EmployeeEvidenceDocuments.Where(x => ids.Contains(x.EmployeeProfileId)).ExecuteDeleteAsync();
        await db.EmployeeComplianceTasks.Where(x => ids.Contains(x.EmployeeProfileId)).ExecuteDeleteAsync();
        await db.EmployeeComplianceAuditEvents.Where(x => ids.Contains(x.EmployeeProfileId)).ExecuteDeleteAsync();
        await db.EmployeeAccountLinks.Where(x => ids.Contains(x.EmployeeProfileId)).ExecuteDeleteAsync();
        await db.EmployeeTransferRecords.Where(x => ids.Contains(x.EmployeeProfileId)).ExecuteDeleteAsync();
        await db.EmployeeProfiles.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(); db.ChangeTracker.Clear();
    }
    private sealed class TransferTestFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "kcas-employee-transfer-" + Guid.NewGuid().ToString("N"));
        public EmployeeComplianceService Source { get; }
        public EmployeeComplianceService Live { get; }
        public EmployeeEvidenceFiles SourceFiles { get; }
        public EmployeeEvidenceFiles LiveFiles { get; }
        public EmployeeTransferService Export { get; }
        public EmployeeTransferService Import { get; }
        public TransferTestFixture(IServiceScope scope)
        {
            var sourceConfig = Config("source"); var liveConfig = Config("live");
            SourceFiles = new(sourceConfig); LiveFiles = new(liveConfig);
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            Source = new(dbFactory, SourceFiles); Live = new(dbFactory, LiveFiles);
            Export = new(dbFactory, SourceFiles, sourceConfig, environment); Import = new(dbFactory, LiveFiles, liveConfig, environment);
        }
        private IConfiguration Config(string name) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["EmployeeCompliance:EvidenceRoot"] = Path.Combine(root, name, "Compliance"), ["EmployeeTransfers:StorageRoot"] = Path.Combine(root, name, "packages") }).Build();
        public EmployeeProfileEdit SourceEdit(EmployeeProfile p) => new()
        {
            Id = p.Id, Version = p.Version, DisplayName = p.DisplayName, LegalName = p.LegalName, Aliases = p.Aliases, Email = p.Email,
            IdentityReference = p.IdentityReference, Responsibilities = p.Responsibilities, AuthorityLimits = p.AuthorityLimits,
            SourceReference = p.SourceReference, RoleExposure = p.RoleExposure, RiskRationale = p.RiskRationale, SelectedChecks = p.SelectedChecks,
            RequireTraining = p.RequireTraining, RequireRegulatedCompetence = p.RequireRegulatedCompetence, RequireAdditionalCheck = p.RequireAdditionalCheck,
            ProposedReviewMonths = p.ProposedReviewMonths, EmploymentStatus = p.EmploymentStatus, ExternalAccessScope = p.ExternalAccessScope
        };
        public void Dispose()
        {
            var path = Path.GetFullPath(root);
            if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("kcas-employee-transfer-")) throw new InvalidOperationException("Unexpected fixture cleanup target.");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
