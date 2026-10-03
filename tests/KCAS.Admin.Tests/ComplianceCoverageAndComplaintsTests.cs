using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ComplianceCoverageAndComplaintsTests(KcasWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly List<int> batches = [];
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // List-update gates affect the entire population; remove only this test's batches.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subjects = await db.ClientSanctionsSubjects.Where(x => batches.Contains(x.ClientSanctionsBatchId)).ToListAsync();
        var ids = subjects.Select(x => x.Id).ToList();
        var tasks = subjects.Where(x => x.ComplianceTaskId.HasValue).Select(x => x.ComplianceTaskId!.Value).ToList();
        await db.ClientSanctionsCoverageRecords.Where(x => ids.Contains(x.ClientSanctionsSubjectId)).ExecuteDeleteAsync();
        await db.ClientSanctionsSubjects.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ClientSanctionsBatches.Where(x => batches.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ComplianceTasks.Where(x => tasks.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Coverage_includes_hidden_historical_clients_and_related_parties_without_claiming_a_scan()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var client = await ClientAsync(scope, hidden: true);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ClientRelatedParties.Add(new() { ClientId = client.Id, DisplayName = "Synthetic trustee", IsActive = true });
        client.Relationships.Add(new() { Name = "Synthetic spouse", RelationshipType = "Spouse" });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ClientSanctionsCoverageService>();
        var batch = await BatchAsync(service, admin);
        var page = await service.LoadAsync(batch, admin);
        var own = page.Subjects.Where(x => x.Subject.ClientId == client.Id).ToList();
        Assert.Equal(3, own.Count);
        Assert.All(own, x => Assert.Equal("Outstanding", x.Status));
        Assert.All(own, x => Assert.NotNull(x.Subject.ComplianceTaskId));
        Assert.Contains(client.DisplayName, page.CodexBrief);
        var reader = await ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        Assert.DoesNotContain((await service.LoadAsync(batch, reader)).Subjects, x => x.Subject.ClientId == client.Id);
        Assert.DoesNotContain(client.DisplayName, (await service.LoadAsync(batch, reader)).CodexBrief);
        Assert.Null(await service.ReminderAsync(reader));
        var officer = await ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        Assert.NotNull(await service.ReminderAsync(officer));
    }

    [Fact]
    public async Task Coverage_reuses_verified_exact_list_evidence_and_detects_later_changes()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientSanctionsCoverageService>();
        var id = await BatchAsync(service, actor);
        var page = await service.LoadAsync(id, actor);
        var row = Assert.Single(page.Subjects.Where(x => x.Subject.ClientId == client.Id));
        var gateDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.ThrowsAsync<ValidationException>(() => ClientOnboardingService.RequireAcceptedAsync(gateDb, client.Id));
        var evidence = await EvidenceAsync(scope, row.Subject);
        await service.RecordEvidenceAsync(row.Subject.Id, evidence.Id, page.Batch.Version, "Actual synthetic check", actor);
        Assert.Equal("Clear", Assert.Single((await service.LoadAsync(id, actor)).Subjects.Where(x => x.Subject.Id == row.Subject.Id)).Status);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id));
        await ClientOnboardingService.RequireAcceptedAsync(db, client.Id);
        evidence.Notes += " changed finding"; await db.SaveChangesAsync();
        Assert.Equal("EvidenceChanged", Assert.Single((await service.LoadAsync(id, actor)).Subjects.Where(x => x.Subject.Id == row.Subject.Id)).Status);
        Assert.NotEmpty(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id));
    }

    [Fact]
    public async Task Old_wrong_source_failed_and_stale_coverage_cannot_be_saved()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientSanctionsCoverageService>();
        var id = await BatchAsync(service, actor);
        var page = await service.LoadAsync(id, actor);
        var row = Assert.Single(page.Subjects.Where(x => x.Subject.ClientId == client.Id));
        var e = await EvidenceAsync(scope, row.Subject);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        e.ScreeningSources = "https://unrelated.example.test/ old"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, e.Id, page.Batch.Version, "Wrong list", actor));
        e.ScreeningSources = page.Batch.SourceUrl + " " + page.Batch.SourceVersion + "0"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, e.Id, page.Batch.Version, "Different version sharing a prefix", actor));
        e.ScreeningSources = page.Batch.SourceUrl + "wrong-source " + page.Batch.SourceVersion; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, e.Id, page.Batch.Version, "Different URL sharing a prefix", actor));
        e.ScreeningSources = page.Batch.SourceUrl + " " + page.Batch.SourceVersion;
        e.EscalationRequired = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, e.Id, page.Batch.Version, "Failed source", actor));
        e.EscalationRequired = false; e.ScreeningReviewedAtUtc = page.Batch.SourcePublishedAtUtc.AddDays(-1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, e.Id, page.Batch.Version, "Old check", actor));
        await service.RefreshPopulationAsync(id, page.Batch.Version, "Refresh", actor);
        await Assert.ThrowsAsync<ValidationException>(() => service.ExcludeAsync(row.Subject.Id, page.Batch.Version, "RelationshipEnded", "Actual termination", "Documented end", actor));
        await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<ComplianceWorkService>().RequestClosureAsync(row.Subject.ComplianceTaskId!.Value, "Fake generic closure", "Clear", "Done", actor.Identity!.Name, "Bypass"));
    }

    [Fact]
    public async Task Confirmed_designations_and_new_possible_matches_cannot_be_overridden_by_an_old_clear()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientSanctionsCoverageService>();
        var id = await BatchAsync(service, actor);
        var page = await service.LoadAsync(id, actor);
        var row = Assert.Single(page.Subjects.Where(x => x.Subject.ClientId == client.Id));
        var clear = await EvidenceAsync(scope, row.Subject);
        await service.RecordEvidenceAsync(row.Subject.Id, clear.Id, page.Batch.Version, "No match", actor);
        var match = await EvidenceAsync(scope, row.Subject, ClientEvidenceScreeningOutcomes.PossibleMatch);
        page = await service.LoadAsync(id, actor);
        Assert.Equal("PossibleMatch", Assert.Single(page.Subjects.Where(x => x.Subject.Id == row.Subject.Id)).Status);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        match.ScreeningOutcome = ClientEvidenceScreeningOutcomes.ConfirmedMatch; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, clear.Id, page.Batch.Version, "Clear again", actor));
        await Assert.ThrowsAsync<ValidationException>(() => service.ExcludeAsync(row.Subject.Id, page.Batch.Version, "RelationshipEnded", "Termination", "Attempt exclusion", actor));
        Assert.Contains(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id), x => x.Contains("confirmed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Identity_changes_require_new_coverage_and_cannot_reuse_the_previous_snapshot()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientSanctionsCoverageService>();
        var id = await BatchAsync(service, actor);
        var before = await service.LoadAsync(id, actor);
        var row = Assert.Single(before.Subjects.Where(x => x.Subject.ClientId == client.Id));
        var evidence = await EvidenceAsync(scope, row.Subject);
        client.FullName = "Corrected identity particulars";
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        Assert.True((await service.LoadAsync(id, actor)).PopulationChanged);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(row.Subject.Id, evidence.Id, before.Batch.Version, "Old scope", actor));
        await service.RefreshPopulationAsync(id, before.Batch.Version, "Verified corrected identity", actor);
        var current = Assert.Single((await service.LoadAsync(id, actor)).Subjects.Where(x => x.Subject.ClientId == client.Id));
        Assert.NotEqual(row.Subject.ScopeHash, current.Subject.ScopeHash);
        Assert.Equal("Outstanding", current.Status);
        var refreshed = await service.LoadAsync(id, actor);
        Assert.Empty(current.AvailableEvidence);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEvidenceAsync(current.Subject.Id, evidence.Id, refreshed.Batch.Version, "Cannot reuse old identifier check", actor));
    }

    [Fact]
    public async Task Complaint_closure_requires_acknowledgement_decision_delivery_remedy_and_actual_payment()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var service = scope.ServiceProvider.GetRequiredService<ComplaintRegisterService>();
        var edit = Intake(actor); edit.Channel = "Telephone"; edit.NextUpdateDate = DateOnly.FromDateTime(DateTime.Today).AddDays(2);
        var id = await service.SaveAsync(edit, "Actual oral complaint", actor);
        var page = await service.LoadAsync(id, actor);
        Assert.Contains("acknowledgement", page.NextAction);
        Assert.Equal("Open", page.Case.Status);
        Assert.Equal(edit.NextUpdateDate, page.Case.NextUpdateDate);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(id, page.Case.Version, Decision(), actor));
        await EventAsync(service, id, "Acknowledgement", actor);
        await EventAsync(service, id, "Evidence", actor, codex: true);
        page = await service.LoadAsync(id, actor);
        Assert.Contains(page.Events, x => x.Kind == "Evidence" && x.PerformedBy == "Codex" && x.RecordedBy == actor.Identity!.Name);
        await service.DecideAsync(id, page.Case.Version, Decision(), actor);
        page = await service.LoadAsync(id, actor);
        await Assert.ThrowsAsync<ValidationException>(() => service.CloseAsync(id, page.Case.Version, "Premature", actor));
        var report = await service.RegisterAsync(actor, edit.ComplainantName);
        Assert.Equal(0m, report.Totals.CompensationPaid);
        await EventAsync(service, id, "ClientOutcomeDelivery", actor);
        await EventAsync(service, id, "RemedyCompleted", actor);
        page = await service.LoadAsync(id, actor);
        await Assert.ThrowsAsync<ValidationException>(() => service.CloseAsync(id, page.Case.Version, "Award not paid", actor));
        await EventAsync(service, id, "CompensationPaid", actor, amount: 125);
        page = await service.LoadAsync(id, actor);
        await service.CloseAsync(id, page.Case.Version, "Actual completed evidence", actor);
        page = await service.LoadAsync(id, actor);
        Assert.Equal("Closed", page.Case.Status);
        report = await service.RegisterAsync(actor, edit.ComplainantName);
        Assert.Equal(125m, report.Totals.CompensationPaid);
        await service.ReopenAsync(id, page.Case.Version, "New client facts", actor);
        page = await service.LoadAsync(id, actor);
        Assert.Null(page.Case.Decision);
        Assert.Contains(page.Events, x => x.Kind == "Decision");
        Assert.Contains(page.Events, x => x.Kind == "CompensationPaid" && x.Amount == 125);
    }

    [Fact]
    public async Task Implicated_accounts_and_stale_or_revoked_permissions_cannot_decide_or_save()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ActorAsync(scope, KcasRoles.Administrator);
        var other = await ActorAsync(scope, KcasRoles.Administrator);
        var service = scope.ServiceProvider.GetRequiredService<ComplaintRegisterService>();
        var edit = Intake(admin); edit.ImplicatedUserId = admin.FindFirstValue(ClaimTypes.NameIdentifier);
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(edit, "Self-handled", admin));
        edit.HandlerUserId = other.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var id = await service.SaveAsync(edit, "Uninvolved handler", admin);
        var before = await service.LoadAsync(id, admin);
        await EventAsync(service, id, "Acknowledgement", other);
        var page = await service.LoadAsync(id, admin);
        Assert.False(page.CanDecide);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(id, page.Case.Version, Decision(), admin));
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEventAsync(id, before.Case.Version, Activity("Progress"), other));
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = await manager.FindByIdAsync(other.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await manager.RemoveFromRoleAsync(account!, KcasRoles.Administrator)).Succeeded);
        Assert.True((await manager.RemoveFromRoleAsync(account!, KcasRoles.ComplianceAdministrator)).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordEventAsync(id, page.Case.Version, Activity("Progress"), other));
    }

    [Fact]
    public async Task Hidden_clients_are_omitted_from_complaint_register_exports_and_direct_case_access()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var reader = await ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        var client = await ClientAsync(scope, hidden: true);
        var service = scope.ServiceProvider.GetRequiredService<ComplaintRegisterService>();
        var edit = Intake(actor); edit.ClientId = client.Id;
        var id = await service.SaveAsync(edit, "Restricted client", actor);
        Assert.Empty((await service.RegisterAsync(reader, edit.ComplainantName)).Rows);
        Assert.DoesNotContain(edit.ComplainantName, Encoding.UTF8.GetString(await service.ExportCsvAsync(reader)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadAsync(id, reader));
        var text = Encoding.UTF8.GetString(await service.ExportCsvAsync(actor, edit.ComplainantName));
        Assert.Contains("'=" , text);
    }

    [Fact]
    public async Task Spreadsheet_import_preserves_originals_is_idempotent_and_does_not_invent_closure()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var service = scope.ServiceProvider.GetRequiredService<ComplaintRegisterService>();
        var name = "Legacy " + Guid.NewGuid().ToString("N");
        var source = "Synthetic historical register " + name;
        var bytes = Workbook(name, "Original complaint and conclusion");
        Assert.Single(await service.PreviewLegacyAsync(bytes, source, actor));
        Assert.Equal(1, await service.ImportLegacyAsync(bytes, source, "Preserve original", actor));
        Assert.Equal(0, await service.ImportLegacyAsync(bytes, source, "Repeated import", actor));
        var entry = Assert.Single((await service.RegisterAsync(actor, name)).Rows);
        Assert.Equal("NeedsReview", entry.Case.Status);
        Assert.False(entry.Case.IsReportable);
        Assert.Null(entry.Case.Decision);
        Assert.Contains("Original complaint and conclusion", entry.Case.LegacySourceJson);
        Assert.Equal(1, (await service.RegisterAsync(actor, name)).Totals.Unclassified);
        var changed = Workbook(name, "Changed original");
        Assert.True(Assert.Single(await service.PreviewLegacyAsync(changed, source, actor)).Changed);
        await Assert.ThrowsAsync<ValidationException>(() => service.ImportLegacyAsync(changed, source, "Do not overwrite", actor));
        var reader = await ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        Assert.Empty((await service.RegisterAsync(reader, name)).Rows);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewLegacyAsync(bytes, source, reader));
        Assert.Empty(await service.PreviewLegacyAsync(Workbook(null, null), source, actor));
    }

    [Fact]
    public async Task Complaint_activity_rejects_future_dates_fake_codex_communications_and_missing_recourse()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var service = scope.ServiceProvider.GetRequiredService<ComplaintRegisterService>();
        var id = await service.SaveAsync(Intake(actor), "Actual intake", actor);
        var page = await service.LoadAsync(id, actor);
        var future = Activity("Acknowledgement"); future.OccurredAtUtc = DateTime.UtcNow.AddDays(1);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEventAsync(id, page.Case.Version, future, actor));
        var fake = Activity("Acknowledgement"); fake.CodexPrepared = true;
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEventAsync(id, page.Case.Version, fake, actor));
        await EventAsync(service, id, "Acknowledgement", actor);
        page = await service.LoadAsync(id, actor);
        await service.DecideAsync(id, page.Case.Version, Decision(), actor);
        page = await service.LoadAsync(id, actor);
        var delivery = Activity("ClientOutcomeDelivery"); delivery.RecourseDetails = null;
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordEventAsync(id, page.Case.Version, delivery, actor));
    }

    private async Task<int> BatchAsync(ClientSanctionsCoverageService service, ClaimsPrincipal actor)
    {
        var edit = new SanctionsBatchEdit { SourceVersion = "Synthetic-" + Guid.NewGuid().ToString("N"), SourceUrl = "https://tfs.fic.gov.za/",
            SourcePublishedAtUtc = DateTime.UtcNow.AddMinutes(-5), Reason = "Synthetic test only; no actual official update" };
        var id = await service.CreateAsync(edit, actor); batches.Add(id);
        Assert.Equal(id, await service.CreateAsync(edit, actor)); return id;
    }
    private static async Task<Client> ClientAsync(IServiceScope scope, bool hidden = false)
    {
        var name = "Synthetic coverage " + Guid.NewGuid().ToString("N");
        var client = new Client { DisplayName = name, SurnameOrEntityName = name, KanaanId = "TEST", ExcludeFromComplianceLists = hidden, IsActive = false, LifecycleStatus = ClientLifecycleStatuses.Historical };
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.Clients.Add(client); await db.SaveChangesAsync(); return client;
    }
    private static async Task<ClientEvidenceItem> EvidenceAsync(IServiceScope scope, ClientSanctionsSubject subject, string outcome = ClientEvidenceScreeningOutcomes.NoMatch)
    {
        var evidence = new ClientEvidenceItem { ClientId = subject.ClientId, EvidenceType = "SanctionsTfs", Title = "Synthetic actual check",
            Status = ClientEvidenceStatuses.Verified, OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed, SelectionStatus = ClientEvidenceSelectionStatuses.Current,
            ClientRelatedPartyId = subject.ClientRelatedPartyId, ScreeningSubjectType = subject.SubjectType, ScreeningSubjectName = subject.SubjectName,
            ScreeningSources = subject.Batch.SourceUrl + " " + subject.Batch.SourceVersion, ScreeningReviewedAtUtc = DateTime.UtcNow,
            ScreeningPerformedBy = "Codex", Reviewer = "test@example.test", Notes = "Synthetic identifiers checked; source accessed; no actual screening claimed", ScreeningOutcome = outcome };
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.ClientEvidenceItems.Add(evidence); await db.SaveChangesAsync(); return evidence;
    }
    private static ComplaintEdit Intake(ClaimsPrincipal actor) => new() { ComplainantName = "=Synthetic " + Guid.NewGuid().ToString("N"),
        ContactDetails = "test@example.test", Allegation = "Synthetic complaint", ReceivedAtUtc = DateTime.UtcNow.AddHours(-1), HandlerUserId = actor.FindFirstValue(ClaimTypes.NameIdentifier)!, RequestedOutcome = "Correction" };
    private static ComplaintDecisionEdit Decision() => new() { Decision = "Upheld", Reasons = "Supported synthetic finding", Remedy = "Correct record and pay compensation", CompensationAwarded = 125, EvidenceReference = "Synthetic decision reference" };
    private static ComplaintEventEdit Activity(string kind) => new() { Kind = kind, Details = "Actual synthetic event and recipient", EvidenceReference = "Synthetic evidence reference", OccurredAtUtc = DateTime.UtcNow, RecourseDetails = kind == "ClientOutcomeDelivery" ? "Actual Ombud contact and recourse information supplied" : null };
    private static async Task EventAsync(ComplaintRegisterService service, int id, string kind, ClaimsPrincipal actor, decimal? amount = null, bool codex = false)
    {
        var page = await service.LoadAsync(id, actor); var edit = Activity(kind); edit.Amount = amount; edit.CodexPrepared = codex;
        await service.RecordEventAsync(id, page.Case.Version, edit, actor);
    }
    private static async Task<ClaimsPrincipal> ActorAsync(IServiceScope scope, string role)
    {
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = "coverage-test-" + Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        Assert.True((await manager.CreateAsync(user)).Succeeded); Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        if (role == KcasRoles.Administrator) Assert.True((await manager.AddToRoleAsync(user, KcasRoles.ComplianceAdministrator)).Succeeded);
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, email)], "Test"));
    }
    private static byte[] Workbook(string? name, string? details)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        using var result = new MemoryStream();
        using (var archive = new ZipArchive(result, ZipArchiveMode.Create, true))
        {
            Write("xl/workbook.xml", new(new XElement(ns + "workbook", new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", "Complaints"), new XAttribute(rel + "id", "rId1"))))));
            Write("xl/_rels/workbook.xml.rels", new(new XElement(pkg + "Relationships", new XElement(pkg + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Target", "worksheets/sheet1.xml")))));
            var rows = new XElement(ns + "sheetData", Row(1, ["Date", "Client", "Nature", "TCF Outcome", "Details"]));
            if (name is not null) rows.Add(Row(2, ["2025-06-01", name, "Complaint", "6", details ?? ""]));
            Write("xl/worksheets/sheet1.xml", new(new XElement(ns + "worksheet", rows)));
            void Write(string path, XDocument xml) { using var stream = archive.CreateEntry(path).Open(); xml.Save(stream); }
            XElement Row(int number, string[] values) => new(ns + "row", new XAttribute("r", number), values.Select((value, index) => new XElement(ns + "c", new XAttribute("r", ((char)('A' + index)).ToString() + number), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)))));
        }
        return result.ToArray();
    }
}
