using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientAdvicePreparationService(IDbContextFactory<ApplicationDbContext> factory)
{
    private const string Requested = "AdviceCodexPreparationRequested";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AdvicePreparationPage> LoadAsync(int clientId, int? caseId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.AdviceView);
        var client = await db.Clients.AsNoTracking().SingleAsync(x => x.Id == clientId);
        await ComplianceWorkflowAccess.VisibleAsync(db, client, actor.Id);
        var item = caseId.HasValue ? await CaseAsync(db, clientId, caseId.Value) : null;
        if (item is not null) await VisibleParticipantsAsync(db, item, actor.Id);
        var request = item is null ? null : await LatestAsync(db, item.Id);
        var task = request is null ? null : await TaskAsync(db, request, item!.Id);
        var canPrepare = await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.AdvicePrepare);
        var canCoordinate = await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id);
        return new(client.Id, client.DisplayName, client.KanaanId, client.ClientFolder, item, request, task,
            canPrepare && (item is null || Editable(item)), canCoordinate && task?.Status == ComplianceWorkStatuses.Open,
            item is null ? [] : ClientAdviceService.Validate(item));
    }

    public async Task<AdvicePreparationReceipt> RequestAsync(int clientId, int? caseId, AdvicePreparationEdit edit, ClaimsPrincipal principal)
    {
        var situation = ComplianceWorkflowAccess.Required(edit.Situation, "Situation", 12000);
        if ((edit.Reference?.Length ?? 0) > 2000) throw new ValidationException("Use a reference of at most 2000 characters.");
        if (!Guid.TryParse(edit.RequestKey, out _)) throw new ValidationException("Reload the request before saving.");
        if (!ClientAdviceTypes.All.Contains(edit.AdviceType)) throw new ValidationException("Select an advice type.");
        var methodology = ClientAdviceService.Methodology(edit.RiskMethodologyCode);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.AdvicePrepare);
        // Serialize requests for a client, including retries of the new-case form.
        var client = await db.Clients.FromSqlInterpolated($"SELECT * FROM `Clients` WHERE `Id` = {clientId} FOR UPDATE").SingleAsync();
        await ComplianceWorkflowAccess.VisibleAsync(db, client, actor.Id);
        if (!caseId.HasValue)
        {
            var priorJson = await (from audit in db.ComplianceAuditEvents
                join prior in db.ClientAdviceCases on audit.EntityId equals prior.Id
                where prior.ClientId == clientId && audit.EntityType == nameof(ClientAdviceCase) && audit.Action == Requested
                select audit.NewValueJson).ToListAsync();
            var repeated = priorJson.Select(Parse).FirstOrDefault(x => x?.RequestKey == edit.RequestKey);
            if (repeated is not null)
            {
                if (repeated.Situation != situation || repeated.Reference != edit.Reference?.Trim() ||
                    repeated.AdviceType != edit.AdviceType || repeated.RiskMethodologyCode != edit.RiskMethodologyCode)
                    throw new ValidationException("This request was already saved. Open its case to change the request.");
                await transaction.CommitAsync();
                return new(repeated.CaseId, repeated.Recipients.Count, true);
            }
        }
        var item = caseId.HasValue ? await CaseAsync(db, clientId, caseId.Value) : null;
        if (item is not null)
        {
            await VisibleParticipantsAsync(db, item, actor.Id);
            if (!Editable(item)) throw new ValidationException("Create a new case or revision before requesting further preparation.");
        }
        var previous = item is null ? null : await LatestAsync(db, item.Id);
        if (edit.ExpectedVersion != previous?.Version) throw new ValidationException("The preparation request changed. Reload before saving.");
        if (item is not null && (item.AdviceType != edit.AdviceType || item.RiskMethodologyCode != methodology.Code))
            throw new ValidationException("Save the advice type and methodology in the case before updating its request.");
        var restricted = client.ExcludeFromComplianceLists || item?.Participants.Any(x => x.Client.ExcludeFromComplianceLists) == true;
        var candidates = await (from user in db.Users where user.IsApproved
            join link in db.UserRoles on user.Id equals link.UserId join role in db.Roles on link.RoleId equals role.Id
            where role.Name == KcasRoles.ComplianceAdministrator || role.Name == KcasRoles.ComplianceApprover
            select user.Id).Distinct().ToListAsync();
        var recipients = new List<string>();
        foreach (var id in candidates)
            if ((!restricted || await ComplianceWorkflowAccess.IsAdminAsync(db, id)) &&
                await ComplianceWorkflowAccess.PermissionAsync(db, id, KcasPermissions.AdviceView)) recipients.Add(id);
        if (recipients.Count == 0) throw new ValidationException("No approved Compliance Administrator or Approver with access to this advice is available to receive the task.");
        if (item is null)
        {
            var advice = new ClientAdviceService(db);
            var id = await advice.CreateDraftAsync(clientId, edit.AdviceType, ComplianceWorkflowAccess.Label(actor));
            item = await CaseAsync(db, clientId, id);
            item.RiskMethodologyCode = methodology.Code;
        }
        var task = previous is null ? null : await TaskAsync(db, previous, item.Id);
        var reused = task?.Status == ComplianceWorkStatuses.Open;
        if (!reused)
        {
            var title = $"Codex advice preparation: {client.DisplayName}";
            task = new ComplianceTask { ClientId = clientId, TaskType = ComplianceTaskTypes.AdvicePreparation,
                LinkedEntityType = nameof(ClientAdviceCase), LinkedEntityId = item.Id,
                Status = ComplianceWorkStatuses.Open, Owner = ComplianceWorkService.ComplianceReviewAudience,
                Title = title[..Math.Min(240, title.Length)] };
            db.ComplianceTasks.Add(task);
            await db.SaveChangesAsync();
        }
        var request = new AdvicePreparationRequest
        {
            TaskId = task!.Id, CaseId = item.Id, RequestKey = edit.RequestKey,
            Situation = situation, Reference = edit.Reference?.Trim(), AdviceType = item.AdviceType,
            RiskMethodologyCode = item.RiskMethodologyCode, RequestedBy = ComplianceWorkflowAccess.Label(actor),
            RequestedAtUtc = DateTime.UtcNow, Recipients = recipients,
            Version = Guid.NewGuid().ToString("N")
        };
        task.Description = await BriefAsync(db, item, request);
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.UpdatedBy = request.RequestedBy;
        ComplianceWorkflowAccess.Audit(db, nameof(ClientAdviceCase), item.Id, Requested, actor,
            reused ? "Refresh the pending advice preparation request." : "Prepare advice from the adviser's described situation.", request);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(item.Id, recipients.Count, reused);
    }

    public async Task RecordResultsAsync(int clientId, int caseId, string version, string summary, ClaimsPrincipal principal)
    {
        summary = ComplianceWorkflowAccess.Required(summary, "Preparation findings", 12000);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.AdviceView);
        if (!await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id))
            throw new UnauthorizedAccessException("An approved Compliance Administrator or Approver must record the Codex preparation findings.");
        var client = await db.Clients.FromSqlInterpolated($"SELECT * FROM `Clients` WHERE `Id` = {clientId} FOR UPDATE").SingleAsync();
        await ComplianceWorkflowAccess.VisibleAsync(db, client, actor.Id);
        var item = await CaseAsync(db, clientId, caseId);
        await VisibleParticipantsAsync(db, item, actor.Id);
        var request = await LatestAsync(db, caseId) ?? throw new ValidationException("Request advice preparation first.");
        if (request.Version != version) throw new ValidationException("The request changed. Reload and use the current handoff.");
        if (!Editable(item)) throw new ValidationException("Only draft or returned advice can receive preparation results.");
        if (item.AdviceType != request.AdviceType || item.RiskMethodologyCode != request.RiskMethodologyCode)
            throw new ValidationException("The advice type or methodology changed. Update the handoff before recording its results.");
        var task = await TaskAsync(db, request, item.Id) ?? throw new ValidationException("No active local preparation task exists. Request preparation in this system first.");
        if (task.Status != ComplianceWorkStatuses.Open) throw new ValidationException("This preparation task is already completed.");
        if (item.FactSources.Count == 0 || string.IsNullOrWhiteSpace(item.RecommendationSummary))
            throw new ValidationException("Save the supported advice draft and material fact sources in the case before recording preparation results.");
        var gaps = ClientAdviceService.Validate(item).Concat(item.ReviewFindings.Where(x => x.Status == ClientAdviceFindingStatuses.Open)
            .Select(x => x.Finding)).ToList();
        task.Status = ComplianceStatuses.Closed;
        task.Outcome = summary;
        task.EvidenceSummary = "Codex preparation; supporting facts, documents, calculations and findings retained in the advice case.";
        task.ClosureReason = gaps.Count == 0 ? "Preparation recorded; adviser and independent review remain." : "Preparation recorded; outstanding facts or findings remain in the case.";
        task.ClosedAtUtc = task.UpdatedAtUtc = DateTime.UtcNow;
        task.ClosedBy = task.UpdatedBy = ComplianceWorkflowAccess.Label(actor);
        ComplianceWorkflowAccess.Audit(db, nameof(ClientAdviceCase), caseId, "AdviceCodexPreparationRecorded", actor,
            task.ClosureReason, new { request.TaskId, request.Version, PerformedBy = "Codex", Summary = summary, Outstanding = gaps });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<AdvicePreparationNotification>> NotificationsAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (principal.Identity?.IsAuthenticated != true || actor?.IsApproved != true ||
            !await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id) ||
            !await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.AdviceView)) return [];
        var admin = await ComplianceWorkflowAccess.IsAdminAsync(db, actor.Id);
        return await (from task in db.ComplianceTasks.AsNoTracking()
            join item in db.ClientAdviceCases on task.LinkedEntityId equals item.Id
            where task.TaskType == ComplianceTaskTypes.AdvicePreparation && task.Status == ComplianceWorkStatuses.Open &&
                task.LinkedEntityType == nameof(ClientAdviceCase) && task.ClientId == item.ClientId &&
                (item.Status == ClientAdviceStatuses.Draft || item.Status == ClientAdviceStatuses.Returned) &&
                (admin || !item.Client.ExcludeFromComplianceLists && !item.Participants.Any(x => x.Client.ExcludeFromComplianceLists))
            orderby task.CreatedAtUtc
            select new AdvicePreparationNotification(item.ClientId, item.Id, item.Client.DisplayName,
                task.UpdatedBy ?? item.PreparedBy, task.UpdatedAtUtc ?? task.CreatedAtUtc)).ToListAsync();
    }

    private static async Task<string> BriefAsync(ApplicationDbContext db, ClientAdviceCase item, AdvicePreparationRequest request)
    {
        var ids = item.Participants.Select(x => x.ClientId).Append(item.ClientId).Distinct().ToList();
        var accounts = await db.ClientInvestmentAccounts.AsNoTracking().Where(x => ids.Contains(x.ClientId)).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ClientId, x.AccountNumber, x.ProductName, x.SurrenderDate }).ToListAsync();
        var text = new StringBuilder()
            .AppendLine($"Prepare advice for {item.Client.DisplayName}; KCAS client ID {item.ClientId}; Kanaan ID {item.Client.KanaanId}.")
            .AppendLine($"Advice case ID {item.Id}, revision {item.Revision}; /clients/{item.ClientId}/advice/{item.Id}.")
            .AppendLine($"Preparation task ID {request.TaskId}; request version {request.Version}.")
            .AppendLine($"Requested by adviser {request.RequestedBy} at {request.RequestedAtUtc:O}; advice type {item.AdviceType}.")
            .AppendLine($"Risk Analyser: {ClientAdviceService.Methodology(item.RiskMethodologyCode).Name} ({item.RiskMethodologyCode}).")
            .AppendLine($"Adviser's situation: {request.Situation}")
            .AppendLine($"Correspondence/contact reference: {request.Reference ?? "Read the situation and actual correspondence; record supported contact evidence."}")
            .AppendLine($"Evidence folder: {item.Client.ClientFolder ?? "Not recorded; resolve the correct client folder before reading documents."}");
        foreach (var party in item.Participants.OrderBy(x => x.ClientId))
            text.AppendLine($"Advice subject: {party.Client.DisplayName}; client ID {party.ClientId}; folder {party.Client.ClientFolder}.");
        foreach (var account in accounts)
            text.AppendLine($"Investment: KCAS ID {account.Id}; client {account.ClientId}; account {account.AccountNumber}; {account.ProductName}; surrender date {account.SurrenderDate:yyyy-MM-dd}.");
        text.AppendLine("Read the actual client folder, correspondence, historical Risk Analysers/CARs, mandates, fact sheets and current KCAS accounts/valuations. Do not run or overwrite evidence scans. Confirm the requested advice subjects before adding any family member; a shared Kanaan ID does not by itself make every record an advice subject.")
            .AppendLine("Prepare this case using authorised ClientAdviceService LoadCaseAsync/SaveDraftAsync and the existing finding/source services. Do not replace another case, invent answers, dates, client instructions or approvals, or change the selected methodology without the adviser's recorded agreement. Record real source paths/references, source dates and rationale for material facts and Risk Analyser answers; save only supported answers and leave genuine gaps as findings for the adviser.")
            .AppendLine("Use the latest evidenced valuations with their actual dates/currencies; reconcile the calculation basis and avoid double-counting joint accounts. Distinguish current facts, historic reference amounts and proposed amounts. Explain any assumptions and preserve applicable cash-account replenishment arrangements evidenced in correspondence. Use Kanaan's actual products, fees, restrictions and mandate scope.")
            .AppendLine("Prepare the professional client-facing Risk Analyser and CAR: needs/objectives, financial position, scope, products considered, exact recommendation, rationale, costs, tax, liquidity, risks and applicable replacement consequences. Keep private paths, internal handoff deliberations and unresolved findings out of the approved letter; retain them in the internal sources/findings and draft issue report.")
            .AppendLine("Generate and visually check the existing draft-preview PDF (including the issue report) and, where appropriate, the labelled approved-letter sample. Report the saved case, calculation results, source evidence, genuine gaps and next adviser action. A proposal is sent for client approval; do not block preparing/issuing it merely because that approval has not yet been obtained.")
            .AppendLine($"After saving the actual draft and sources, use ClientAdvicePreparationService.RecordResultsAsync(clientId: {item.ClientId}, caseId: {item.Id}, version: {request.Version}) with a substantive summary under the authorised Compliance account; performer is Codex and the actual saving account/time is retained. Request a refreshed handoff if advice type/methodology changes. Do not submit for independent review, approve, issue, record client authority or execution on anyone's behalf.");
        return text.ToString();
    }

    private static Task<ClientAdviceCase> CaseAsync(ApplicationDbContext db, int clientId, int caseId) => db.ClientAdviceCases
        .Include(x => x.Client).Include(x => x.Participants).ThenInclude(x => x.Client).Include(x => x.RiskResponses)
        .Include(x => x.FactSources).Include(x => x.Products).Include(x => x.ReviewFindings)
        .SingleAsync(x => x.Id == caseId && x.ClientId == clientId);
    private static async Task VisibleParticipantsAsync(ApplicationDbContext db, ClientAdviceCase item, string actorId)
    {
        await ComplianceWorkflowAccess.VisibleAsync(db, item.Client, actorId);
        foreach (var party in item.Participants) await ComplianceWorkflowAccess.VisibleAsync(db, party.Client, actorId);
    }
    private static bool Editable(ClientAdviceCase item) => item.Status is ClientAdviceStatuses.Draft or ClientAdviceStatuses.Returned;
    private static Task<ComplianceTask?> TaskAsync(ApplicationDbContext db, AdvicePreparationRequest request, int caseId) => db.ComplianceTasks
        .SingleOrDefaultAsync(x => x.Id == request.TaskId && x.TaskType == ComplianceTaskTypes.AdvicePreparation &&
            x.LinkedEntityType == nameof(ClientAdviceCase) && x.LinkedEntityId == caseId &&
            x.Description != null && x.Description.Contains("request version " + request.Version + "."));
    private static async Task<AdvicePreparationRequest?> LatestAsync(ApplicationDbContext db, int caseId)
    {
        var tasks = await db.ComplianceTasks.AsNoTracking().Where(x => x.TaskType == ComplianceTaskTypes.AdvicePreparation &&
            x.LinkedEntityType == nameof(ClientAdviceCase) && x.LinkedEntityId == caseId).ToListAsync();
        if (tasks.Count == 0) return null;
        var requests = await db.ComplianceAuditEvents.AsNoTracking()
            .Where(x => x.EntityType == nameof(ClientAdviceCase) && x.EntityId == caseId && x.Action == Requested)
            .OrderByDescending(x => x.TimestampUtc).ThenByDescending(x => x.Id).Select(x => x.NewValueJson).ToListAsync();
        // Transferred audit history must not associate source task IDs with unrelated live tasks.
        return requests.Select(Parse).FirstOrDefault(x => x is not null && tasks.Any(t => t.Id == x.TaskId &&
            t.Description?.Contains("request version " + x.Version + ".", StringComparison.Ordinal) == true));
    }
    private static AdvicePreparationRequest? Parse(string? json) => json is null ? null : JsonSerializer.Deserialize<AdvicePreparationRequest>(json, Json);
}

