using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class EmployeeComplianceService(IDbContextFactory<ApplicationDbContext> factory, EmployeeEvidenceFiles files)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EmployeePermissionState> PermissionsAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var id = UserId(principal);
        return new(await HasPermissionAsync(db, id, KcasPermissions.EmployeesManage),
            await HasPermissionAsync(db, id, KcasPermissions.EmployeesReview),
            await HasPermissionAsync(db, id, KcasPermissions.SecurityManage));
    }

    public async Task<IReadOnlyList<EmployeeRegisterRow>> RegisterAsync(ClaimsPrincipal principal, string? search = null, string? status = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var query = db.EmployeeProfiles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.DisplayName.Contains(search) || x.LegalName.Contains(search) || x.Aliases.Contains(search));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.EmploymentStatus == status);
        var profiles = await query.OrderBy(x => x.DisplayName).ToListAsync();
        var ids = profiles.Select(x => x.Id).ToList();
        var reviews = await db.EmployeeComplianceReviews.AsNoTracking().Where(x => ids.Contains(x.EmployeeProfileId)).ToListAsync();
        var tasks = await db.EmployeeComplianceTasks.AsNoTracking().Where(x => ids.Contains(x.EmployeeProfileId) && x.Status != "Closed").ToListAsync();
        return profiles.Select(p => new EmployeeRegisterRow(p, reviews.Where(r => r.EmployeeProfileId == p.Id).OrderByDescending(r => r.Id).FirstOrDefault(), tasks.Count(t => t.EmployeeProfileId == p.Id))).ToList();
    }

    public async Task<EmployeeReviewPage> LoadAsync(int employeeId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync(x => x.Id == employeeId);
        var history = await db.EmployeeComplianceReviews.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderByDescending(x => x.Id).ToListAsync();
        var review = history.FirstOrDefault();
        var reviewIds = history.Select(x => x.Id).ToList();
        var checks = await db.EmployeeComplianceChecks.AsNoTracking().Where(x => reviewIds.Contains(x.EmployeeComplianceReviewId)).OrderByDescending(x => x.Id).ToListAsync();
        var access = await db.EmployeeAccessConfirmations.AsNoTracking().Where(x => reviewIds.Contains(x.EmployeeComplianceReviewId)).OrderByDescending(x => x.Id).ToListAsync();
        var decisions = await db.EmployeeReviewDecisions.AsNoTracking().Where(x => reviewIds.Contains(x.EmployeeComplianceReviewId)).OrderByDescending(x => x.Id).ToListAsync();
        var accounts = await (from link in db.EmployeeAccountLinks where link.EmployeeProfileId == employeeId
                              join user in db.Users on link.UserId equals user.Id
                              select new EmployeeAccountOption(user.Id, user.Email ?? user.UserName ?? user.Id)).ToListAsync();
        var scope = await AccountScopeAsync(db, employeeId);
        var blockers = review is null ? ["Start the employee review."] : await BlockersAsync(db, profile, review, checks, access, scope);
        return new(profile, review, history, checks, access, decisions, accounts, scope, blockers,
            await db.EmployeeComplianceTasks.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderByDescending(x => x.Id).ToListAsync(),
            await db.EmployeeComplianceAuditEvents.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderByDescending(x => x.Id).Take(100).ToListAsync(),
            await UserNamesAsync(db));
    }

    public async Task<int> SaveProfileAsync(EmployeeProfileEdit edit, string reason, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            reason = Required(reason, "A change reason");
            var profile = edit.Id.HasValue ? await db.EmployeeProfiles.SingleAsync(x => x.Id == edit.Id.Value) : new EmployeeProfile { CreatedByUserId = actor.Id };
            if (edit.Id.HasValue) Expect(profile.Version, edit.Version);
            ValidateProfile(edit);
            if (!edit.Id.HasValue) db.EmployeeProfiles.Add(profile);
            var old = Serialize(profile);
            ApplyProfile(profile, edit);
            Touch(profile);
            await db.SaveChangesAsync();
            if (profile.EmploymentStatus != "Inactive")
                await QueueAsync(db, profile.Id, "profile:" + profile.Version, edit.Id.HasValue ? "RoleOrIdentityChange" : "InitialReview", reason);
            else
            {
                await QueueAsync(db, profile.Id, "leave:" + profile.Version, "AccessRemoval", "Verify actual KCAS and external access removal; retain employee history.");
                var tasks = await db.EmployeeComplianceTasks.Where(x => x.EmployeeProfileId == profile.Id && x.Status != "Closed" && x.Kind != "AccessRemoval").ToListAsync();
                foreach (var task in tasks) { task.Status = "Closed"; task.ClosedAtUtc = DateTime.UtcNow; }
            }
            Audit(db, profile.Id, edit.Id.HasValue ? "ProfileUpdated" : "ProfileCreated", actor.Id, reason, new { Old = old, Current = profile });
            return profile.Id;
        });

    public async Task LinkAccountAsync(int employeeId, string expectedVersion, string accountId, string verificationReference, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            if (!await HasPermissionAsync(db, actor.Id, KcasPermissions.SecurityManage)) throw new UnauthorizedAccessException("Account identity links require Security.Manage.");
            var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == employeeId);
            Expect(profile.Version, expectedVersion);
            if (!await db.Users.AnyAsync(x => x.Id == accountId)) throw new ValidationException("The account does not exist.");
            var oldLink = await db.EmployeeAccountLinks.SingleOrDefaultAsync(x => x.UserId == accountId);
            if (oldLink is not null) throw new ValidationException("This account is already linked to an employee; identity links cannot be silently reassigned.");
            verificationReference = Required(verificationReference, "An identity verification reference");
            db.EmployeeAccountLinks.Add(new() { EmployeeProfileId = employeeId, UserId = accountId, VerificationReference = verificationReference, LinkedByUserId = actor.Id });
            Touch(profile);
            await QueueAsync(db, employeeId, "accounts:" + profile.Version, "AccountChange", "Verify the linked identity and actual account access.");
            Audit(db, employeeId, "AccountLinked", actor.Id, verificationReference, new { accountId });
            return true;
        });

    public async Task<IReadOnlyList<EmployeeAccountOption>> AccountOptionsAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesManage);
        return await db.Users.AsNoTracking().Where(x => !db.EmployeeAccountLinks.Any(l => l.UserId == x.Id))
            .OrderBy(x => x.Email).Select(x => new EmployeeAccountOption(x.Id, x.Email ?? x.UserName ?? x.Id)).ToListAsync();
    }

    public async Task<int> StartReviewAsync(int employeeId, string expectedProfileVersion, string reason, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == employeeId);
            Expect(profile.Version, expectedProfileVersion);
            if (profile.EmploymentStatus == "Inactive") throw new ValidationException("Inactive employees retain history; they are not in the current review population.");
            reason = Required(reason, "A review reason");
            var draft = await db.EmployeeComplianceReviews.SingleOrDefaultAsync(x => x.EmployeeProfileId == employeeId && x.Status == "Draft");
            if (draft?.ProfileVersion == profile.Version) return draft.Id;
            if (draft is not null) { draft.Status = "Superseded"; draft.Version = NewVersion(); }
            var review = new EmployeeComplianceReview { EmployeeProfileId = employeeId, ProfileVersion = profile.Version, ProfileSnapshotJson = Serialize(profile), PreparedByUserId = actor.Id };
            db.EmployeeComplianceReviews.Add(review);
            Touch(profile, changeVersion: false);
            await db.SaveChangesAsync();
            Audit(db, employeeId, "ReviewStarted", actor.Id, reason, new { review.Id, profile.Version });
            return review.Id;
        });

    public async Task RecordCheckAsync(int reviewId, string expectedVersion, EmployeeCheckEdit edit, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            var (profile, review) = await DraftAsync(db, reviewId, expectedVersion);
            Allowed(edit.Kind, EmployeeComplianceValues.CheckKinds, "check kind");
            Allowed(edit.Outcome, EmployeeComplianceValues.CheckOutcomes, "outcome");
            Allowed(edit.PerformerType, ["Manual", "Codex"], "performer");
            var when = ActualTime(edit.PerformedAtLocal);
            if (edit.Kind == "TFS" && edit.Outcome == "Satisfied" || edit.Kind != "TFS" && edit.Outcome is "NoMatch" or "FalsePositive" or "ConfirmedDesignation")
                throw new ValidationException("Choose an outcome appropriate to this check type.");
            var previous = await db.EmployeeComplianceChecks.Where(x => x.EmployeeComplianceReviewId == reviewId && x.Kind == edit.Kind).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            if (previous is null)
            {
                var earlier = await (from priorCheck in db.EmployeeComplianceChecks
                                     join oldReview in db.EmployeeComplianceReviews on priorCheck.EmployeeComplianceReviewId equals oldReview.Id
                                     where oldReview.EmployeeProfileId == profile.Id && priorCheck.Kind == edit.Kind
                                     orderby priorCheck.Id descending select priorCheck).FirstOrDefaultAsync();
                if (earlier?.Outcome is "Unresolved" or "SourceFailed" or "Concern" or "ConfirmedDesignation") previous = earlier;
            }
            if (previous is not null && edit.SupersedesCheckId != previous.Id) throw new ValidationException("Identify the current check being superseded; history is retained.");
            if (previous is null && edit.SupersedesCheckId.HasValue) throw new ValidationException("The superseded check is not part of this review.");
            if (previous?.Outcome == "ConfirmedDesignation") throw new ValidationException("A confirmed designation cannot be cleared by an ordinary replacement check. Follow the restriction and reporting procedure.");
            if (edit.Kind == "TFS" && edit.Outcome is "NoMatch" or "FalsePositive" && await HasDesignationAsync(db, profile.Id))
                throw new ValidationException("A confirmed designation is retained in this employee's history. An ordinary new review cannot clear it.");
            if (edit.EmployeeTfsBatchId.HasValue && (edit.Kind != "TFS" || !await db.EmployeeComplianceTasks.AnyAsync(x => x.EmployeeTfsBatchId == edit.EmployeeTfsBatchId && x.EmployeeProfileId == profile.Id)))
                throw new ValidationException("The TFS batch does not include this employee.");
            var sourceUrl = SafeUrl(edit.SourceUrl);
            if (edit.Kind == "TFS" && sourceUrl is not null)
                ValidateTfsSource(sourceUrl);
            if (edit.Kind == "TFS" && (string.IsNullOrWhiteSpace(edit.ListVersion) || sourceUrl is null || string.IsNullOrWhiteSpace(edit.IdentifierScope)))
                throw new ValidationException("TFS checks require the actual official source URL, list/version and identifier/alias scope.");
            if (edit.EmployeeTfsBatchId.HasValue)
            {
                var batch = await db.EmployeeTfsBatches.SingleAsync(x => x.Id == edit.EmployeeTfsBatchId);
                if (batch.SourceVersion != edit.ListVersion || batch.SourceUrl != sourceUrl) throw new ValidationException("The check source/version must match the selected batch.");
            }
            var path = string.IsNullOrWhiteSpace(edit.EvidencePath) ? null : edit.EvidencePath.Trim();
            var check = new EmployeeComplianceCheck
            {
                EmployeeComplianceReviewId = reviewId, Kind = edit.Kind, Outcome = edit.Outcome,
                PerformerType = edit.PerformerType, Performer = edit.PerformerType == "Codex" ? "Codex" : actor.Email ?? actor.UserName ?? actor.Id,
                PerformedAtUtc = when, RecordedByUserId = actor.Id,
                SourceReference = Required(edit.SourceReference, "The actual evidence/source reference"), SourceUrl = sourceUrl,
                ListVersion = edit.ListVersion?.Trim(), IdentifierScope = edit.IdentifierScope.Trim(),
                Finding = Required(edit.Finding, "The evidenced finding or source failure"), Limitations = edit.Limitations.Trim(),
                EvidencePath = path, EvidenceSha256 = path is null ? null : await files.HashAsync(path),
                SupersedesCheckId = edit.SupersedesCheckId, EmployeeTfsBatchId = edit.EmployeeTfsBatchId
            };
            db.EmployeeComplianceChecks.Add(check);
            review.Version = NewVersion();
            if (check.Outcome is "ConfirmedDesignation" or "Concern" or "Unresolved" or "SourceFailed")
                await QueueAsync(db, profile.Id, "check:" + review.Version, "ScreeningFollowUp", "Unresolved or adverse employee check: retain evidence and apply the relevant restrictions/escalation.");
            Audit(db, profile.Id, "CheckRecorded", actor.Id, check.Finding, check);
            return true;
        });

    public async Task RecordAccessAsync(int reviewId, string expectedVersion, EmployeeAccessEdit edit, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            var (profile, review) = await DraftAsync(db, reviewId, expectedVersion);
            Allowed(edit.Kind, ["KCAS", "External"], "access type");
            var scope = await AccountScopeAsync(db, profile.Id);
            var confirmation = new EmployeeAccessConfirmation
            {
                EmployeeComplianceReviewId = reviewId, Kind = edit.Kind,
                SystemAndScope = Required(edit.SystemAndScope, "The systems and scope checked"),
                ApprovedScope = Required(edit.ApprovedScope, "The authorised duties/access scope"),
                ActualScope = Required(edit.ActualScope, "The actual access found (including no access where verified)"),
                ActionConfirmation = Required(edit.ActionConfirmation, "The actual responsible-owner action or no-change confirmation"),
                IsAligned = edit.IsAligned, VerifiedAtUtc = ActualTime(edit.VerifiedAtLocal),
                VerifiedBy = actor.Email ?? actor.UserName ?? actor.Id,
                EvidenceReference = Required(edit.EvidenceReference, "The verification evidence reference"),
                AccountScopeJson = edit.Kind == "KCAS" ? scope : "",
                RecordedByUserId = actor.Id
            };
            db.EmployeeAccessConfirmations.Add(confirmation);
            review.Version = NewVersion();
            Audit(db, profile.Id, "AccessVerified", actor.Id, confirmation.ActionConfirmation, confirmation);
            return true;
        });

    public async Task DecideAsync(int reviewId, string expectedVersion, string decision, string reason, string followUp, int? months, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesReview, principal, async (db, actor) =>
        {
            var (profile, review) = await DraftAsync(db, reviewId, expectedVersion);
            Allowed(decision, EmployeeComplianceValues.DecisionTypes, "decision");
            reason = Required(reason, "An evidenced management decision reason");
            var reviewerLink = await db.EmployeeAccountLinks.SingleOrDefaultAsync(x => x.UserId == actor.Id)
                ?? throw new ValidationException("Link the reviewer's account to their verified employee identity first.");
            if (!await db.EmployeeProfiles.AnyAsync(x => x.Id == reviewerLink.EmployeeProfileId && x.EmploymentStatus == "Current"))
                throw new ValidationException("The reviewer must be a current authorised employee.");
            var checks = await db.EmployeeComplianceChecks.Where(x => x.EmployeeComplianceReviewId == reviewId).ToListAsync();
            var access = await db.EmployeeAccessConfirmations.Where(x => x.EmployeeComplianceReviewId == reviewId).ToListAsync();
            var involvedIds = checks.Select(x => x.RecordedByUserId).Concat(access.Select(x => x.RecordedByUserId)).Append(review.PreparedByUserId).ToList();
            var reviewerAccounts = await db.EmployeeAccountLinks.Where(x => x.EmployeeProfileId == reviewerLink.EmployeeProfileId).Select(x => x.UserId).ToListAsync();
            if (reviewerLink.EmployeeProfileId == profile.Id || involvedIds.Contains(actor.Id) ||
                reviewerAccounts.Any(involvedIds.Contains))
                throw new ValidationException("An uninvolved reviewer is required. Neither the employee nor a preparer/check recorder may approve through another linked account.");
            var scope = await AccountScopeAsync(db, profile.Id);
            var blockers = await BlockersAsync(db, profile, review, checks, access, scope);
            if (decision == "Approved")
            {
                if (blockers.Count > 0) throw new ValidationException(string.Join(" ", blockers));
                if (months is null or < 1 or > 60) throw new ValidationException("Approve a review interval from 1 to 60 months.");
                if (checks.Any(x => x.Outcome == "ConfirmedDesignation")) throw new ValidationException("A confirmed designation cannot be approved or overridden.");
            }
            else if (string.IsNullOrWhiteSpace(followUp)) throw new ValidationException("Record the actual restrictions and follow-up for an incomplete or restricted review.");
            var currentChecks = checks.OrderByDescending(x => x.Id).GroupBy(x => x.Kind).Select(g => g.First()).ToList();
            foreach (var check in currentChecks.Where(x => x.EvidencePath is not null))
            {
                if (await files.HashAsync(check.EvidencePath!) != check.EvidenceSha256)
                    throw new ValidationException("A linked evidence file changed after its check; record a fresh check before deciding.");
            }
            var now = DateTime.UtcNow;
            var record = new EmployeeReviewDecision
            {
                EmployeeComplianceReviewId = reviewId, Decision = decision, Reason = reason,
                RestrictionsAndFollowUp = followUp.Trim(), ApprovedReviewMonths = decision == "Approved" ? months : null,
                ReviewerUserId = actor.Id, ReviewerName = actor.Email ?? actor.UserName ?? actor.Id,
                ReviewerEmployeeProfileId = reviewerLink.EmployeeProfileId,
                EvidenceSnapshotJson = Serialize(new { profile, review, checks, access, scope, blockers,
                    Evidence = await db.EmployeeEvidenceDocuments.Where(x => x.EmployeeProfileId == profile.Id).ToListAsync() }), DecidedAtUtc = now
            };
            db.EmployeeReviewDecisions.Add(record);
            review.Status = decision;
            review.Version = NewVersion();
            if (decision == "Approved")
            {
                review.CompletedAtUtc = now;
                review.NextReviewDate = DateOnly.FromDateTime(now.ToLocalTime()).AddMonths(months!.Value);
                var tasks = await db.EmployeeComplianceTasks.Where(x => x.EmployeeProfileId == profile.Id && x.Status != "Closed").ToListAsync();
                foreach (var task in tasks)
                {
                    if (task.EmployeeTfsBatchId.HasValue && !checks.Any(x => x.EmployeeTfsBatchId == task.EmployeeTfsBatchId && ClearTfs(x))) continue;
                    task.Status = "Closed"; task.ClosedAtUtc = now;
                }
            }
            else await QueueAsync(db, profile.Id, "decision:" + review.Version, "ManagementFollowUp", followUp);
            Audit(db, profile.Id, "DecisionRecorded", actor.Id, reason, record);
            return true;
        });

    public async Task<int> CreateTfsBatchAsync(string sourceVersion, string sourceUrl, string reason, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            sourceVersion = Required(sourceVersion, "The official list/version reference", 191);
            sourceUrl = SafeUrl(sourceUrl) ?? throw new ValidationException("An official TFS source URL is required.");
            ValidateTfsSource(sourceUrl);
            reason = Required(reason, "The list-update reason");
            var existing = await db.EmployeeTfsBatches.SingleOrDefaultAsync(x => x.SourceVersion == sourceVersion);
            if (existing is not null)
            {
                if (existing.SourceUrl != sourceUrl) throw new ValidationException("That version was already registered with a different source.");
                return existing.Id;
            }
            var batch = new EmployeeTfsBatch { SourceVersion = sourceVersion, SourceUrl = sourceUrl, Reason = reason, CreatedByUserId = actor.Id };
            db.EmployeeTfsBatches.Add(batch);
            await db.SaveChangesAsync();
            var profiles = await db.EmployeeProfiles.Where(x => x.EmploymentStatus == "Current").ToListAsync();
            foreach (var profile in profiles)
            {
                await QueueAsync(db, profile.Id, "tfs:" + batch.Id, "TfsListUpdate", reason, batchId: batch.Id);
                Audit(db, profile.Id, "TfsListUpdateQueued", actor.Id, reason, new { batch.Id, sourceVersion, sourceUrl });
            }
            return batch.Id;
        });

    public async Task<IReadOnlyList<EmployeeBatchRow>> BatchesAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var batches = await db.EmployeeTfsBatches.AsNoTracking().OrderByDescending(x => x.Id).ToListAsync();
        var tasks = await db.EmployeeComplianceTasks.AsNoTracking().Where(x => x.EmployeeTfsBatchId != null).ToListAsync();
        var checks = await db.EmployeeComplianceChecks.AsNoTracking().Where(x => x.EmployeeTfsBatchId != null).OrderByDescending(x => x.Id).ToListAsync();
        var reviews = await db.EmployeeComplianceReviews.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.EmployeeProfileId);
        return batches.Select(batch =>
        {
            var memberTasks = tasks.Where(t => t.EmployeeTfsBatchId == batch.Id).ToList();
            var results = checks.Where(c => c.EmployeeTfsBatchId == batch.Id).GroupBy(c => reviews[c.EmployeeComplianceReviewId]).Select(g => g.First()).ToList();
            var clear = results.Count(ClearTfs);
            return new EmployeeBatchRow(batch, memberTasks.Count, clear, memberTasks.Count - clear, results.Count(c => !ClearTfs(c)));
        }).ToList();
    }

    public async Task TriggerReviewAsync(int employeeId, string expectedProfileVersion, string reason, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == employeeId);
            Expect(profile.Version, expectedProfileVersion);
            if (profile.EmploymentStatus == "Inactive") throw new ValidationException("Inactive staff are not in the review population.");
            reason = Required(reason, "The material event or access change");
            Touch(profile);
            await QueueAsync(db, profile.Id, "event:" + profile.Version, "TriggerReview", reason);
            Audit(db, profile.Id, "EventReviewQueued", actor.Id, reason, new { profile.Version });
            return true;
        });

    public async Task<IReadOnlyList<EmployeeTaskRow>> TasksAsync(ClaimsPrincipal principal, bool includeClosed = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var rows = await (from task in db.EmployeeComplianceTasks.AsNoTracking()
                      where includeClosed || task.Status != "Closed"
                      join profile in db.EmployeeProfiles.AsNoTracking() on task.EmployeeProfileId equals profile.Id
                      orderby task.DueDate, task.Id
                      select new EmployeeTaskRow(task, profile.DisplayName)).ToListAsync();
        var labels = await UserNamesAsync(db);
        return rows.Select(row => row with { RecipientNames = string.Join("; ",
            (JsonSerializer.Deserialize<List<string>>(row.Task.RecipientUserIdsJson) ?? []).Select(id => labels.GetValueOrDefault(id, "Retained account " + id))),
            AcknowledgedByName = row.Task.AcknowledgedByUserId is null ? null : labels.GetValueOrDefault(row.Task.AcknowledgedByUserId, row.Task.AcknowledgedByUserId) }).ToList();
    }

    public async Task AcknowledgeAsync(int taskId, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesView, principal, async (db, actor) =>
        {
            var task = await db.EmployeeComplianceTasks.SingleAsync(x => x.Id == taskId);
            if (task.Status != "Open") return true;
            task.Status = "Acknowledged"; task.AcknowledgedAtUtc = DateTime.UtcNow; task.AcknowledgedByUserId = actor.Id;
            Audit(db, task.EmployeeProfileId, "TaskAcknowledged", actor.Id, "In-app employee task acknowledged; not a completed review.", new { task.Id });
            return true;
        });

    public async Task<int> RefreshDueTasksAsync(ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesView, principal, (db, _) => RefreshDueAsync(db));

    internal async Task<int> RefreshDueTasksForJobAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var count = await RefreshDueAsync(db);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return count;
    }

    private static async Task<int> RefreshDueAsync(ApplicationDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var profiles = await db.EmployeeProfiles.Where(x => x.EmploymentStatus != "Inactive").ToListAsync();
        var reviews = await db.EmployeeComplianceReviews.Where(x => x.Status == "Approved").ToListAsync();
        var count = 0;
        foreach (var profile in profiles)
        {
            var review = reviews.Where(x => x.EmployeeProfileId == profile.Id).OrderByDescending(x => x.Id).FirstOrDefault();
            if (review?.NextReviewDate <= today)
                count += await QueueAsync(db, profile.Id, "due:" + review.Id, "PeriodicReview", "The approved employee review interval is due.", review.NextReviewDate);
        }
        return count;
    }

    public async Task<int> ImportBaselineAsync(EmployeeBaseline baseline, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            if (baseline.Employees.Count is < 1 or > 100) throw new ValidationException("Import between 1 and 100 sourced employee baselines.");
            var source = Required(baseline.SourceReference, "The roster source reference");
            if (baseline.Employees.GroupBy(x => x.Profile.LegalName.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new ValidationException("The baseline contains duplicate legal names.");
            var created = 0;
            foreach (var entry in baseline.Employees)
            {
                ValidateProfile(entry.Profile);
                if (await db.EmployeeProfiles.AnyAsync(x => x.LegalName == entry.Profile.LegalName.Trim())) continue;
                var profile = new EmployeeProfile { CreatedByUserId = actor.Id };
                ApplyProfile(profile, entry.Profile);
                db.EmployeeProfiles.Add(profile);
                await db.SaveChangesAsync();
                foreach (var email in entry.AccountEmails)
                {
                    if (!await HasPermissionAsync(db, actor.Id, KcasPermissions.SecurityManage)) throw new UnauthorizedAccessException("Baseline account links require Security.Manage.");
                    var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
                    if (user is null) continue;
                    if (await db.EmployeeAccountLinks.AnyAsync(x => x.UserId == user.Id)) throw new ValidationException("A baseline account is already linked to another employee.");
                    db.EmployeeAccountLinks.Add(new() { EmployeeProfileId = profile.Id, UserId = user.Id, VerificationReference = source, LinkedByUserId = actor.Id });
                }
                foreach (var document in entry.Evidence)
                    db.EmployeeEvidenceDocuments.Add(await MakeEvidenceAsync(profile.Id, document, actor.Id));
                await QueueAsync(db, profile.Id, "initial:" + profile.Version, "InitialReview", "Sourced baseline imported; actual screening, access verification and management decision pending.");
                Audit(db, profile.Id, "BaselineImported", actor.Id, source, profile);
                created++;
            }
            return created;
        });

    public async Task<(Stream Stream, string FileName)?> OpenEvidenceAsync(int checkId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var check = await db.EmployeeComplianceChecks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == checkId);
        if (check?.EvidencePath is null) return null;
        var path = files.Resolve(check.EvidencePath);
        if (!File.Exists(path)) return null;
        var stream = File.OpenRead(path);
        if (Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)) != check.EvidenceSha256)
        {
            await stream.DisposeAsync();
            throw new ValidationException("The evidence file has changed since this check; re-verify it.");
        }
        stream.Position = 0;
        return (stream, Path.GetFileName(path));
    }

    public async Task<IReadOnlyList<EmployeeEvidenceDocument>> EvidenceAsync(int employeeId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        return await db.EmployeeEvidenceDocuments.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderBy(x => x.Category).ThenBy(x => x.Title).ToListAsync();
    }

    public async Task LinkEvidenceAsync(int employeeId, string expectedProfileVersion, EmployeeEvidenceEdit edit, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.EmployeesManage, principal, async (db, actor) =>
        {
            var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == employeeId);
            Expect(profile.Version, expectedProfileVersion);
            var record = await MakeEvidenceAsync(employeeId, edit, actor.Id);
            db.EmployeeEvidenceDocuments.Add(record);
            Touch(profile);
            await QueueAsync(db, profile.Id, "evidence:" + profile.Version, "EvidenceChange", "Employee source evidence linked; verify applicability in the review.");
            Audit(db, employeeId, "EvidenceLinked", actor.Id, record.SourceNote, record);
            return true;
        });

    private async Task<EmployeeEvidenceDocument> MakeEvidenceAsync(int employeeId, EmployeeEvidenceEdit edit, string actorId)
    {
        Allowed(edit.Category, ["Identity", "Appointment", "Competence", "Integrity", "Training", "TFS", "Access", "Other"], "evidence category");
        return new() { EmployeeProfileId = employeeId, Title = Required(edit.Title, "Evidence title", 191),
            Category = edit.Category, EvidencePath = Required(edit.EvidencePath, "Evidence path", 1024),
            EvidenceSha256 = await files.HashAsync(edit.EvidencePath), SourceNote = Required(edit.SourceNote, "Source/applicability note"), LinkedByUserId = actorId };
    }

    public async Task<(Stream Stream, string FileName)?> OpenDocumentAsync(int documentId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.EmployeesView);
        var document = await db.EmployeeEvidenceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId);
        if (document is null) return null;
        var path = files.Resolve(document.EvidencePath);
        if (!File.Exists(path)) return null;
        if (await files.HashAsync(document.EvidencePath) != document.EvidenceSha256) throw new ValidationException("This source document changed after linking; retain the historical reference and link the revised document.");
        return (File.OpenRead(path), Path.GetFileName(path));
    }

    public async Task RequireSensitiveAccessAsync(string userId, string roleName, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, principal, KcasPermissions.SecurityManage);
        await CheckAccessGrantAsync(db, userId, roleName);
    }

    private static async Task CheckAccessGrantAsync(ApplicationDbContext db, string userId, string roleName)
    {
        var link = await db.EmployeeAccountLinks.SingleOrDefaultAsync(x => x.UserId == userId);
        if (link is null) return; // Non-employee accounts retain the established account-approval workflow.
        var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == link.EmployeeProfileId);
        if (profile.EmploymentStatus == "Inactive") throw new ValidationException("Do not grant access to an inactive employee.");
        if (await HasDesignationAsync(db, profile.Id)) throw new ValidationException("Confirmed designation: new access cannot be granted through an ordinary administrator override.");
        // Existing managers can receive the narrow decision role without a circular
        // demand that they first approve their own baseline. No access is revoked.
        if (profile.EmploymentStatus == "Current" && roleName == KcasRoles.EmployeeReviewer && !string.IsNullOrWhiteSpace(profile.IdentityReference)) return;
        var review = await db.EmployeeComplianceReviews.Where(x => x.EmployeeProfileId == profile.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var openTasks = await db.EmployeeComplianceTasks.AnyAsync(x => x.EmployeeProfileId == profile.Id && x.Status != "Closed");
        if (review?.Status != "Approved" || review.ProfileVersion != profile.Version || openTasks || review.NextReviewDate <= DateOnly.FromDateTime(DateTime.Today))
            throw new ValidationException("Complete the employee review and uninvolved decision before granting new sensitive access. Existing access is not automatically revoked.");
    }

    public async Task ChangeAccountRoleAsync(string userId, string roleName, bool grant, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.SecurityManage, principal, async (db, actor) =>
        {
            var role = await db.Roles.SingleAsync(x => x.Name == roleName);
            var user = await db.Users.SingleAsync(x => x.Id == userId);
            var assignment = await db.UserRoles.SingleOrDefaultAsync(x => x.UserId == userId && x.RoleId == role.Id);
            if (grant && assignment is null)
            {
                await CheckAccessGrantAsync(db, userId, roleName);
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role.Id });
            }
            else if (!grant && assignment is not null) db.UserRoles.Remove(assignment);
            else return false;
            user.SecurityStamp = NewVersion();
            user.ConcurrencyStamp = NewVersion();
            await db.SaveChangesAsync();
            var link = await db.EmployeeAccountLinks.SingleOrDefaultAsync(x => x.UserId == userId);
            if (link is not null)
            {
                var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == link.EmployeeProfileId);
                Touch(profile);
                var reason = $"Role {roleName} {(grant ? "granted" : "removed")} through Security; actual employee account access changed.";
                await QueueAsync(db, profile.Id, "permission:" + profile.Version, "AccountChange", reason);
                Audit(db, profile.Id, "PermissionChangeRecorded", actor.Id, reason, new { userId, roleName, grant, Scope = await AccountScopeAsync(db, profile.Id) });
            }
            return true;
        });

    public async Task ActivateAccountAsync(string userId, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.SecurityManage, principal, async (db, actor) =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == userId);
            if (user.IsApproved) return false;
            await CheckAccessGrantAsync(db, userId, "AccountActivation");
            user.IsApproved = true; user.ApprovedAtUtc = DateTime.UtcNow; user.ApprovedByUserId = actor.Id;
            user.SecurityStamp = NewVersion(); user.ConcurrencyStamp = NewVersion();
            var link = await db.EmployeeAccountLinks.SingleOrDefaultAsync(x => x.UserId == userId);
            if (link is not null)
            {
                var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == link.EmployeeProfileId);
                Touch(profile);
                await QueueAsync(db, profile.Id, "activate:" + profile.Version, "AccountChange", "Account activated; verify actual access.");
                Audit(db, profile.Id, "AccountActivated", actor.Id, "Employee-linked account activated through Security.", new { userId });
            }
            return true;
        });

    public async Task RecordPermissionChangeAsync(string userId, string reason, ClaimsPrincipal principal)
        => await WriteAsync(KcasPermissions.SecurityManage, principal, async (db, actor) =>
        {
            var link = await db.EmployeeAccountLinks.SingleOrDefaultAsync(x => x.UserId == userId);
            if (link is null) return false;
            var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == link.EmployeeProfileId);
            Touch(profile);
            await QueueAsync(db, profile.Id, "permission:" + profile.Version, "AccountChange", reason);
            Audit(db, profile.Id, "PermissionChangeRecorded", actor.Id, reason, new { userId, Scope = await AccountScopeAsync(db, profile.Id) });
            return true;
        });

    private static async Task<List<string>> BlockersAsync(ApplicationDbContext db, EmployeeProfile profile, EmployeeComplianceReview review,
        IEnumerable<EmployeeComplianceCheck> allChecks, IEnumerable<EmployeeAccessConfirmation> allAccess, string scope)
    {
        var blockers = new List<string>();
        if (review.ProfileVersion != profile.Version) blockers.Add("The employee duties/identity changed; start a fresh review against the current profile.");
        if (profile.EmploymentStatus == "Inactive") blockers.Add("The employee is inactive.");
        if (string.IsNullOrWhiteSpace(profile.IdentityReference)) blockers.Add("Record the verified identity source, including relevant aliases.");
        var checks = allChecks.Where(x => x.EmployeeComplianceReviewId == review.Id).OrderByDescending(x => x.Id).GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.First());
        var required = new List<string> { "Identity", "Competence", "Integrity", "TFS" };
        if (profile.RequireTraining) required.Add("Training");
        if (profile.RequireRegulatedCompetence) required.Add("RegulatedCompetence");
        if (profile.RequireAdditionalCheck) required.Add("Additional");
        foreach (var kind in required)
        {
            if (!checks.TryGetValue(kind, out var check) || (kind == "TFS" ? !ClearTfs(check) : check.Outcome != "Satisfied"))
                blockers.Add($"Complete the evidenced {EmployeeComplianceValues.CheckLabel(kind)} check.");
        }
        if (await HasDesignationAsync(db, profile.Id)) blockers.Add("Confirmed TFS designation: restrictions/reporting cannot be overridden.");
        if (checks.Values.Any(x => x.Outcome is "Unresolved" or "SourceFailed" or "Concern")) blockers.Add("Resolve or escalate the outstanding check findings; a failed source is not clearance.");
        var historyChecks = await (from check in db.EmployeeComplianceChecks
                                   join oldReview in db.EmployeeComplianceReviews on check.EmployeeComplianceReviewId equals oldReview.Id
                                   where oldReview.EmployeeProfileId == profile.Id select check).ToListAsync();
        var superseded = historyChecks.Where(x => x.SupersedesCheckId.HasValue).Select(x => x.SupersedesCheckId!.Value).ToHashSet();
        if (historyChecks.Any(x => x.EmployeeComplianceReviewId != review.Id && !superseded.Contains(x.Id) && x.Outcome is "Unresolved" or "SourceFailed" or "Concern"))
            blockers.Add("An earlier unresolved finding remains; explicitly supersede it with evidenced resolution rather than silently starting over.");
        var latestBatch = await db.EmployeeTfsBatches.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        if (latestBatch is not null && (!checks.TryGetValue("TFS", out var tfs) || tfs.ListVersion != latestBatch.SourceVersion || tfs.SourceUrl != latestBatch.SourceUrl))
            blockers.Add("TFS evidence must cover the latest recorded official list/version.");
        var access = allAccess.Where(x => x.EmployeeComplianceReviewId == review.Id).OrderByDescending(x => x.Id).GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.First());
        if (!access.TryGetValue("KCAS", out var kcas) || !kcas.IsAligned) blockers.Add("Verify actual KCAS account access against the authorised duties, including no account where applicable.");
        else if (kcas.AccountScopeJson != scope) blockers.Add("KCAS account approval/roles/permissions changed after verification; verify access again.");
        if (!access.TryGetValue("External", out var external) || !external.IsAligned) blockers.Add("Record the actual external-system/shared-folder access verification and responsible-owner confirmation.");
        return blockers;
    }

    private static bool ClearTfs(EmployeeComplianceCheck check) => check.Outcome is "NoMatch" or "FalsePositive";
    private static Task<bool> HasDesignationAsync(ApplicationDbContext db, int employeeId)
        => (from check in db.EmployeeComplianceChecks
            join review in db.EmployeeComplianceReviews on check.EmployeeComplianceReviewId equals review.Id
            where review.EmployeeProfileId == employeeId && check.Outcome == "ConfirmedDesignation" select check).AnyAsync();
    private static async Task<string> AccountScopeAsync(ApplicationDbContext db, int employeeId)
    {
        var users = await db.Users.AsNoTracking().Where(x => db.EmployeeAccountLinks.Any(l => l.EmployeeProfileId == employeeId && l.UserId == x.Id))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Email, x.IsApproved }).ToListAsync();
        var roles = await (from userRole in db.UserRoles where db.EmployeeAccountLinks.Any(l => l.EmployeeProfileId == employeeId && l.UserId == userRole.UserId)
                           join role in db.Roles on userRole.RoleId equals role.Id
                           select new { userRole.UserId, role.Id, role.Name }).ToListAsync();
        var permissions = await db.RoleClaims.AsNoTracking().Where(x => x.ClaimType == KcasClaimTypes.Permission &&
                db.UserRoles.Any(r => r.RoleId == x.RoleId && db.EmployeeAccountLinks.Any(l => l.EmployeeProfileId == employeeId && l.UserId == r.UserId)))
            .Select(x => new { x.RoleId, x.ClaimValue }).ToListAsync();
        return Serialize(new { Users = users, Roles = roles.OrderBy(x => x.UserId).ThenBy(x => x.Id), Permissions = permissions.OrderBy(x => x.RoleId).ThenBy(x => x.ClaimValue) });
    }

    private static async Task<(EmployeeProfile Profile, EmployeeComplianceReview Review)> DraftAsync(ApplicationDbContext db, int id, string version)
    {
        var review = await db.EmployeeComplianceReviews.SingleAsync(x => x.Id == id);
        Expect(review.Version, version);
        if (review.Status != "Draft") throw new ValidationException("This review is frozen. Start a new review rather than changing its checks or decision.");
        var profile = await db.EmployeeProfiles.SingleAsync(x => x.Id == review.EmployeeProfileId);
        if (profile.Version != review.ProfileVersion) throw new ValidationException("The employee profile changed. Start a fresh review.");
        return (profile, review);
    }

    private async Task<T> WriteAsync<T>(string permission, ClaimsPrincipal principal, Func<ApplicationDbContext, ApplicationUser, Task<T>> action)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ActorAsync(db, principal, permission);
        var result = await action(db, actor);
        try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ValidationException("This employee record changed while you were working. Reload and review the current version."); }
        return result;
    }

    private static async Task<ApplicationUser> ActorAsync(ApplicationDbContext db, ClaimsPrincipal principal, string permission)
    {
        var id = UserId(principal);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (principal.Identity?.IsAuthenticated != true || user?.IsApproved != true || !await HasPermissionAsync(db, id, permission))
            throw new UnauthorizedAccessException("Current personnel-compliance permission and an approved signed-in account are required.");
        return user;
    }

    private static string UserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private static async Task<Dictionary<string, string>> UserNamesAsync(ApplicationDbContext db)
    {
        var names = await db.Users.AsNoTracking().Select(x => new { x.Id, Label = x.Email ?? x.UserName ?? x.Id }).ToDictionaryAsync(x => x.Id, x => x.Label);
        foreach (var record in await db.EmployeeTransferRecords.AsNoTracking().Where(x => x.Direction == "Incoming").OrderBy(x => x.Id).ToListAsync())
            foreach (var (id, label) in JsonSerializer.Deserialize<Dictionary<string, string>>(record.ActorNamesJson) ?? []) names.TryAdd(id, label);
        return names;
    }
    private static Task<bool> HasPermissionAsync(ApplicationDbContext db, string id, string permission)
        => (from role in db.UserRoles where role.UserId == id
            join claim in db.RoleClaims on role.RoleId equals claim.RoleId
            where claim.ClaimType == KcasClaimTypes.Permission && claim.ClaimValue == permission select role).AnyAsync();

    private static async Task<int> QueueAsync(ApplicationDbContext db, int employeeId, string key, string kind, string reason, DateOnly? due = null, int? batchId = null)
    {
        if (await db.EmployeeComplianceTasks.AnyAsync(x => x.EmployeeProfileId == employeeId && x.TriggerKey == key)) return 0;
        var recipients = await (from user in db.Users where user.IsApproved
                                join role in db.UserRoles on user.Id equals role.UserId
                                join claim in db.RoleClaims on role.RoleId equals claim.RoleId
                                where claim.ClaimType == KcasClaimTypes.Permission && (claim.ClaimValue == KcasPermissions.EmployeesManage || claim.ClaimValue == KcasPermissions.EmployeesReview)
                                select user.Id).Distinct().OrderBy(x => x).ToListAsync();
        db.EmployeeComplianceTasks.Add(new() { EmployeeProfileId = employeeId, TriggerKey = key, Kind = kind, Reason = reason, DueDate = due,
            RecipientUserIdsJson = Serialize(recipients), EmployeeTfsBatchId = batchId });
        return 1;
    }

    private static void Audit(ApplicationDbContext db, int employeeId, string action, string actor, string reason, object snapshot)
        => db.EmployeeComplianceAuditEvents.Add(new() { EmployeeProfileId = employeeId, Action = action, UserId = actor, Reason = reason, SnapshotJson = Serialize(snapshot) });
    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string NewVersion() => Guid.NewGuid().ToString("N");
    private static void Touch(EmployeeProfile profile, bool changeVersion = true) { profile.UpdatedAtUtc = DateTime.UtcNow; if (changeVersion) profile.Version = NewVersion(); }
    private static void Expect(string actual, string? expected) { if (actual != expected) throw new ValidationException("The employee record changed. Reload before saving or deciding."); }
    private static string Required(string? value, string label, int max = 20000)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ValidationException($"{label} is required (maximum {max} characters).");
        return value.Trim();
    }
    private static void Allowed(string value, IEnumerable<string> allowed, string label) { if (!allowed.Contains(value)) throw new ValidationException($"Invalid {label}."); }
    private static DateTime ActualTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        if (utc > DateTime.UtcNow.AddMinutes(1) || utc.Year < 2000) throw new ValidationException("Use the actual check/verification time, not a future or missing date.");
        return utc;
    }
    private static string? SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 1024 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ValidationException("Use an absolute HTTPS source URL without embedded credentials.");
        return value.Trim();
    }
    private static void ValidateTfsSource(string sourceUrl)
    {
        var host = new Uri(sourceUrl).Host;
        if (!(host.Equals("fic.gov.za", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".fic.gov.za", StringComparison.OrdinalIgnoreCase) ||
              host.Equals("un.org", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".un.org", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Use the actual official FIC or UN targeted-financial-sanctions source.");
    }
    private static void ValidateProfile(EmployeeProfileEdit edit)
    {
        Required(edit.DisplayName, "Display name", 191); Required(edit.LegalName, "Legal name", 191);
        Required(edit.Responsibilities, "Sourced responsibilities"); Required(edit.AuthorityLimits, "Authority limits");
        Required(edit.SourceReference, "The profile source"); Required(edit.RiskRationale, "The role-risk rationale"); Required(edit.SelectedChecks, "The proportionate check selection");
        Allowed(edit.EmploymentStatus, EmployeeComplianceValues.EmploymentStatuses, "employment status");
        Allowed(edit.RoleExposure, EmployeeComplianceValues.RoleExposures, "role exposure");
        if (edit.ProposedReviewMonths is < 1 or > 60) throw new ValidationException("Propose a review interval from 1 to 60 months.");
        if (edit.EmploymentStart.HasValue && edit.EmploymentEnd < edit.EmploymentStart) throw new ValidationException("Employment end cannot precede employment start.");
        if (edit.Email?.Length > 191) throw new ValidationException("Email is too long.");
    }
    private static void ApplyProfile(EmployeeProfile p, EmployeeProfileEdit e)
    {
        p.DisplayName = e.DisplayName.Trim(); p.LegalName = e.LegalName.Trim(); p.Aliases = e.Aliases.Trim(); p.Email = e.Email?.Trim();
        p.EmploymentStatus = e.EmploymentStatus; p.IdentityReference = e.IdentityReference.Trim(); p.Responsibilities = e.Responsibilities.Trim();
        p.AuthorityLimits = e.AuthorityLimits.Trim(); p.SourceReference = e.SourceReference.Trim(); p.RoleExposure = e.RoleExposure;
        p.RiskRationale = e.RiskRationale.Trim(); p.SelectedChecks = e.SelectedChecks.Trim(); p.RequireTraining = e.RequireTraining;
        p.RequireRegulatedCompetence = e.RequireRegulatedCompetence; p.ProposedReviewMonths = e.ProposedReviewMonths;
        p.RequireAdditionalCheck = e.RequireAdditionalCheck;
        p.EmploymentStart = e.EmploymentStart; p.EmploymentEnd = e.EmploymentEnd; p.ExternalAccessScope = e.ExternalAccessScope.Trim();
    }
}
