using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InsuranceService(ApplicationDbContext db, InsuranceFiles files)
{
    public async Task<List<InsurancePolicy>> ListAsync(ClaimsPrincipal principal)
    {
        await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        return await db.InsurancePolicies.AsNoTracking().Include(x => x.Documents).OrderByDescending(x => x.StartDate).ToListAsync();
    }

    public async Task<InsurancePolicy> LoadAsync(int id, ClaimsPrincipal principal)
    {
        await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        return await db.InsurancePolicies.AsNoTracking().Include(x => x.Documents).Include(x => x.Events).SingleAsync(x => x.Id == id);
    }

    public async Task<int> SaveAsync(InsurancePolicy input, string reason, ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceManage);
        Validate(input);
        InsurancePolicy row;
        if (input.Id == 0) { row = new(); db.InsurancePolicies.Add(row); }
        else
        {
            row = await db.InsurancePolicies.Include(x => x.Events).SingleAsync(x => x.Id == input.Id);
            ComplianceWorkflowAccess.Expect(row.Version, input.Version);
            if (row.Status is InsuranceStages.Accepted or InsuranceStages.Confirmed)
                throw new ValidationException("Accepted or confirmed terms are frozen. Record a new policy year rather than changing the accepted record.");
        }
        if (await db.InsurancePolicies.AnyAsync(x => x.Id != row.Id && x.PolicyNumber == input.PolicyNumber.Trim() && x.StartDate == input.StartDate))
            throw new ValidationException("This policy number and start date already exist.");
        CopyTerms(input, row);
        row.Version = ComplianceWorkflowAccess.NewVersion();
        row.RequestedVersion = null;
        row.ReviewedVersion = null;
        if (row.Status == InsuranceStages.Reviewed) row.Status = InsuranceStages.QuotationReceived;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = ComplianceWorkflowAccess.Label(actor);
        AddEvent(row, "TermsRecorded", "Manual", actor, DateOnly.FromDateTime(DateTime.Today), reason, null);
        await db.SaveChangesAsync();
        ComplianceWorkflowAccess.Audit(db, nameof(InsurancePolicy), row.Id, "InsuranceTermsRecorded", actor, reason,
            new { row.TransferKey, row.Version, row.PolicyNumber, row.StartDate });
        await db.SaveChangesAsync();
        await RefreshTasksAsync();
        return row.Id;
    }

    public async Task LinkAsync(int id, string version, string title, string kind, string path, DateOnly? date, string reason, ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceManage);
        var row = await MutableAsync(id, version);
        if (row.Status is InsuranceStages.Accepted or InsuranceStages.Confirmed)
            throw new ValidationException("Link confirmation using the renewal action; accepted supporting documents are frozen.");
        if (!InsuranceStages.DocumentKinds.Contains(kind)) throw new ValidationException("Select a valid document type.");
        var relative = files.Relative(path);
        var document = row.Documents.FirstOrDefault(x => x.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
        var refreshing = document is not null;
        document ??= new() { RelativePath = relative };
        document.Title = ComplianceWorkflowAccess.Required(title, "Title", 240);
        document.Kind = kind; document.DocumentDate = date;
        document.Sha256 = await files.FingerprintAsync(relative);
        if (!refreshing) row.Documents.Add(document);
        row.Version = ComplianceWorkflowAccess.NewVersion(); row.RequestedVersion = null; row.ReviewedVersion = null;
        if (row.Status == InsuranceStages.Reviewed) row.Status = InsuranceStages.QuotationReceived;
        AddEvent(row, refreshing ? "DocumentRefreshed" : "DocumentLinked", "Manual", actor, DateOnly.FromDateTime(DateTime.Today), reason, document.TransferKey);
        await db.SaveChangesAsync();
        await RefreshTasksAsync();
    }

    public async Task<string> RequestCodexAsync(int id, string version, ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceManage);
        var row = await MutableAsync(id, version);
        if (row.Status is InsuranceStages.Accepted or InsuranceStages.Confirmed) throw new ValidationException("This policy has already been accepted.");
        if (row.RequestedVersion != row.Version)
        {
            AddEvent(row, "CodexRequested", ComplianceWorkflowAccess.Label(actor), actor, DateOnly.FromDateTime(DateTime.Today), "Prepare the renewal review from the linked evidence.", null);
            row.RequestedVersion = row.Version;
            await db.SaveChangesAsync();
        }
        await RefreshTasksAsync();
        var prior = await db.InsurancePolicies.AsNoTracking().Include(x => x.Documents)
            .Where(x => x.PolicyNumber == row.PolicyNumber && x.StartDate < row.StartDate).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return $"PI renewal review; KCAS policy ID {row.Id}; version {row.Version}; /compliance/insurance/{row.Id}.\n" +
            $"Policy {row.PolicyNumber}; insured {row.InsuredName}; period {row.StartDate:yyyy-MM-dd} to {row.EndDate:yyyy-MM-dd}.\n" +
            $"Compliance file root on this instance: {files.Root}.\n" +
            string.Join('\n', row.Documents.Select(x => $"{x.Kind}: {x.RelativePath}; SHA256 {x.Sha256}")) +
            (prior is null ? "\nNo prior year linked; research the PI cover archive." : "\nPrevious year:\n" + string.Join('\n', prior.Documents.Select(x => $"{x.Kind}: {x.RelativePath}"))) +
            "\nRead the actual files and previous terms; compare cover, premium/VAT, limits, excesses, dates, exclusions, payment arrangements, disclosures and acceptance requirements. Do not scan folders or modify signed originals. Record actual findings and genuine gaps; do not invent approvals, receipt, payment or binding. Use InsuranceService.RecordReviewAsync with the current policy ID/version, substantive summary and findings under the authorised Compliance account. Performer is Codex; saving account and time remain recorded. Terms/document changes require a refreshed handoff. The authorised KI decides acceptance; insurer confirmation requires actual evidence.";
    }

    public async Task RecordReviewAsync(int id, string version, string summary, string findings, ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceManage);
        var row = await MutableAsync(id, version);
        if (row.RequestedVersion != version) throw new ValidationException("Request a current Codex handoff before recording results.");
        if (!row.Documents.Any(x => x.Kind == "Quotation")) throw new ValidationException("Link the actual quotation first.");
        if (findings.Length > 8000) throw new ValidationException("Keep findings within 8,000 characters.");
        foreach (var doc in row.Documents)
            if (await files.FingerprintAsync(doc.RelativePath) != doc.Sha256) throw new ValidationException("A linked file changed; refresh the evidence and request a new handoff.");
        row.Findings = findings.Trim();
        AddEvent(row, "CodexReviewRecorded", "Codex", actor, DateOnly.FromDateTime(DateTime.Today), summary, null);
        row.Status = InsuranceStages.Reviewed; row.ReviewedVersion = row.Version; row.RequestedVersion = null;
        await db.SaveChangesAsync();
        await RefreshTasksAsync();
    }

    public async Task<bool> CanAcceptAsync(ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        if (!await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.ComplianceApprove)) return false;
        var today = DateOnly.FromDateTime(DateTime.Today);
        return (await db.GovernanceRoleAssignments.AsNoTracking().Where(x => x.IsActive && (x.StartDate == null || x.StartDate <= today) &&
            (x.EndDate == null || x.EndDate >= today)).ToListAsync()).Any(x => ComplianceApprovalRules.IsKeyIndividualRole(x.RoleType) &&
                !string.IsNullOrWhiteSpace(actor.Email) && string.Equals(x.Email, actor.Email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AdvanceAsync(int id, string version, string stage, DateOnly occurredOn, string summary,
        string evidencePath, ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal,
            stage == InsuranceStages.Accepted ? KcasPermissions.ComplianceApprove : KcasPermissions.ComplianceManage);
        var row = await MutableAsync(id, version);
        if (occurredOn > DateOnly.FromDateTime(DateTime.Today) || occurredOn.Year < 2000) throw new ValidationException("Record the actual event date, not a future date.");
        var valid = stage switch
        {
            InsuranceStages.Submitted => row.Status == InsuranceStages.Preparation,
            InsuranceStages.QuotationReceived => row.Status is InsuranceStages.Preparation or InsuranceStages.Submitted,
            InsuranceStages.Reviewed => row.Status == InsuranceStages.QuotationReceived,
            InsuranceStages.Accepted => row.Status == InsuranceStages.Reviewed,
            InsuranceStages.Confirmed => row.Status == InsuranceStages.Accepted || (row.Status == InsuranceStages.Preparation && row.EndDate < DateOnly.FromDateTime(DateTime.Today)),
            _ => false
        };
        if (!valid) throw new ValidationException("The selected action is not available at this stage.");
        if (stage == InsuranceStages.Accepted)
        {
            if (!await CanAcceptAsync(principal)) throw new UnauthorizedAccessException("A current authorised KI must record acceptance.");
            if (row.ReviewedVersion != version || row.RequestedVersion is not null || !string.IsNullOrWhiteSpace(row.Findings))
                throw new ValidationException("Resolve findings and complete the current review before acceptance.");
        }
        if (stage is InsuranceStages.Accepted or InsuranceStages.Confirmed)
        {
            if (row.EndDate < row.StartDate) throw new ValidationException("Correct the insurance period before acceptance or confirmation.");
            foreach (var doc in row.Documents)
                if (await files.FingerprintAsync(doc.RelativePath) != doc.Sha256) throw new ValidationException("A linked file changed; refresh the evidence and review first.");
        }
        Guid? key = null;
        if (stage == InsuranceStages.Reviewed)
        {
            if (!row.Documents.Any(x => x.Kind == "Quotation")) throw new ValidationException("Link the actual quotation first.");
            row.ReviewedVersion = version;
        }
        else
        {
            var relative = files.Relative(evidencePath);
            var document = row.Documents.FirstOrDefault(x => x.RelativePath == relative);
            var kind = stage switch { InsuranceStages.Accepted => "Acceptance", InsuranceStages.Confirmed => "PolicySchedule", _ => "Correspondence" };
            if (document is not null && stage == InsuranceStages.Confirmed && document.Kind is not ("PolicySchedule" or "InsurerConfirmation"))
                throw new ValidationException("Confirmation requires the final schedule or written insurer confirmation, not a quotation.");
            if (document is not null && stage == InsuranceStages.Accepted && document.Kind is not ("Acceptance" or "Correspondence"))
                throw new ValidationException("Link the actual acceptance correspondence, not the quotation, as evidence of acceptance.");
            if (document is null)
            {
                document = new() { Kind = kind, Title = InsuranceStages.Display(stage), RelativePath = relative,
                    Sha256 = await files.FingerprintAsync(relative), DocumentDate = occurredOn };
                row.Documents.Add(document);
            }
            key = document.TransferKey;
        }
        AddEvent(row, stage, stage == InsuranceStages.Accepted ? ComplianceWorkflowAccess.Label(actor) : "Manual", actor, occurredOn, summary, key);
        if (stage == InsuranceStages.Reviewed) row.ReviewedVersion = row.Version;
        row.Status = stage;
        row.RequestedVersion = null;
        await db.SaveChangesAsync();
        await RefreshTasksAsync();
    }

    public async Task<List<InsurancePolicy>> NotificationsAsync(ClaimsPrincipal principal)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        if (!await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id)) return [];
        await RefreshTasksAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = await db.InsurancePolicies.AsNoTracking().OrderBy(x => x.EndDate).ToListAsync();
        return rows.Where(x => x.RequestedVersion != null || (x.EndDate.AddDays(-x.ReminderDays) <= today &&
            !rows.Any(y => y.PolicyNumber == x.PolicyNumber && y.StartDate > x.EndDate && y.StartDate <= x.EndDate.AddDays(1) && y.Status == InsuranceStages.Confirmed))).ToList();
    }

    public async Task RefreshTasksAsync(bool retryDuplicate = true)
    {
        var rows = await db.InsurancePolicies.ToListAsync();
        var tasks = await db.ComplianceTasks.Where(x => x.TaskType == ComplianceTaskTypes.InsuranceRenewal).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var row in rows)
        {
            var renewed = rows.Any(x => x.PolicyNumber == row.PolicyNumber && x.StartDate == row.EndDate.AddDays(1) && x.Status == InsuranceStages.Confirmed);
            var needsRenewal = row.EndDate.AddDays(-row.ReminderDays) <= today && !renewed;
            var needed = needsRenewal || row.RequestedVersion != null;
            var task = tasks.FirstOrDefault(x => x.LinkedEntityId == row.Id);
            if (task is null && needed)
            {
                task = new() { TaskType = ComplianceTaskTypes.InsuranceRenewal, InsurancePolicyId = row.Id, LinkedEntityType = nameof(InsurancePolicy), LinkedEntityId = row.Id };
                db.ComplianceTasks.Add(task);
            }
            if (task is null) continue;
            task.Title = $"PI cover: {row.PolicyNumber} ({row.StartDate.Year})";
            task.Owner = ComplianceWorkService.ComplianceReviewAudience;
            task.Description = $"/compliance/insurance/{row.Id} | {(row.RequestedVersion != null ? "Awaiting Codex renewal review" : InsuranceStages.Display(row.Status))} | Expires {row.EndDate:yyyy-MM-dd}";
            task.DueDate = row.EndDate;
            task.Priority = row.EndDate.AddDays(-row.UrgentDays) <= today ? "High" : "Normal";
            task.EvidenceSummary = row.EndDate.AddDays(-row.FollowUpDays) <= today ? "Renewal follow-up required." : "Renewal preparation required.";
            task.Status = needed ? ComplianceWorkStatuses.Open : ComplianceStatuses.Closed;
            task.UpdatedBy = "KCAS"; task.UpdatedAtUtc = DateTime.UtcNow;
            task.ClosedAtUtc = needed ? null : DateTime.UtcNow;
            task.ClosedBy = needed ? null : "KCAS";
        }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (retryDuplicate && ex.InnerException is MySql.Data.MySqlClient.MySqlException { Number: 1062 })
        {
            // A background refresh and a user request can create the same reminder concurrently.
            db.ChangeTracker.Clear();
            await RefreshTasksAsync(false);
        }
    }

    public static void Validate(InsurancePolicy row)
    {
        Validator.ValidateObject(row, new ValidationContext(row), true);
        if (row.StartDate.Year < 2000 || row.EndDate.Year < 2000 || row.EndDate.Year > 9998 ||
            row.ReminderDays is < 1 or > 365 || row.FollowUpDays < 1 || row.FollowUpDays > row.ReminderDays || row.UrgentDays < 1 || row.UrgentDays > row.FollowUpDays)
            throw new ValidationException("Use valid dates and reminder thresholds: 365 >= first >= follow-up >= urgent >= 1 day.");
        if (new[] { row.Limit, row.Excess, row.PremiumExVat, row.Vat, row.MonthlyInstalment ?? 0 }.Any(x => x < 0))
            throw new ValidationException("Amounts cannot be negative.");
        if (row.PaymentFrequency is not ("Unconfirmed" or "Annual" or "Monthly")) throw new ValidationException("Select a payment frequency.");
    }

    internal static void CopyTerms(InsurancePolicy source, InsurancePolicy target)
    {
        // Explicit list prevents status, provenance and children being overwritten by an edit form.
        target.InsuredName = source.InsuredName.Trim(); target.PolicyNumber = source.PolicyNumber.Trim(); target.Insurer = source.Insurer.Trim();
        target.Broker = source.Broker; target.BrokerContact = source.BrokerContact; target.PrincipalAddress = source.PrincipalAddress;
        target.BusinessDescription = source.BusinessDescription; target.StartDate = source.StartDate; target.EndDate = source.EndDate;
        target.RetroactiveDate = source.RetroactiveDate; target.Limit = source.Limit; target.CombinedLimit = source.CombinedLimit; target.Excess = source.Excess;
        target.PremiumExVat = source.PremiumExVat; target.Vat = source.Vat; target.MonthlyInstalment = source.MonthlyInstalment;
        target.PaymentFrequency = source.PaymentFrequency; target.CoverSummary = source.CoverSummary; target.Exclusions = source.Exclusions;
        target.Findings = source.Findings; target.Owner = source.Owner; target.ReminderDays = source.ReminderDays; target.FollowUpDays = source.FollowUpDays; target.UrgentDays = source.UrgentDays;
    }

    private async Task<InsurancePolicy> MutableAsync(int id, string version)
    {
        var row = await db.InsurancePolicies.Include(x => x.Documents).Include(x => x.Events).SingleAsync(x => x.Id == id);
        ComplianceWorkflowAccess.Expect(row.Version, version);
        return row;
    }

    private static void AddEvent(InsurancePolicy row, string action, string performer, ApplicationUser actor, DateOnly date, string summary, Guid? document)
    {
        row.Version = ComplianceWorkflowAccess.NewVersion();
        row.UpdatedAtUtc = DateTime.UtcNow; row.UpdatedBy = ComplianceWorkflowAccess.Label(actor);
        row.Events.Add(new() { Action = action, Performer = performer, RecordedBy = row.UpdatedBy, OccurredOn = date,
            Summary = ComplianceWorkflowAccess.Required(summary, "Summary / reason"), DocumentKey = document });
    }
}

public sealed class InsuranceReminderJob(IServiceScopeFactory scopes, ILogger<InsuranceReminderJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<InsuranceService>().RefreshTasksAsync(); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "PI renewal reminders could not be refreshed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