public sealed class AdvicePreparationEdit
{
    public string RequestKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? ExpectedVersion { get; set; }
    public string Situation { get; set; } = "";
    public string? Reference { get; set; }
    public string AdviceType { get; set; } = ClientAdviceTypes.AnnualReview;
    public string RiskMethodologyCode { get; set; } = ClientAdviceMethodologies.KcasEvidenced;
}
public sealed class AdvicePreparationRequest
{
    public int TaskId { get; set; }
    public int CaseId { get; set; }
    public string RequestKey { get; set; } = "";
    public string Version { get; set; } = "";
    public string Situation { get; set; } = "";
    public string? Reference { get; set; }
    public string AdviceType { get; set; } = "";
    public string RiskMethodologyCode { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public List<string> Recipients { get; set; } = [];
}
public sealed record AdvicePreparationPage(int ClientId, string ClientName, string? KanaanId, string? Folder,
    ClientAdviceCase? Case, AdvicePreparationRequest? Request, ComplianceTask? Task, bool CanRequest, bool CanRecordResults,
    IReadOnlyList<string> Blockers);
public sealed record AdvicePreparationReceipt(int CaseId, int RecipientCount, bool Reused);
public sealed record AdvicePreparationNotification(int ClientId, int CaseId, string ClientName, string RequestedBy, DateTime RequestedAtUtc);
