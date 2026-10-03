using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using static KCAS.Admin.Data.ComplianceWorkflowAccess;

namespace KCAS.Admin.Data;

public sealed partial class ComplaintRegisterService(IDbContextFactory<ApplicationDbContext> factory)
{
    public static readonly string[] Categories = ["DesignOrFees", "Information", "Advice", "Performance", "Service", "AccessSwitchesRedemptions", "ComplaintHandling", "InsuranceClaim", "Other"];
    public static readonly string[] Channels = ["Email", "Telephone", "InPerson", "Letter", "Other"];
    public static readonly string[] EventKinds = ["Acknowledgement", "Triage", "Evidence", "Progress", "Referral", "Escalation", "ExternalComplianceNotification", "OmbudReferral", "OmbudOutcome", "ClientOutcomeDelivery", "ClientReaction", "RootCause", "RemedyCompleted", "CompensationPaid", "GoodwillPaid"];
    public static readonly string[] Decisions = ["Upheld", "PartiallyUpheld", "Rejected", "Resolved", "Withdrawn"];
    public async Task<ComplianceControlReminder?> ReminderAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        if (!await ReceivesReviewsAsync(db, actor.Id)) return null;
        var admin = await IsAdminAsync(db, actor.Id);
        var count = await db.ComplaintCases.CountAsync(x => x.Status != "Closed" &&
            (admin || ((x.Client == null || !x.Client.ExcludeFromComplianceLists) && (x.LegacyKey == null || x.ClientId != null))));
        return count == 0 ? null : new("Complaints requiring follow-up", count, "/compliance/complaints");
    }

    public async Task<ComplaintRegisterPage> RegisterAsync(ClaimsPrincipal principal, string? search = null, string? status = null, DateOnly? from = null, DateOnly? to = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        var admin = await IsAdminAsync(db, actor.Id);
        var query = db.ComplaintCases.AsNoTracking().Include(x => x.Client)
            .Where(x => admin || ((x.Client == null || !x.Client.ExcludeFromComplianceLists) && (x.LegacyKey == null || x.ClientId != null)));
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.ComplainantName.Contains(term) || x.Allegation.Contains(term) || (x.Client != null && (x.Client.DisplayName.Contains(term) || x.Client.KanaanId!.Contains(term)))); }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (from > to) throw new ValidationException("The received-date range is reversed.");
        if (from is { } start) { var utc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime(); query = query.Where(x => x.ReceivedAtUtc >= utc); }
        if (to is { } end) { var utc = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime(); query = query.Where(x => x.ReceivedAtUtc < utc); }
        var cases = await query.OrderBy(x => x.Status == "Closed").ThenBy(x => x.NextUpdateDate).ThenByDescending(x => x.ReceivedAtUtc).ToListAsync();
        var ids = cases.Select(x => x.Id).ToList();
        var events = await db.ComplaintEvents.AsNoTracking().Where(x => ids.Contains(x.ComplaintCaseId)).ToListAsync();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email ?? x.UserName ?? x.Id);
        var classified = cases.Where(x => x.IsReportable && x.Status != "NeedsReview").Select(x => x.Id).ToHashSet();
        var reportEvents = events.Where(x => classified.Contains(x.ComplaintCaseId)).ToList();
        return new(cases.Select(x => new ComplaintRow(x, users.GetValueOrDefault(x.HandlerUserId, x.HandlerUserId), NextAction(x, events.Where(e => e.ComplaintCaseId == x.Id).ToList()),
                events.Where(e => e.ComplaintCaseId == x.Id && e.Kind == "CompensationPaid").Sum(e => e.Amount ?? 0),
                events.Where(e => e.ComplaintCaseId == x.Id && e.Kind == "GoodwillPaid").Sum(e => e.Amount ?? 0),
                events.Any(e => e.ComplaintCaseId == x.Id && e.Kind == "OmbudReferral"))).ToList(),
            new(classified.Count, cases.Count(x => classified.Contains(x.Id) && x.Decision is "Upheld" or "PartiallyUpheld"),
                cases.Count(x => classified.Contains(x.Id) && x.Decision == "Rejected"), reportEvents.Where(x => x.Kind == "Escalation").Select(x => x.ComplaintCaseId).Distinct().Count(),
                reportEvents.Where(x => x.Kind == "OmbudReferral").Select(x => x.ComplaintCaseId).Distinct().Count(),
                reportEvents.Where(x => x.Kind == "CompensationPaid").Select(x => x.ComplaintCaseId).Distinct().Count(), reportEvents.Where(x => x.Kind == "CompensationPaid").Sum(x => x.Amount ?? 0),
                reportEvents.Where(x => x.Kind == "GoodwillPaid").Select(x => x.ComplaintCaseId).Distinct().Count(), reportEvents.Where(x => x.Kind == "GoodwillPaid").Sum(x => x.Amount ?? 0),
                cases.Count(x => classified.Contains(x.Id) && x.Status != "Closed"), cases.Count(x => x.Status == "NeedsReview")),
            await PermissionAsync(db, actor.Id, KcasPermissions.ComplianceManage), admin);
    }

    public async Task<ComplaintPage> LoadAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        var item = await CaseAsync(db, id, actor);
        var events = await db.ComplaintEvents.AsNoTracking().Where(x => x.ComplaintCaseId == id).OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).ToListAsync();
        return new(item, events, NextAction(item, events), await PermissionAsync(db, actor.Id, KcasPermissions.ComplianceManage),
            await CanDecideAsync(db, actor) && actor.Id != item.ImplicatedUserId,
            $"Complaint C-{id:000000}; client KCAS ID: {item.ClientId}; complainant: {item.ComplainantName}.\n" +
            $"Concern: {item.Allegation}\nRequested result: {item.RequestedOutcome}\n" +
            "Read actual complaint correspondence, mandate/advice/instruction records and relevant policy. Link dated evidence, prepare a factual chronology and identify supported findings, client impact and unresolved facts. " +
            "Do not fabricate communications, client agreement, payments, legal/Ombud conclusions or management decisions. Preserve originals and attribution. " +
            "An uninvolved authorised person records the actual decision; Codex prepares findings only.\n" +
            $"Next action: {NextAction(item, events)}");
    }

    public async Task<ComplaintOptions> OptionsAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        var admin = await IsAdminAsync(db, actor.Id);
        return new(await db.Clients.AsNoTracking().Where(x => admin || !x.ExcludeFromComplianceLists).OrderBy(x => x.DisplayName)
            .Select(x => new ComplianceWorkOption(x.Id, x.DisplayName + " (" + x.KanaanId + ")")).ToListAsync(),
            await db.Users.AsNoTracking().Where(x => x.IsApproved).OrderBy(x => x.Email).Select(x => new ComplaintUserOption(x.Id, x.Email ?? x.UserName ?? x.Id)).ToListAsync());
    }

    public async Task<int> SaveAsync(ComplaintEdit edit, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var item = edit.Id is null ? new ComplaintCase() : await CaseAsync(db, edit.Id.Value, actor);
            if (edit.Id is not null) { Expect(item.Version, edit.Version); if (item.Status is "Closed" or "Decided") throw new ValidationException("Reopen the case before changing its intake or decision basis."); }
            var eventDates = edit.Id is null ? new List<DateTime>() : await db.ComplaintEvents.Where(x => x.ComplaintCaseId == item.Id && x.Kind != "SourcePreserved").Select(x => x.OccurredAtUtc).ToListAsync();
            if (eventDates.Any(x => x < ActualTime(edit.ReceivedAtUtc))) throw new ValidationException("Receipt cannot be moved after recorded case activity.");
            if (edit.ClientId is int clientId) await VisibleAsync(db, await db.Clients.SingleAsync(x => x.Id == clientId), actor.Id);
            if (!Channels.Contains(edit.Channel) || !Categories.Contains(edit.Category)) throw new ValidationException("Choose a supported channel and complaint category.");
            var handler = await db.Users.SingleOrDefaultAsync(x => x.Id == edit.HandlerUserId && x.IsApproved);
            if (handler is null) throw new ValidationException("Assign an approved account to handle the complaint.");
            if (handler.Id == edit.ImplicatedUserId) throw new ValidationException("The implicated person cannot handle their own complaint.");
            if (edit.ImplicatedUserId is not null && !await db.Users.AnyAsync(x => x.Id == edit.ImplicatedUserId)) throw new ValidationException("The implicated account was not found.");
            item.ClientId = edit.ClientId; item.ComplainantName = Required(edit.ComplainantName, "Complainant", 240);
            item.ContactDetails = Required(edit.ContactDetails, "Contact details or documented contact limitation");
            item.ReceivedAtUtc = ActualTime(edit.ReceivedAtUtc); item.Channel = edit.Channel;
            item.Allegation = Required(edit.Allegation, "Concern in the complainant's words"); item.RequestedOutcome = edit.RequestedOutcome.Trim();
            item.Category = edit.Category; item.SecondaryThemes = edit.SecondaryThemes.Trim(); item.HandlerUserId = handler.Id;
            item.ImplicatedUserId = edit.ImplicatedUserId; item.IsReportable = edit.IsReportable; item.NextUpdateDate = edit.NextUpdateDate;
            if (item.Status == "NeedsReview") item.Status = "Open";
            Touch(item, actor);
            if (edit.Id is null) db.ComplaintCases.Add(item);
            await db.SaveChangesAsync(); await EnsureTaskAsync(db, item, actor);
            Audit(db, nameof(ComplaintCase), item.Id, edit.Id is null ? "IntakeRecorded" : "IntakeUpdated", actor, reason,
                new { item.Version, item.ClientId, item.ComplainantName, item.Category, item.HandlerUserId, item.ImplicatedUserId, item.IsReportable });
            return item.Id;
        });

    public async Task RecordEventAsync(int id, string version, ComplaintEventEdit edit, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var item = await CaseAsync(db, id, actor); Expect(item.Version, version);
            if (item.Status == "Closed") throw new ValidationException("Reopen the case before recording additional activity.");
            if (!EventKinds.Contains(edit.Kind)) throw new ValidationException("Choose a supported case event.");
            var time = ActualTime(edit.OccurredAtUtc);
            if (item.ReceivedAtUtc.HasValue && time < item.ReceivedAtUtc) throw new ValidationException("Case activity cannot precede receipt of the complaint.");
            var afterDecision = edit.Kind is "ClientOutcomeDelivery" or "RemedyCompleted" or "CompensationPaid" or "GoodwillPaid";
            if (afterDecision && (item.DecidedAtUtc is null || time < item.DecidedAtUtc)) throw new ValidationException("Record the actual decision before its delivery, remedy or payment.");
            if (edit.Kind is "CompensationPaid" or "GoodwillPaid")
            {
                if (edit.Amount is null or <= 0) throw new ValidationException("Record the actual positive amount paid and supporting payment reference.");
            }
            else if (edit.Amount.HasValue) throw new ValidationException("An amount belongs only to an actual payment event.");
            var record = new ComplaintEvent { ComplaintCaseId = id, Kind = edit.Kind, OccurredAtUtc = time,
                Details = Required(edit.Details, "Actual event details"), EvidenceReference = Required(edit.EvidenceReference, "Correspondence, case note or payment evidence reference"),
                Amount = edit.Amount, RecordedBy = Label(actor) };
            record.PerformedBy = edit.CodexPrepared && edit.Kind == "Evidence" ? "Codex" : Label(actor);
            if (edit.CodexPrepared && edit.Kind != "Evidence") throw new ValidationException("Codex may prepare findings; it cannot claim actual communications, decisions or payments.");
            if (edit.Kind == "ClientOutcomeDelivery") record.RecourseDetails = Required(edit.RecourseDetails, "Further-recourse information actually communicated, or supported reason none applies");
            db.ComplaintEvents.Add(record); item.NextUpdateDate = edit.NextUpdateDate;
            if (item.Status == "Open") item.Status = "Investigating";
            Touch(item, actor); await EnsureTaskAsync(db, item, actor);
            Audit(db, nameof(ComplaintCase), id, "CaseEventRecorded", actor, record.Details, new { record.Kind, record.OccurredAtUtc, record.EvidenceReference, record.Amount });
            return true;
        });

    public async Task DecideAsync(int id, string version, ComplaintDecisionEdit edit, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        if (!await CanDecideAsync(db, actor)) throw new UnauthorizedAccessException("An authorised compliance approver or current Key Individual is required.");
        var item = await CaseAsync(db, id, actor); Expect(item.Version, version);
        if (item.Status is "Closed" or "Decided" or "NeedsReview") throw new ValidationException("Complete intake, or reopen, before recording a decision.");
        if (actor.Id == item.ImplicatedUserId) throw new ValidationException("An implicated person cannot decide their own complaint.");
        if (!await db.ComplaintEvents.AnyAsync(x => x.ComplaintCaseId == id && x.Kind == "Acknowledgement"))
            throw new ValidationException("Record the actual acknowledgement and triage before the decision.");
        if (!Decisions.Contains(edit.Decision) || edit.CompensationAwarded < 0 || edit.GoodwillAwarded < 0) throw new ValidationException("Use a supported outcome and non-negative remedy amounts.");
        item.Decision = edit.Decision; item.DecisionReasons = Required(edit.Reasons, "Decision reasons addressing the material concerns");
        item.Remedy = Required(edit.Remedy, "Remedy/follow-up, or reason no remedy applies");
        item.CompensationAwarded = edit.CompensationAwarded; item.GoodwillAwarded = edit.GoodwillAwarded;
        item.DecidedAtUtc = DateTime.UtcNow; item.DecidedBy = Label(actor); item.Status = "Decided"; Touch(item, actor);
        db.ComplaintEvents.Add(new() { ComplaintCaseId = id, Kind = "Decision", OccurredAtUtc = item.DecidedAtUtc.Value,
            Details = edit.Decision + ": " + item.DecisionReasons + "\nRemedy: " + item.Remedy,
            EvidenceReference = Required(edit.EvidenceReference, "Decision evidence/reference"), RecordedBy = Label(actor) });
        Audit(db, nameof(ComplaintCase), id, "DecisionRecorded", actor, item.DecisionReasons, new { item.Decision, item.Remedy, item.CompensationAwarded, item.GoodwillAwarded, item.DecidedBy });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task CloseAsync(int id, string version, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var item = await CaseAsync(db, id, actor); Expect(item.Version, version);
            if (item.Status != "Decided") throw new ValidationException("Record an authorised decision before closing the complaint.");
            if (actor.Id == item.ImplicatedUserId) throw new ValidationException("An implicated person cannot close their own complaint.");
            var events = await db.ComplaintEvents.Where(x => x.ComplaintCaseId == id).ToListAsync();
            if (!events.Any(x => x.Kind == "ClientOutcomeDelivery" && x.OccurredAtUtc >= item.DecidedAtUtc && !string.IsNullOrWhiteSpace(x.RecourseDetails))) throw new ValidationException("Record actual delivery of the outcome and applicable further-recourse information.");
            if (!events.Any(x => x.Kind == "RemedyCompleted" && x.OccurredAtUtc >= item.DecidedAtUtc)) throw new ValidationException("Record the remedy/follow-up completed, or evidenced reason no further action applies.");
            if (events.Where(x => x.Kind == "CompensationPaid").Sum(x => x.Amount ?? 0) < item.CompensationAwarded || events.Where(x => x.Kind == "GoodwillPaid").Sum(x => x.Amount ?? 0) < item.GoodwillAwarded)
                throw new ValidationException("Promised compensation/goodwill remains unpaid; an award is not an actual payment.");
            item.Status = "Closed"; item.NextUpdateDate = null; Touch(item, actor);
            db.ComplaintEvents.Add(new() { ComplaintCaseId = id, Kind = "Closure", OccurredAtUtc = DateTime.UtcNow, Details = Required(reason, "Closure reasons/evidence"), RecordedBy = Label(actor) });
            await EnsureTaskAsync(db, item, actor); Audit(db, nameof(ComplaintCase), id, "Closed", actor, reason, new { item.Version, item.Decision }); return true;
        });

    public async Task ReopenAsync(int id, string version, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var item = await CaseAsync(db, id, actor); Expect(item.Version, version);
            if (item.Status is not ("Closed" or "Decided")) throw new ValidationException("Only a decided or closed case needs reopening.");
            item.Status = "Investigating"; item.Decision = null; item.DecisionReasons = null; item.Remedy = null;
            item.DecidedAtUtc = null; item.DecidedBy = null; item.CompensationAwarded = 0; item.GoodwillAwarded = 0;
            Touch(item, actor); await EnsureTaskAsync(db, item, actor);
            db.ComplaintEvents.Add(new() { ComplaintCaseId = id, Kind = "Reopened", OccurredAtUtc = DateTime.UtcNow, Details = Required(reason, "Reason/new evidence"), RecordedBy = Label(actor) });
            Audit(db, nameof(ComplaintCase), id, "Reopened", actor, reason, new { item.Version }); return true;
        });

    public async Task<byte[]> ExportCsvAsync(ClaimsPrincipal principal, string? search = null, string? status = null, DateOnly? from = null, DateOnly? to = null)
    {
        var page = await RegisterAsync(principal, search, status, from, to);
        var result = new StringBuilder("Reference,Client,Kanaan ID,Complainant,Received UTC,Channel,Category,Secondary themes,Reportable,Status,Decision,Reasons,Remedy,Compensation awarded,Compensation paid,Goodwill awarded,Goodwill paid,Ombud referred,Handler,Next update,Allegation\r\n");
        foreach (var row in page.Rows)
            result.AppendLine(string.Join(',', new[] { $"C-{row.Case.Id:000000}", row.Case.Client?.DisplayName ?? "", row.Case.Client?.KanaanId ?? "", row.Case.ComplainantName,
                row.Case.ReceivedAtUtc?.ToString("O") ?? "", row.Case.Channel, row.Case.Category, row.Case.SecondaryThemes, row.Case.IsReportable.ToString(), row.Case.Status,
                row.Case.Decision ?? "", row.Case.DecisionReasons ?? "", row.Case.Remedy ?? "", Money(row.Case.CompensationAwarded), Money(row.CompensationPaid),
                Money(row.Case.GoodwillAwarded), Money(row.GoodwillPaid), row.OmbudReferred.ToString(), row.Handler, row.Case.NextUpdateDate?.ToString("yyyy-MM-dd") ?? "", row.Case.Allegation }.Select(Csv)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.ToString())).ToArray();
    }
    private static string Money(decimal value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    private static string Csv(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static async Task<ComplaintCase> CaseAsync(ApplicationDbContext db, int id, ApplicationUser actor)
    {
        var item = await db.ComplaintCases.Include(x => x.Client).SingleAsync(x => x.Id == id);
        if (item.Client is not null) await VisibleAsync(db, item.Client, actor.Id);
        if (item.LegacyKey != null && item.ClientId == null && !await IsAdminAsync(db, actor.Id)) throw new UnauthorizedAccessException("Unmatched historical entries require administrator review before general visibility.");
        return item;
    }
    private static async Task<bool> CanDecideAsync(ApplicationDbContext db, ApplicationUser actor)
    {
        if (await PermissionAsync(db, actor.Id, KcasPermissions.ComplianceApprove)) return true;
        if (!await PermissionAsync(db, actor.Id, KcasPermissions.ClientsManage)) return false;
        var today = DateOnly.FromDateTime(DateTime.Today);
        return (await db.GovernanceRoleAssignments.Where(x => x.IsActive && (x.StartDate == null || x.StartDate <= today) && (x.EndDate == null || x.EndDate >= today)).ToListAsync())
            .Any(x => ComplianceApprovalRules.IsKeyIndividualRole(x.RoleType) && !string.IsNullOrWhiteSpace(x.Email) && string.Equals(x.Email, actor.Email, StringComparison.OrdinalIgnoreCase));
    }
    private static string NextAction(ComplaintCase item, IReadOnlyList<ComplaintEvent> events) => item.Status == "Closed" ? "View closed case" :
        item.Status == "NeedsReview" ? "Review preserved historical entry; identify missing facts" :
        !events.Any(x => x.Kind == "Acknowledgement") ? "Record acknowledgement and immediate client-harm triage" :
        item.Status != "Decided" ? "Prepare findings and obtain an uninvolved authorised decision" :
        !events.Any(x => x.Kind == "ClientOutcomeDelivery" && x.OccurredAtUtc >= item.DecidedAtUtc) ? "Deliver the outcome and further-recourse information" :
        "Complete remedy/payment and follow-up evidence before closure";
    private static void Touch(ComplaintCase item, ApplicationUser actor) { item.Version = NewVersion(); item.UpdatedAtUtc = DateTime.UtcNow; item.UpdatedBy = Label(actor); }
    private static async Task EnsureTaskAsync(ApplicationDbContext db, ComplaintCase item, ApplicationUser actor)
    {
        var task = item.Task ?? (item.ComplianceTaskId.HasValue ? await db.ComplianceTasks.SingleAsync(x => x.Id == item.ComplianceTaskId) : null);
        if (task is null) { task = new ComplianceTask { TaskType = ComplianceTaskTypes.Complaint, LinkedEntityType = nameof(ComplaintCase), LinkedEntityId = item.Id }; item.Task = task; }
        task.Title = item.LegacyKey != null && item.ClientId == null ? $"Historical complaint C-{item.Id:000000}: administrator matching required" : $"Complaint C-{item.Id:000000}: {item.ComplainantName[..Math.Min(item.ComplainantName.Length, 210)]}"; task.ClientId = item.ClientId;
        task.Owner = ComplianceWorkService.ComplianceReviewAudience; task.DueDate = item.NextUpdateDate; task.Priority = "High";
        task.Description = $"Handler account: {item.HandlerUserId}. View the controlled complaint case for the next action and actual evidence.";
        task.Status = item.Status == "Closed" ? ComplianceStatuses.Closed : ComplianceWorkStatuses.Open;
        task.ClosedAtUtc = item.Status == "Closed" ? DateTime.UtcNow : null; task.UpdatedBy = Label(actor);
    }
    private async Task<T> WriteAsync<T>(ClaimsPrincipal principal, Func<ApplicationDbContext, ApplicationUser, Task<T>> action)
    {
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceManage); var result = await action(db, actor);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ValidationException("The complaint changed. Reload before saving."); }
        return result;
    }
}

