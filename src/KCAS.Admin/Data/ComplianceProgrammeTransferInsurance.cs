using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed partial class ComplianceProgrammeTransferService
{
    private async Task<List<InsurancePolicy>> ExportInsuranceAsync(CancellationToken ct)
    {
        var rows = await db.InsurancePolicies.AsNoTracking().Include(x => x.Documents).Include(x => x.Events).ToListAsync(ct);
        var files = new InsuranceFiles(configuration);
        foreach (var document in rows.SelectMany(x => x.Documents))
            if (await files.FingerprintAsync(document.RelativePath, ct) != document.Sha256)
                throw new ValidationException($"PI cover document changed since linking: {document.RelativePath}");
        return rows;
    }

    private async Task ValidateInsuranceAsync(List<InsurancePolicy>? rows, List<string> conflicts, CancellationToken ct)
    {
        if (rows is null) return; // Version-one packages did not carry insurance records.
        var files = new InsuranceFiles(configuration);
        var keys = new HashSet<Guid>();
        var years = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentOwners = await db.InsuranceDocuments.AsNoTracking().Select(x => new { x.TransferKey, x.InsurancePolicyId }).ToListAsync(ct);
        var eventOwners = await db.InsuranceEvents.AsNoTracking().Select(x => new { x.TransferKey, x.InsurancePolicyId }).ToListAsync(ct);
        foreach (var row in rows)
        {
            try
            {
                InsuranceService.Validate(row);
                if (row.TransferKey == Guid.Empty || !keys.Add(row.TransferKey) || !years.Add($"{row.PolicyNumber}|{row.StartDate}"))
                    throw new ValidationException("Duplicate or missing insurance transfer identity.");
                if (!InsuranceStages.All.Contains(row.Status) || row.Version?.Length != 32 || row.Documents is null || row.Events is null ||
                    row.Status is InsuranceStages.Accepted or InsuranceStages.Confirmed && row.EndDate < row.StartDate)
                    throw new ValidationException("Invalid insurance workflow state or insurance period.");
                if (row.Status == InsuranceStages.Confirmed && !row.Documents.Any(x => x.Kind is "PolicySchedule" or "InsurerConfirmation"))
                    throw new ValidationException("Confirmed insurance has no final schedule or insurer confirmation.");
                var docKeys = new HashSet<Guid>();
                foreach (var document in row.Documents)
                {
                    Validator.ValidateObject(document, new ValidationContext(document), true);
                    if (document.TransferKey == Guid.Empty || !docKeys.Add(document.TransferKey) || !InsuranceStages.DocumentKinds.Contains(document.Kind))
                        throw new ValidationException("Invalid PI document identity or document type.");
                    if (await files.FingerprintAsync(document.RelativePath, ct) != document.Sha256)
                        throw new ValidationException($"Mapped PI file differs from the reviewed copy: {document.RelativePath}");
                }
                if (row.Events.Any(x => x.TransferKey == Guid.Empty || (x.DocumentKey is Guid key && !docKeys.Contains(key))) ||
                    row.Events.Select(x => x.TransferKey).Distinct().Count() != row.Events.Count)
                    throw new ValidationException("Invalid PI event evidence references.");
                var target = await db.InsurancePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.TransferKey == row.TransferKey ||
                    (x.PolicyNumber == row.PolicyNumber && x.StartDate == row.StartDate), ct);
                var targetId = target?.Id ?? 0;
                var eventKeys = row.Events.Select(x => x.TransferKey).ToList();
                if (documentOwners.Any(x => docKeys.Contains(x.TransferKey) && x.InsurancePolicyId != targetId) ||
                    eventOwners.Any(x => eventKeys.Contains(x.TransferKey) && x.InsurancePolicyId != targetId))
                    throw new ValidationException("PI document or event belongs to another live policy.");
                if (target is not null && target.Version != row.Version &&
                    (target.UpdatedAtUtc >= row.UpdatedAtUtc || target.Status is InsuranceStages.Accepted or InsuranceStages.Confirmed))
                    throw new ValidationException("The live insurance record is newer or accepted; it cannot be overwritten by a different source snapshot.");
            }
            catch (Exception ex) when (ex is ValidationException or IOException or UnauthorizedAccessException)
            { conflicts.Add($"PI policy {row.PolicyNumber} ({row.StartDate.Year}): {ex.Message}"); }
        }
    }

    private async Task ImportInsuranceAsync(List<InsurancePolicy>? rows, string user, CancellationToken ct)
    {
        if (rows is null) return;
        foreach (var source in rows)
        {
            var target = await db.InsurancePolicies.Include(x => x.Documents).Include(x => x.Events).FirstOrDefaultAsync(x =>
                x.TransferKey == source.TransferKey || (x.PolicyNumber == source.PolicyNumber && x.StartDate == source.StartDate), ct);
            if (target is not null && target.Version == source.Version) continue;
            if (target is null) { target = new() { TransferKey = source.TransferKey }; db.InsurancePolicies.Add(target); }
            InsuranceService.CopyTerms(source, target);
            target.Version = source.Version; target.Status = source.Status;
            target.RequestedVersion = source.RequestedVersion; target.ReviewedVersion = source.ReviewedVersion;
            target.UpdatedAtUtc = source.UpdatedAtUtc; target.UpdatedBy = source.UpdatedBy;
            foreach (var document in source.Documents)
            {
                var existing = target.Documents.FirstOrDefault(x => x.TransferKey == document.TransferKey);
                if (existing is null) { existing = new() { TransferKey = document.TransferKey }; target.Documents.Add(existing); }
                existing.Kind = document.Kind; existing.Title = document.Title; existing.RelativePath = document.RelativePath;
                existing.Sha256 = document.Sha256; existing.DocumentDate = document.DocumentDate;
            }
            foreach (var item in source.Events.Where(x => !target.Events.Any(y => y.TransferKey == x.TransferKey)))
                target.Events.Add(new() { TransferKey = item.TransferKey, Action = item.Action, Performer = item.Performer,
                    RecordedBy = item.RecordedBy, RecordedAtUtc = item.RecordedAtUtc, OccurredOn = item.OccurredOn,
                    Summary = item.Summary, DocumentKey = item.DocumentKey });
            await db.SaveChangesAsync(ct);
            db.ComplianceAuditEvents.Add(new() { EntityType = nameof(InsurancePolicy), EntityId = target.Id, Action = "InsuranceProgrammeImported",
                UserName = user, Reason = "Apply reviewed programme insurance snapshot; retain source performers and dates.",
                NewValueJson = JsonSerializer.Serialize(new { source.TransferKey, source.Version }) });
        }
        await db.SaveChangesAsync(ct);
        await new InsuranceService(db, new InsuranceFiles(configuration)).RefreshTasksAsync();
    }
}
