using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed partial class EmployeeComplianceServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Personnel_pages_and_file_endpoints_deny_general_readers_even_with_a_valid_cookie()
    {
        using var scope = factory.Services.CreateScope();
        var reader = await ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        var client = await HttpClientAsync(scope, reader);
        foreach (var route in new[] { "/compliance/employees", "/compliance/employees/1", "/compliance/employees/1/update", "/compliance/employees/checks/1/file", "/compliance/employees/documents/1/file" })
        {
            var response = await client.GetAsync(route);
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, $"Unexpected access to {route}: {response.StatusCode}");
        }
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var authorised = await HttpClientAsync(scope, admin);
        var page = await authorised.GetAsync("/compliance/employees");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Employee compliance", await page.Content.ReadAsStringAsync());
        // The new policies consult current database permissions, not stale cookie claims.
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(admin, null, KcasPermissions.EmployeesView)).Succeeded);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(admin.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await userManager.RemoveFromRoleAsync(user!, KcasRoles.Administrator)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(admin, null, KcasPermissions.EmployeesView)).Succeeded);
    }

    [Fact]
    public async Task Starting_another_review_does_not_clear_an_earlier_confirmed_designation()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var reviewer = await LinkedReviewerAsync(scope, service, admin);
        var id = await service.SaveProfileAsync(Profile(), "Create", admin);
        await StartAsync(service, id, admin);
        await CheckAsync(service, id, "TFS", "ConfirmedDesignation", admin);
        var page = await service.LoadAsync(id, admin);
        await service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version, "Restricted", "Retain confirmed designation", "Restrict and follow reporting procedure; no ordinary clearance.", null, reviewer);
        page = await StartAsync(service, id, admin);
        Assert.Contains(page.Blockers, x => x.Contains("cannot be overridden"));
        var edit = CheckEdit("TFS", "NoMatch"); edit.SupersedesCheckId = page.Checks.First(c => c.Kind == "TFS").Id;
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordCheckAsync(page.CurrentReview!.Id, page.CurrentReview.Version, edit, admin));
    }

    [Fact]
    public async Task Profile_change_invalidates_draft_and_additional_selected_checks_are_required()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var edit = Profile(); edit.RequireAdditionalCheck = true;
        var id = await service.SaveProfileAsync(edit, "Create", admin);
        var page = await StartAsync(service, id, admin);
        Assert.Contains(page.Blockers, x => x.Contains("Additional"));
        edit.Id = id; edit.Version = page.Profile.Version; edit.Responsibilities += "; changed duties";
        await service.SaveProfileAsync(edit, "Material role change", admin);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordCheckAsync(page.CurrentReview!.Id, page.CurrentReview.Version, CheckEdit("Competence", "Satisfied"), admin));
        var newPage = await StartAsync(service, id, admin);
        Assert.NotEqual(page.CurrentReview!.Id, newPage.CurrentReview!.Id);
        Assert.Contains(newPage.History, x => x.Id == page.CurrentReview.Id && x.Status == "Superseded");
    }

    [Fact]
    public async Task Baselines_remain_pending_reuse_evidence_and_include_people_without_accounts()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var actor = await ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var profile = Profile();
        var baseline = new EmployeeBaseline { SourceReference = "Confirmed test roster", Employees = [new() { Profile = profile }] };
        Assert.Equal(1, await service.ImportBaselineAsync(baseline, actor));
        Assert.Equal(0, await service.ImportBaselineAsync(baseline, actor));
        var item = Assert.Single(await service.RegisterAsync(actor, profile.LegalName));
        Assert.Null(item.LatestReview);
        Assert.Equal(1, item.OpenTasks);
        var page = await service.LoadAsync(item.Profile.Id, actor);
        Assert.Empty(page.Accounts);
        Assert.Empty(page.Checks);
        Assert.Empty(page.Decisions);
        Assert.Equal("InitialReview", Assert.Single(page.Tasks).Kind);
        Assert.NotEqual("[]", page.Tasks[0].RecipientUserIdsJson);
    }

    [Fact]
    public async Task Personnel_service_denies_general_compliance_readers_and_rechecks_removed_permissions()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var reader = await ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RegisterAsync(reader));
        var reviewer = await ActorAsync(scope, KcasRoles.EmployeeReviewer);
        Assert.False((await service.PermissionsAsync(reviewer)).CanManage);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveProfileAsync(Profile(), "Not authorised", reviewer));
        var manager = await ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(manager.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await users.RemoveFromRoleAsync(user!, KcasRoles.ComplianceAdministrator)).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RegisterAsync(manager));
    }

    [Fact]
    public async Task Incomplete_and_failed_source_reviews_cannot_be_approved()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var reviewer = await LinkedReviewerAsync(scope, service, admin);
        var id = await service.SaveProfileAsync(Profile(), "Create test profile", admin);
        var page = await StartAsync(service, id, admin);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version,
            "Approved", "Incomplete approval attempt", "", 12, reviewer));
        await CheckAsync(service, id, "TFS", "SourceFailed", admin);
        page = await service.LoadAsync(id, admin);
        Assert.Contains(page.Blockers, x => x.Contains("Targeted financial sanctions"));
        Assert.Contains(page.Blockers, x => x.Contains("failed source"));
        await service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version, "FollowUp", "Source unavailable", "Repeat official source check; preserve limitations.", null, reviewer);
        page = await service.LoadAsync(id, admin);
        Assert.Equal("FollowUp", page.CurrentReview!.Status);
        Assert.Null(page.CurrentReview.CompletedAtUtc);
        Assert.Null(page.CurrentReview.NextReviewDate);
    }

    [Fact]
    public async Task Approval_freezes_evidence_and_dates_review_from_actual_completion()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var reviewer = await LinkedReviewerAsync(scope, service, admin);
        var id = await service.SaveProfileAsync(Profile(), "Create test profile", admin);
        await CompletePrerequisitesAsync(service, id, admin);
        var page = await service.LoadAsync(id, admin);
        Assert.Empty(page.Blockers);
        await service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version, "Approved", "Uninvolved evidence-based review", "", 24, reviewer);
        page = await service.LoadAsync(id, admin);
        Assert.Equal("Approved", page.CurrentReview!.Status);
        Assert.Equal(DateOnly.FromDateTime(page.CurrentReview.CompletedAtUtc!.Value.ToLocalTime()).AddMonths(24), page.CurrentReview.NextReviewDate);
        Assert.Contains("checks", Assert.Single(page.Decisions).EvidenceSnapshotJson);
        Assert.All(page.Tasks, task => Assert.Equal("Closed", task.Status));
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordCheckAsync(page.CurrentReview.Id, page.CurrentReview.Version,
            CheckEdit("Integrity", "Satisfied"), admin));
    }

    [Fact]
    public async Task Employee_and_preparer_cannot_self_approve_using_a_second_account()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var subject = await ActorAsync(scope, KcasRoles.EmployeeReviewer);
        var alternate = await ActorAsync(scope, KcasRoles.EmployeeReviewer);
        var id = await service.SaveProfileAsync(Profile(), "Subject", admin);
        var page = await service.LoadAsync(id, admin);
        await service.LinkAccountAsync(id, page.Profile.Version, subject.FindFirstValue(ClaimTypes.NameIdentifier)!, "Verified subject account", admin);
        page = await service.LoadAsync(id, admin);
        await service.LinkAccountAsync(id, page.Profile.Version, alternate.FindFirstValue(ClaimTypes.NameIdentifier)!, "Verified second subject account", admin);
        await CompletePrerequisitesAsync(service, id, admin);
        page = await service.LoadAsync(id, admin);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version,
            "Approved", "Self approval via another login", "", 12, alternate));
        var preparerId = await service.SaveProfileAsync(Profile(), "Preparer identity", admin);
        page = await service.LoadAsync(preparerId, admin);
        await service.LinkAccountAsync(preparerId, page.Profile.Version, admin.FindFirstValue(ClaimTypes.NameIdentifier)!, "Preparer primary account", admin);
        page = await service.LoadAsync(preparerId, admin);
        var preparerOther = await ActorAsync(scope, KcasRoles.EmployeeReviewer);
        await service.LinkAccountAsync(preparerId, page.Profile.Version, preparerOther.FindFirstValue(ClaimTypes.NameIdentifier)!, "Preparer second account", admin);
        var target = await service.SaveProfileAsync(Profile(), "Unrelated target", admin);
        await CompletePrerequisitesAsync(service, target, admin);
        page = await service.LoadAsync(target, admin);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version,
            "Approved", "Preparer second account", "", 12, preparerOther));
    }

    [Fact]
    public async Task Stale_review_and_changed_permissions_block_decision()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var reviewer = await LinkedReviewerAsync(scope, service, admin);
        var id = await service.SaveProfileAsync(Profile(), "Create", admin);
        var subject = await ActorAsync(scope, KcasRoles.ReadOnly);
        var page = await service.LoadAsync(id, admin);
        await service.LinkAccountAsync(id, page.Profile.Version, subject.FindFirstValue(ClaimTypes.NameIdentifier)!, "Identity link", admin);
        await CompletePrerequisitesAsync(service, id, admin);
        page = await service.LoadAsync(id, admin);
        var oldVersion = page.CurrentReview!.Version;
        await CheckAsync(service, id, "Integrity", "Satisfied", admin);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(page.CurrentReview.Id, oldVersion, "Approved", "Stale version", "", 12, reviewer));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(subject.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await users.AddToRoleAsync(user!, KcasRoles.Reports)).Succeeded);
        page = await service.LoadAsync(id, admin);
        Assert.Contains(page.Blockers, x => x.Contains("permissions changed"));
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version, "Approved", "Changed permissions", "", 12, reviewer));
    }

    [Fact]
    public async Task Manual_and_codex_performers_keep_actual_source_timestamp_and_saving_actor()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var id = await service.SaveProfileAsync(Profile(), "Create", admin);
        var page = await StartAsync(service, id, admin);
        var edit = CheckEdit("Competence", "Satisfied"); edit.PerformerType = "Codex";
        await service.RecordCheckAsync(page.CurrentReview!.Id, page.CurrentReview.Version, edit, admin);
        await CheckAsync(service, id, "Integrity", "Satisfied", admin);
        page = await service.LoadAsync(id, admin);
        Assert.Equal("Codex", Assert.Single(page.Checks, x => x.Kind == "Competence").Performer);
        var manual = Assert.Single(page.Checks, x => x.Kind == "Integrity");
        Assert.Equal(admin.Identity!.Name, manual.Performer);
        Assert.Equal(admin.FindFirstValue(ClaimTypes.NameIdentifier), manual.RecordedByUserId);
        var future = CheckEdit("TFS", "NoMatch"); future.PerformedAtLocal = DateTime.Now.AddDays(1);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordCheckAsync(page.CurrentReview!.Id, page.CurrentReview.Version, future, admin));
    }

    [Fact]
    public async Task Tfs_batch_snapshots_current_staff_without_logins_and_preserves_former_history()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var id = await service.SaveProfileAsync(Profile(), "Current without account", admin);
        var former = Profile(); former.EmploymentStatus = "Inactive";
        var formerId = await service.SaveProfileAsync(former, "Former test employee", admin);
        var version = "Official test update " + Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTfsBatchAsync(version, "https://fic.gov.za.example.test/", "Non-official source", admin));
        Assert.DoesNotContain(await service.BatchesAsync(admin), x => x.Batch.SourceVersion == version);
        var batchId = await service.CreateTfsBatchAsync(version, "https://www.fic.gov.za/", "Controlled test notice", admin);
        Assert.Equal(batchId, await service.CreateTfsBatchAsync(version, "https://www.fic.gov.za/", "Repeated notice", admin));
        var current = await service.LoadAsync(id, admin);
        Assert.Single(current.Tasks, x => x.EmployeeTfsBatchId == batchId);
        var departed = await service.LoadAsync(formerId, admin);
        Assert.DoesNotContain(departed.Tasks, x => x.EmployeeTfsBatchId == batchId);
        Assert.Contains(departed.Tasks, x => x.Kind == "AccessRemoval");
        current = await StartAsync(service, id, admin);
        var edit = CheckEdit("TFS", "SourceFailed"); edit.ListVersion = version; edit.EmployeeTfsBatchId = batchId;
        await service.RecordCheckAsync(current.CurrentReview!.Id, current.CurrentReview.Version, edit, admin);
        var batch = Assert.Single(await service.BatchesAsync(admin), x => x.Batch.Id == batchId);
        Assert.True(batch.Remaining > 0);
        Assert.Equal(1, batch.FollowUp);
    }

    [Fact]
    public async Task Confirmed_designation_cannot_be_overridden_or_replaced_as_no_match()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var reviewer = await LinkedReviewerAsync(scope, service, admin);
        var id = await service.SaveProfileAsync(Profile(), "Create", admin);
        await StartAsync(service, id, admin);
        await CheckAsync(service, id, "TFS", "ConfirmedDesignation", admin);
        var page = await service.LoadAsync(id, admin);
        Assert.Contains(page.Blockers, x => x.Contains("cannot be overridden"));
        await Assert.ThrowsAsync<ValidationException>(() => CheckAsync(service, id, "TFS", "NoMatch", admin));
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(page.CurrentReview!.Id, page.CurrentReview.Version, "Approved", "Override attempt", "", 12, reviewer));
    }

    [Fact]
    public async Task Due_tasks_are_idempotent_and_acknowledgement_is_not_completion()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var id = await service.SaveProfileAsync(Profile(), "Due fixture", admin);
        var page = await StartAsync(service, id, admin);
        var review = await db.EmployeeComplianceReviews.SingleAsync(x => x.Id == page.CurrentReview!.Id);
        review.Status = "Approved"; review.CompletedAtUtc = DateTime.UtcNow.AddYears(-1); review.NextReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        await db.SaveChangesAsync();
        await service.RefreshDueTasksAsync(admin); await service.RefreshDueTasksAsync(admin);
        page = await service.LoadAsync(id, admin);
        var task = Assert.Single(page.Tasks, x => x.Kind == "PeriodicReview");
        await service.AcknowledgeAsync(task.Id, admin);
        page = await service.LoadAsync(id, admin);
        task = Assert.Single(page.Tasks, x => x.Id == task.Id);
        Assert.Equal("Acknowledged", task.Status); Assert.NotNull(task.AcknowledgedAtUtc); Assert.Null(task.ClosedAtUtc);
    }

    [Fact]
    public async Task New_sensitive_grants_are_gated_without_revoking_existing_access()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var subject = await ActorAsync(scope, KcasRoles.ReadOnly);
        var id = await service.SaveProfileAsync(Profile(), "Existing employee", admin);
        var page = await service.LoadAsync(id, admin);
        var userId = subject.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await service.LinkAccountAsync(id, page.Profile.Version, userId, "Verified identity", admin);
        await Assert.ThrowsAsync<ValidationException>(() => service.ChangeAccountRoleAsync(userId, KcasRoles.Administrator, true, admin));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId);
        Assert.True(await users.IsInRoleAsync(user!, KcasRoles.ReadOnly));
        await service.ChangeAccountRoleAsync(userId, KcasRoles.EmployeeReviewer, true, admin);
        Assert.True(await users.IsInRoleAsync(user!, KcasRoles.EmployeeReviewer));
        Assert.False(await users.IsInRoleAsync(user!, KcasRoles.ComplianceApprover));
        await service.ChangeAccountRoleAsync(userId, KcasRoles.ReadOnly, false, admin);
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ChangeTracker.Clear();
        Assert.False(await users.IsInRoleAsync(user!, KcasRoles.ReadOnly));
    }

    [Fact]
    public async Task Evidence_mapping_is_portable_and_rejects_traversal_outside_roots_and_changed_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "kcas-employee-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var resolver = new EmployeeEvidenceFiles(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["EmployeeCompliance:EvidenceRoot"] = root }).Build());
            var mapped = resolver.Resolve(@"Z:\Kanaan Trust\Compliance\Training\sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(mapped)!);
            await File.WriteAllTextAsync(mapped, "Synthetic employee test evidence");
            Assert.Equal(Path.Combine(root, "Training", "sample.txt"), mapped);
            Assert.Equal(mapped, resolver.Resolve(@"E:\Userdata\Kanaan Trust\Compliance\Training\sample.txt"));
            Assert.Throws<ValidationException>(() => resolver.Resolve(@"..\secret.txt"));
            Assert.Throws<ValidationException>(() => resolver.Resolve(@"C:\Windows\secret.txt"));
            using var scope = factory.Services.CreateScope();
            var service = new EmployeeComplianceService(scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(), resolver);
            var admin = await ActorAsync(scope, KcasRoles.Administrator);
            var id = await service.SaveProfileAsync(Profile(), "Evidence fixture", admin);
            var page = await service.LoadAsync(id, admin);
            await service.LinkEvidenceAsync(id, page.Profile.Version, new() { Title = "Synthetic evidence", Category = "Training", EvidencePath = @"Training\sample.txt", SourceNote = "Test fixture, not actual employee evidence." }, admin);
            var document = Assert.Single(await service.EvidenceAsync(id, admin));
            var opened = await service.OpenDocumentAsync(document.Id, admin);
            Assert.NotNull(opened); await opened.Value.Stream.DisposeAsync();
            await File.WriteAllTextAsync(mapped, "Changed test evidence");
            await Assert.ThrowsAsync<ValidationException>(() => service.OpenDocumentAsync(document.Id, admin));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static EmployeeProfileEdit Profile() => new()
    {
        DisplayName = "Test employee " + Guid.NewGuid().ToString("N"), LegalName = "Legal fixture " + Guid.NewGuid().ToString("N"),
        IdentityReference = "Verified synthetic identity source", Responsibilities = "Synthetic support role", AuthorityLimits = "No decision authority inferred",
        SourceReference = "Test fixture", RoleExposure = "Standard", RiskRationale = "Support-only test role", SelectedChecks = "Competence, integrity and all-employee TFS",
        RequireTraining = false, ProposedReviewMonths = 24
    };

    private async Task<HttpClient> HttpClientAsync(IServiceScope scope, ClaimsPrincipal actor)
    {
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user!);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, IdentityConstants.ApplicationScheme));
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={ticket}");
        return client;
    }

    private static async Task<ClaimsPrincipal> ActorAsync(IServiceScope scope, string role)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = "employee-test-" + Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        var created = await users.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, email)], "Test"));
    }

    private static async Task<ClaimsPrincipal> LinkedReviewerAsync(IServiceScope scope, EmployeeComplianceService service, ClaimsPrincipal admin)
    {
        var actor = await ActorAsync(scope, KcasRoles.EmployeeReviewer);
        var id = await service.SaveProfileAsync(Profile(), "Reviewer's verified identity", admin);
        var page = await service.LoadAsync(id, admin);
        await service.LinkAccountAsync(id, page.Profile.Version, actor.FindFirstValue(ClaimTypes.NameIdentifier)!, "Synthetic authorised reviewer identity", admin);
        return actor;
    }

    private static async Task<EmployeeReviewPage> StartAsync(EmployeeComplianceService service, int id, ClaimsPrincipal actor)
    {
        var page = await service.LoadAsync(id, actor);
        await service.StartReviewAsync(id, page.Profile.Version, "Start test review", actor);
        return await service.LoadAsync(id, actor);
    }

    private static EmployeeCheckEdit CheckEdit(string kind, string outcome) => new()
    {
        Kind = kind, Outcome = outcome, SourceReference = "Synthetic source " + kind, Finding = "Synthetic evidenced finding: " + outcome,
        SourceUrl = kind == "TFS" ? "https://www.fic.gov.za/" : null, ListVersion = kind == "TFS" ? "Initial synthetic version" : null,
        IdentifierScope = "Synthetic legal identity and verified aliases", PerformedAtLocal = DateTime.Now
    };

    private static async Task CheckAsync(EmployeeComplianceService service, int id, string kind, string outcome, ClaimsPrincipal actor)
    {
        var page = await service.LoadAsync(id, actor);
        var edit = CheckEdit(kind, outcome);
        edit.SupersedesCheckId = page.Checks.FirstOrDefault(c => c.EmployeeComplianceReviewId == page.CurrentReview!.Id && c.Kind == kind)?.Id;
        if (kind == "TFS")
        {
            var batch = (await service.BatchesAsync(actor)).FirstOrDefault();
            if (batch is not null) { edit.ListVersion = batch.Batch.SourceVersion; edit.SourceUrl = batch.Batch.SourceUrl; }
        }
        await service.RecordCheckAsync(page.CurrentReview!.Id, page.CurrentReview.Version, edit, actor);
    }

    private static async Task CompletePrerequisitesAsync(EmployeeComplianceService service, int id, ClaimsPrincipal actor)
    {
        await StartAsync(service, id, actor);
        await CheckAsync(service, id, "Identity", "Satisfied", actor);
        await CheckAsync(service, id, "Competence", "Satisfied", actor);
        await CheckAsync(service, id, "Integrity", "Satisfied", actor);
        await CheckAsync(service, id, "TFS", "NoMatch", actor);
        foreach (var kind in new[] { "KCAS", "External" })
        {
            var page = await service.LoadAsync(id, actor);
            await service.RecordAccessAsync(page.CurrentReview!.Id, page.CurrentReview.Version, new EmployeeAccessEdit
            {
                Kind = kind, SystemAndScope = "Test scope", ApprovedScope = "Synthetic approved duties", ActualScope = "Actual test access or verified absence",
                ActionConfirmation = "Test owner confirmation, no actual external access changed", IsAligned = true, EvidenceReference = "Synthetic verification evidence"
            }, actor);
        }
    }
}