public sealed class ComplaintEdit
{
    public int? Id { get; set; }
    public string? Version { get; set; }
    public int? ClientId { get; set; }
    public string ComplainantName { get; set; } = "";
    public string ContactDetails { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public string Channel { get; set; } = "Email";
    public string Allegation { get; set; } = "";
    public string RequestedOutcome { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string SecondaryThemes { get; set; } = "";
    public string HandlerUserId { get; set; } = "";
    public string? ImplicatedUserId { get; set; }
    public bool IsReportable { get; set; } = true;
    public DateOnly? NextUpdateDate { get; set; }
}
public sealed class ComplaintEventEdit
{
    public string Kind { get; set; } = "Evidence";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Details { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public bool CodexPrepared { get; set; }
    public string? RecourseDetails { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? NextUpdateDate { get; set; }
}
public sealed class ComplaintDecisionEdit
{
    public string Decision { get; set; } = "Upheld";
    public string Reasons { get; set; } = "";
    public string Remedy { get; set; } = "";
    public decimal CompensationAwarded { get; set; }
    public decimal GoodwillAwarded { get; set; }
    public string EvidenceReference { get; set; } = "";
}
public sealed record ComplaintUserOption(string Id, string Label);
public sealed record ComplaintOptions(IReadOnlyList<ComplianceWorkOption> Clients, IReadOnlyList<ComplaintUserOption> Users);
public sealed record ComplaintRow(ComplaintCase Case, string Handler, string NextAction, decimal CompensationPaid, decimal GoodwillPaid, bool OmbudReferred);
public sealed record ComplaintTotals(int Received, int Upheld, int Rejected, int Escalated, int Ombud, int CompensationCases, decimal CompensationPaid, int GoodwillCases, decimal GoodwillPaid, int Outstanding, int Unclassified);
public sealed record ComplaintRegisterPage(IReadOnlyList<ComplaintRow> Rows, ComplaintTotals Totals, bool CanManage, bool IsAdministrator);
public sealed record ComplaintPage(ComplaintCase Case, IReadOnlyList<ComplaintEvent> Events, string NextAction, bool CanManage, bool CanDecide, string CodexBrief);
