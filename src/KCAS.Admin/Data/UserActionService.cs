using System.Security.Claims;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class UserActionService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<IReadOnlyList<UserActionItem>> LoadAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return [];
        await using var db = await factory.CreateDbContextAsync();
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsApproved);
        if (user is null) return [];
        var roles = await (from link in db.UserRoles where link.UserId == user.Id
            join role in db.Roles on link.RoleId equals role.Id select role.Name!).ToListAsync();
        var permissions = await (from link in db.UserRoles where link.UserId == user.Id
            join claim in db.RoleClaims on link.RoleId equals claim.RoleId
            where claim.ClaimType == KcasClaimTypes.Permission select claim.ClaimValue!).ToListAsync();
        bool Can(string permission) => permissions.Contains(permission);
        var admin = roles.Contains(KcasRoles.Administrator);
        var receivesReviews = roles.Contains(KcasRoles.ComplianceAdministrator) || roles.Contains(KcasRoles.ComplianceApprover);
        var labels = new[] { user.Email, user.UserName, principal.Identity.Name }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray();
        bool IsMe(string? value) => labels.Contains(value, StringComparer.OrdinalIgnoreCase);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var assignments = await db.GovernanceRoleAssignments.AsNoTracking()
            .Where(x => x.IsActive && (x.StartDate == null || x.StartDate <= today) && (x.EndDate == null || x.EndDate >= today))
            .ToListAsync();
        var ki = assignments.Any(x => ComplianceApprovalRules.IsKeyIndividualRole(x.RoleType) &&
            !string.IsNullOrWhiteSpace(user.Email) && string.Equals(x.Email, user.Email, StringComparison.OrdinalIgnoreCase));
        var actions = new List<UserActionItem>();
        void Add(string key, string subject, string action, string url, DateOnly? due = null)
            => actions.Add(new(key, subject, action, url, due));

        var tasks = Can(KcasPermissions.ComplianceView) || receivesReviews
            ? await db.ComplianceTasks.AsNoTracking().Include(x => x.Client)
                .Where(x => x.Status != ComplianceStatuses.Closed && x.Status != ComplianceStatuses.Withdrawn &&
                    (admin || x.Client == null || !x.Client.ExcludeFromComplianceLists)).ToListAsync()
            : [];

        if (receivesReviews && Can(KcasPermissions.RiskAssessmentsView) &&
            (Can(KcasPermissions.KycManage) || Can(KcasPermissions.ComplianceManage)))
        {
            var requests = await db.ClientCodexReviewRequests.AsNoTracking().Include(x => x.Client)
                .Where(x => x.Status == "AwaitingCodex" && (admin || !x.Client.ExcludeFromComplianceLists)).ToListAsync();
            foreach (var request in requests.Where(x => Recipients(x.RecipientUserIdsJson).Contains(user.Id)))
                Add($"codex:{request.ClientId}", request.Client.DisplayName, "Run Codex review", $"/clients/{request.ClientId}/onboarding#codex-handoff");
        }

        if (Can(KcasPermissions.AdviceView))
        {
            var cases = await db.ClientAdviceCases.AsNoTracking().Include(x => x.Client).Include(x => x.Participants).ThenInclude(x => x.Client)
                .Where(x => x.Status == ClientAdviceStatuses.Draft || x.Status == ClientAdviceStatuses.Returned || x.Status == ClientAdviceStatuses.ReadyForReview)
                .ToListAsync();
            var recorded = await db.ComplianceAuditEvents.AsNoTracking()
                .Where(x => x.EntityType == nameof(ClientAdviceCase) && x.Action == "AdviceCodexPreparationRecorded")
                .Select(x => new { x.EntityId, x.UserName }).ToListAsync();
            foreach (var item in cases.Where(x => admin || !x.Client.ExcludeFromComplianceLists && !x.Participants.Any(p => p.Client.ExcludeFromComplianceLists)))
            {
                var preparation = tasks.FirstOrDefault(x => x.TaskType == ComplianceTaskTypes.AdvicePreparation &&
                    x.LinkedEntityType == nameof(ClientAdviceCase) && x.LinkedEntityId == item.Id && x.ClientId == item.ClientId && x.Status == ComplianceWorkStatuses.Open);
                if (preparation is not null && receivesReviews)
                    Add($"advice-preparation:{item.Id}", item.Client.DisplayName, "Run Codex advice preparation",
                        $"/clients/{item.ClientId}/advice/{item.Id}/preparation#codex-handoff", preparation.DueDate);
                if (item.Status == ClientAdviceStatuses.ReadyForReview && Can(KcasPermissions.AdviceReview) &&
                    !IsMe(item.PreparedBy) && !recorded.Any(x => x.EntityId == item.Id && IsMe(x.UserName)))
                    Add($"advice-review:{item.Id}", item.Client.DisplayName, "Review advice", $"/clients/{item.ClientId}/advice/{item.Id}#independent-review");
                if (item.Status == ClientAdviceStatuses.Returned && preparation is null && Can(KcasPermissions.AdvicePrepare) && IsMe(item.PreparedBy))
                    Add($"advice-returned:{item.Id}", item.Client.DisplayName, "Update returned advice", $"/clients/{item.ClientId}/advice/{item.Id}");
                if (item.Status == ClientAdviceStatuses.Draft && preparation is null && Can(KcasPermissions.AdvicePrepare) && IsMe(item.PreparedBy) &&
                    recorded.Any(x => x.EntityId == item.Id))
                    Add($"advice-confirm:{item.Id}", item.Client.DisplayName, "Confirm prepared advice", $"/clients/{item.ClientId}/advice/{item.Id}");
            }
        }

        if (ki && Can(KcasPermissions.ClientsManage) && Can(KcasPermissions.RiskAssessmentsView))
        {
            var clients = await db.Clients.AsNoTracking().Where(x => x.RequiresClientAcceptance && (admin || !x.ExcludeFromComplianceLists) &&
                db.ClientOnboardingProfiles.Any(p => p.ClientId == x.Id)).Select(x => x.Id).ToListAsync();
            var requests = await db.ClientCodexReviewRequests.AsNoTracking().Where(x => clients.Contains(x.ClientId)).ToListAsync();
            var decisions = await db.ClientAcceptanceDecisions.AsNoTracking().Where(x => clients.Contains(x.ClientId)).ToListAsync();
            foreach (var clientId in clients)
            {
                var request = requests.Where(x => x.ClientId == clientId).MaxBy(x => x.Id);
                var decision = decisions.Where(x => x.ClientId == clientId).MaxBy(x => x.Id);
                // Only recheck substantive readiness when the workflow has reached a new decision stage.
                if (request?.Status == "AwaitingCodex" || decision is not null &&
                    decision.Decision is "Accepted" or "Declined" or "ReturnForClarification" &&
                    (request?.CompletedAtUtc is null || request.CompletedAtUtc <= decision.DecidedAtUtc)) continue;
                var model = await ClientOnboardingService.BuildAsync(db, clientId, tracked: false, ensureDefaultRequirements: false);
                if (model.Status == "Ready for KI decision" &&
                    (model.Request is null || model.Request.Status == "ResultsRecorded" && model.Request.CompletedContentHash == model.ContentHash))
                    Add($"acceptance:{clientId}", model.Client.DisplayName, "Decide client acceptance", $"/clients/{clientId}/onboarding#ki-decision");
            }
        }

        if (Can(KcasPermissions.RiskAssessmentsView) && Can(KcasPermissions.RiskAssessmentsApprove))
        {
            var assessments = await db.ClientRiskAssessments.AsNoTracking().Include(x => x.Client).Include(x => x.Approvals)
                .Where(x => x.Status == ClientRiskAssessmentStatuses.PendingKiApproval && !x.Client!.RequiresClientAcceptance &&
                    (admin || !x.Client.ExcludeFromComplianceLists)).ToListAsync();
            foreach (var item in assessments)
                if (!item.Approvals.Any(x => IsMe(x.Approver)))
                    Add($"client-risk:{item.Id}", item.Client!.DisplayName, "Approve client risk", $"/clients/{item.ClientId}/risk#ki-approval");
        }

        if (Can(KcasPermissions.ComplianceView))
        {
            if (ki && Can(KcasPermissions.ComplianceApprove))
                foreach (var policy in await db.InsurancePolicies.AsNoTracking().Where(x => x.Status == InsuranceStages.Reviewed && x.Findings == "" && x.ReviewedVersion == x.Version && x.RequestedVersion == null).ToListAsync())
                    Add($"insurance-accept:{policy.Id}", $"PI cover {policy.PolicyNumber}", "Decide renewal acceptance", $"/compliance/insurance/{policy.Id}");
            var approvals = await db.ComplianceApprovals.AsNoTracking().ToListAsync();
            bool AlreadyApproved(string type, int targetId, string decision = ComplianceStatuses.Approved) => approvals.Any(x =>
                x.TargetEntityType == type && x.TargetEntityId == targetId && x.Decision == decision && IsMe(x.Approver));
            if (Can(KcasPermissions.ComplianceApprove))
            {
                if (ki && Can(KcasPermissions.ComplianceManage))
                    foreach (var item in await db.RiskMethodologyVersions.AsNoTracking().Where(x => x.Status == ComplianceStatuses.Review).ToListAsync())
                        Add($"methodology:{item.Id}", $"{item.Name} {item.VersionLabel}", "Approve methodology", $"/compliance/methodologies/update#methodology-{item.Id}");
                foreach (var item in await db.BusinessRiskAssessments.AsNoTracking().Include(x => x.Approvals).Where(x => x.Status == ComplianceStatuses.Review).ToListAsync())
                    if (!item.Approvals.Any(x => IsMe(x.Approver)))
                        Add($"bra:{item.Id}", item.Name, "Record BRA approval", $"/compliance/business-risk/{item.Id}#ki-approval");
                foreach (var item in await db.RmcpVersions.AsNoTracking().Where(x => x.Status == ComplianceStatuses.Review).ToListAsync())
                    if (!AlreadyApproved(nameof(RmcpVersion), item.Id))
                        Add($"rmcp:{item.Id}", item.VersionReference, "Record RMCP approval", $"/compliance/rmcp/{item.Id}#ki-approval");
            }
            var subjectTasks = await db.ClientSanctionsSubjects.AsNoTracking().Where(x => x.ComplianceTaskId != null)
                .Select(x => new { x.Id, x.ClientSanctionsBatchId, x.ComplianceTaskId }).ToListAsync();
            foreach (var task in tasks.Where(x => x.TaskType is not (ComplianceTaskTypes.CodexReview or ComplianceTaskTypes.AdvicePreparation)))
            {
                var controlled = task.TaskType is ComplianceTaskTypes.SanctionsCoverage or ComplianceTaskTypes.Complaint;
                var pending = task.Status == ComplianceWorkStatuses.PendingClosure;
                var mine = IsMe(task.Owner) || receivesReviews && task.Owner == ComplianceWorkService.ComplianceReviewAudience ||
                    ki && task.Owner == "Key Individuals";
                if (pending ? controlled || !Can(KcasPermissions.ComplianceApprove) || AlreadyApproved(nameof(ComplianceTask), task.Id, ComplianceStatuses.Closed)
                    : !Can(KcasPermissions.ComplianceManage) || !mine) continue;
                var subject = subjectTasks.FirstOrDefault(x => x.ComplianceTaskId == task.Id);
                var url = task.TaskType switch
                {
                    ComplianceTaskTypes.InsuranceRenewal => $"/compliance/insurance/{task.InsurancePolicyId}",
                    ComplianceTaskTypes.SanctionsCoverage when task.LinkedEntityType == nameof(SanctionsSourceCheck) => "/compliance/sanctions#official-source",
                    ComplianceTaskTypes.SanctionsCoverage when subject is not null => $"/compliance/sanctions/{subject.ClientSanctionsBatchId}/subjects/{subject.Id}",
                    ComplianceTaskTypes.SanctionsCoverage => $"/compliance/sanctions/{task.LinkedEntityId}",
                    ComplianceTaskTypes.Complaint => $"/compliance/complaints/{task.LinkedEntityId}",
                    _ => $"/compliance/work-register/{task.Id}"
                };
                var action = pending ? "Approve closure" : task.TaskType switch
                {
                    ComplianceTaskTypes.InsuranceRenewal => "Review PI renewal",
                    ComplianceTaskTypes.PeriodicReview or ComplianceTaskTypes.TriggerReview => "Complete client compliance review",
                    ComplianceTaskTypes.SanctionsCoverage when task.LinkedEntityType == nameof(SanctionsSourceCheck) => "Refresh official sanctions source",
                    ComplianceTaskTypes.SanctionsCoverage => "Check sanctions coverage",
                    ComplianceTaskTypes.Complaint => "Action complaint",
                    _ => "Complete compliance work"
                };
                Add($"task:{task.Id}", task.Client?.DisplayName ?? task.Title, action, url, task.DueDate);
            }
        }

        if (Can(KcasPermissions.EmployeesView) && (Can(KcasPermissions.EmployeesManage) || Can(KcasPermissions.EmployeesReview)))
        {
            var employeeTasks = await db.EmployeeComplianceTasks.AsNoTracking().Where(x => x.Status != "Closed").ToListAsync();
            var employees = await db.EmployeeProfiles.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.DisplayName);
            foreach (var task in employeeTasks.Where(x => Recipients(x.RecipientUserIdsJson).Contains(user.Id)))
                Add($"employee:{task.EmployeeProfileId}", employees[task.EmployeeProfileId], "Review employee compliance",
                    $"/compliance/employees/{task.EmployeeProfileId}", task.DueDate);
        }

        return actions.DistinctBy(x => x.Key).OrderByDescending(x => x.DueDate < today)
            .ThenBy(x => x.DueDate ?? DateOnly.MaxValue).ThenBy(x => x.Subject).ThenBy(x => x.Key).ToList();
    }

    private static string[] Recipients(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
}

public sealed record UserActionItem(string Key, string Subject, string Action, string Url, DateOnly? DueDate = null);
