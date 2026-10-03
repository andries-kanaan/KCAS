using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientBraRiskReportService(IDbContextFactory<ApplicationDbContext> factory, ClientOnboardingService onboarding)
{
    public async Task<ClientBraRiskModel> LoadAsync(int clientId, ClaimsPrincipal principal, int? reportId = null)
    {
        var review = await onboarding.LoadAsync(clientId, principal);
        await using var db = await factory.CreateDbContextAsync();
        if (!await PermissionAsync(db, principal, KcasPermissions.RiskAssessmentsView)) throw new UnauthorizedAccessException("Current risk-assessment viewing permission is required.");
        var history = await db.ClientBraRiskReports.AsNoTracking().Where(x => x.ClientId == clientId).OrderByDescending(x => x.Id).ToListAsync();
        var selected = reportId is null ? history.FirstOrDefault() : history.SingleOrDefault(x => x.Id == reportId)
            ?? throw new KeyNotFoundException("Report not found for this client.");
        return new(review, selected, history, await CanPrepareAsync(db, principal));
    }
    public async Task<int> RecordAsync(int clientId, ClientBraRiskEdit edit, string reason, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await CanPrepareAsync(db, principal)) throw new UnauthorizedAccessException("Current risk-assessment preparation permission is required.");
        // Serialise this client's immutable report history, including the first report.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Clients WHERE Id = {clientId} FOR UPDATE");
        var review = await onboarding.LoadAsync(clientId, principal);
        if (edit.SourceContentHash != review.ChecksContentHash) throw new ValidationException("Client checks changed. Reload report preparation before saving.");
        var previousId = await db.ClientBraRiskReports.Where(x => x.ClientId == clientId).OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        if (edit.PreviousReportId != previousId) throw new ValidationException("A newer report was recorded. Reload before saving.");
        if (string.IsNullOrWhiteSpace(edit.BraReference) || edit.BraReference.Length > 20000 || string.IsNullOrWhiteSpace(reason) || reason.Length > 20000)
            throw new ValidationException("Record the exact BRA version/sections and an audit reason.");
        if (edit.PerformedBy is not ("Codex" or "Human reviewer")) throw new ValidationException("Select the actual review performer.");
        var actor = await db.Users.AsNoTracking().SingleAsync(x => x.Id == principal.FindFirstValue(ClaimTypes.NameIdentifier));
        var user = actor.Email ?? actor.UserName ?? actor.Id;
        var content = JsonSerializer.Deserialize<ClientBraRiskContent>(JsonSerializer.Serialize(edit.Content))!;
        var ids = content.Scenarios.SelectMany(x => x.Evidence).Select(x => x.EvidenceItemId).Distinct().ToList();
        var files = await db.ClientEvidenceItems.AsNoTracking().Where(x => x.ClientId == clientId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var root = await db.ClientEvidenceScanRoots.AsNoTracking().Where(x => x.IsActive).OrderByDescending(x => x.Id).Select(x => x.RootPath).FirstOrDefaultAsync();
        foreach (var row in content.Scenarios)
        {
            if ((row.ResidualLikelihood < row.InherentLikelihood || row.ResidualImpact < row.InherentImpact) && review.CheckBlockers.Count > 0)
                throw new ValidationException("Unresolved current check blockers prevent claiming a demonstrated risk reduction.");
            var retained = new List<ClientBraControlEvidence>();
            foreach (var link in row.Evidence)
            {
                if (!files.TryGetValue(link.EvidenceItemId, out var item) || item.Status != ClientEvidenceStatuses.Verified ||
                    item.SelectionStatus != ClientEvidenceSelectionStatuses.Current || !ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) ||
                    item.VerifiedDate is null || item.ExpiryDate < DateOnly.FromDateTime(DateTime.Today) || item.EscalationRequired)
                    throw new ValidationException("Controls must link current verified evidence for this client without an unresolved escalation.");
                if (!string.IsNullOrWhiteSpace(item.SourcePath) || !string.IsNullOrWhiteSpace(item.RelativePath))
                {
                    var path = ClientEvidenceFileResolver.ResolveExistingPath(item.SourcePath, item.RelativePath, item.FileName, review.Client.ClientFolder, root);
                    if (path is null || string.IsNullOrWhiteSpace(item.FileSha256)) throw new ValidationException("A linked control file is missing or lacks its verified fingerprint.");
                    await using var stream = File.OpenRead(path);
                    if (!Convert.ToHexString(await SHA256.HashDataAsync(stream)).Equals(item.FileSha256, StringComparison.OrdinalIgnoreCase))
                        throw new ValidationException("A linked control file changed since verification.");
                }
                retained.Add(new() { EvidenceItemId = item.Id, EvidenceKey = ClientReviewTransferService.EvidenceKey(item),
                    EvidenceType = item.EvidenceType, Title = item.Title, RelativePath = item.RelativePath, FileSha256 = item.FileSha256,
                    PerformedBy = item.ScreeningPerformedBy ?? item.Reviewer,
                    CheckedAtUtc = item.ScreeningReviewedAtUtc ?? item.OwnershipReviewedAtUtc, Findings = item.Notes });
            }
            row.Evidence = retained;
        }
        ClientBraRiskMethod.Validate(content);
        var report = new ClientBraRiskReport { ClientId = clientId, SourceContentHash = review.ChecksContentHash,
            BraReference = edit.BraReference.Trim(), PerformedBy = edit.PerformedBy == "Codex" ? "Codex" : user,
            RecordedBy = user, ContentJson = JsonSerializer.Serialize(content) };
        db.ClientBraRiskReports.Add(report);
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(new() { EntityType = nameof(ClientBraRiskReport), EntityId = report.Id,
            Action = "ProposedBraClientRiskRecorded", UserName = user, Reason = reason.Trim(),
            NewValueJson = JsonSerializer.Serialize(new { report.Id, report.ClientId, report.MethodVersion, report.PerformedBy, report.BraReference, report.SourceContentHash, content }) });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return report.Id;
    }
    private static Task<bool> CanPrepareAsync(ApplicationDbContext db, ClaimsPrincipal principal) => PermissionAsync(db, principal, KcasPermissions.RiskAssessmentsPrepare);
    private static Task<bool> PermissionAsync(ApplicationDbContext db, ClaimsPrincipal principal, string permission) =>
        (from user in db.Users where user.Id == principal.FindFirstValue(ClaimTypes.NameIdentifier) && user.IsApproved
            join link in db.UserRoles on user.Id equals link.UserId join claim in db.RoleClaims on link.RoleId equals claim.RoleId
            where claim.ClaimType == KcasClaimTypes.Permission && claim.ClaimValue == permission
            select user).AnyAsync();
}
