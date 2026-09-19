using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientDuplicateReviewTransferService(
    ApplicationDbContext db,
    ClientReviewTransferService reviewTransfers)
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClientDuplicateSource> LoadSourceAsync(
        int clientId, CancellationToken cancellationToken = default)
    {
        var client = await db.Clients.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == clientId, cancellationToken)
            ?? throw new KeyNotFoundException("Client not found.");
        var canonical = client.DuplicateOfClientId.HasValue
            ? await db.Clients.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == client.DuplicateOfClientId.Value, cancellationToken)
            : null;
        if (client.LifecycleStatus != ClientLifecycleStatuses.Duplicate ||
            canonical is null ||
            !client.LegacyClientId.HasValue ||
            !canonical.LegacyClientId.HasValue)
        {
            throw new ValidationException("Only a resolved duplicate with an identifiable canonical client can be transferred.");
        }
        if (string.IsNullOrWhiteSpace(client.LifecycleReason))
        {
            throw new ValidationException("The duplicate classification needs a recorded reason before transfer.");
        }

        return new ClientDuplicateSource(
            client.Id,
            new ClientDuplicateIdentity(client.LegacyClientId.Value, client.KanaanId, client.DisplayName),
            canonical.Id,
            new ClientDuplicateIdentity(canonical.LegacyClientId.Value, canonical.KanaanId, canonical.DisplayName),
            client.LifecycleReason,
            client.LifecycleReviewedAtUtc,
            client.LifecycleReviewedBy);
    }

    public async Task<ClientDuplicateExportResult> ExportAsync(
        int clientId, string passphrase, string? userName, string reason,
        CancellationToken cancellationToken = default)
    {
        ClientReviewTransferService.ValidatePassphrase(passphrase);
        var user = Require(userName, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");
        var source = await LoadSourceAsync(clientId, cancellationToken);

        var package = new ClientDuplicateReviewPackage(
            FormatVersion,
            Guid.NewGuid().ToString("D"),
            DateTime.UtcNow,
            user,
            reason,
            source.Duplicate,
            source.Canonical,
            source.Reason,
            source.ReviewedAtUtc,
            source.ReviewedBy);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
        var bytes = ClientReviewTransferService.Encrypt(plaintext, passphrase);
        var fileName = $"KCAS-duplicate-C{source.Duplicate.LegacyClientId}-{package.CreatedAtUtc:yyyyMMdd}-{package.PackageId[..8]}.kcas-duplicate-review";
        var directory = Path.Combine(reviewTransfers.StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        var summary = JsonSerializer.Serialize(package, JsonOptions);
        var record = new ClientReviewTransferRecord
        {
            PackageId = package.PackageId,
            Direction = ClientReviewTransferDirections.Outgoing,
            ContentSha256 = hash,
            ClientId = source.ClientId,
            Status = ClientReviewTransferStatuses.Exported,
            FileName = fileName,
            StoragePath = path,
            SummaryJson = summary
        };
        db.ClientReviewTransferRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(ClientReviewTransferRecord),
            EntityId = checked((int)record.Id),
            Action = "ClientDuplicateReconciliationExported",
            NewValueJson = summary,
            UserName = user,
            Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);
        return new ClientDuplicateExportResult(package.PackageId, fileName, path, bytes.Length);
    }

    public async Task<ClientDuplicateReviewPreview> PreviewAsync(
        byte[] encryptedPackage, string passphrase, CancellationToken cancellationToken = default)
    {
        ClientReviewTransferService.ValidatePassphrase(passphrase);
        ClientDuplicateReviewPackage package;
        string hash;
        try
        {
            var plaintext = ClientReviewTransferService.Decrypt(encryptedPackage, passphrase);
            hash = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
            package = JsonSerializer.Deserialize<ClientDuplicateReviewPackage>(plaintext, JsonOptions)
                ?? throw new ValidationException("The duplicate package is empty.");
        }
        catch (CryptographicException)
        {
            throw new ValidationException("The package could not be decrypted. Check the passphrase and package integrity.");
        }
        catch (JsonException)
        {
            throw new ValidationException("The decrypted package is not valid duplicate review data.");
        }
        catch (EndOfStreamException)
        {
            throw new ValidationException("The encrypted package is truncated or invalid.");
        }
        catch (IOException)
        {
            throw new ValidationException("The encrypted package could not be read.");
        }

        if (package.FormatVersion != FormatVersion || !Guid.TryParse(package.PackageId, out _) ||
            package.Duplicate is null || package.Canonical is null ||
            package.Duplicate.LegacyClientId <= 0 || package.Canonical.LegacyClientId <= 0 ||
            package.Duplicate.LegacyClientId == package.Canonical.LegacyClientId ||
            string.IsNullOrWhiteSpace(package.Reason))
        {
            throw new ValidationException("The duplicate package has invalid or incomplete reconciliation data.");
        }

        var conflicts = new List<string>();
        var duplicate = await ResolveAsync(package.Duplicate, cancellationToken);
        var canonical = await ResolveAsync(package.Canonical, cancellationToken);
        if (duplicate is null) conflicts.Add("No unique live duplicate client matches the legacy and Kanaan IDs.");
        if (canonical is null) conflicts.Add("No unique live canonical client matches the legacy and Kanaan IDs.");
        if (canonical is not null && canonical.LifecycleStatus == ClientLifecycleStatuses.Duplicate)
            conflicts.Add("The proposed canonical client is itself classified as a duplicate on live.");
        if (duplicate is not null && duplicate.LifecycleStatus != ClientLifecycleStatuses.Unreviewed &&
            (duplicate.LifecycleStatus != ClientLifecycleStatuses.Duplicate ||
             duplicate.DuplicateOfClientId != canonical?.Id))
            conflicts.Add("Live has a different lifecycle or canonical-client decision for this record.");
        if (duplicate is not null && await db.ClientRiskAssessments.AsNoTracking().AnyAsync(
                item => item.ClientId == duplicate.Id &&
                    (item.Status == ClientRiskAssessmentStatuses.Finalised ||
                     item.Status == ClientRiskAssessmentStatuses.Approved), cancellationToken))
            conflicts.Add("The live duplicate record already has a completed risk assessment.");

        var alreadyApplied = await db.ClientReviewTransferRecords.AsNoTracking().AnyAsync(item =>
            item.Direction == ClientReviewTransferDirections.Incoming &&
            item.Status == ClientReviewTransferStatuses.Applied &&
            (item.PackageId == package.PackageId || item.ContentSha256 == hash), cancellationToken);
        return new ClientDuplicateReviewPreview(package, duplicate?.Id, canonical?.Id,
            duplicate?.DisplayName, canonical?.DisplayName, hash, alreadyApplied, conflicts);
    }

    public async Task<ClientDuplicateReviewPreview> ApplyAsync(
        byte[] encryptedPackage, string passphrase, string? userName, string reason,
        CancellationToken cancellationToken = default)
    {
        var user = Require(userName, "A signed-in importer is required.");
        reason = Require(reason, "An import approval reason is required.");
        var preview = await PreviewAsync(encryptedPackage, passphrase, cancellationToken);
        if (!preview.CanApply)
            throw new InvalidOperationException("The duplicate package is already applied or has unresolved conflicts.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var duplicate = await db.Clients.SingleAsync(item => item.Id == preview.DuplicateClientId, cancellationToken);
        var canonical = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == preview.CanonicalClientId, cancellationToken);
        if (duplicate.LifecycleStatus != ClientLifecycleStatuses.Unreviewed &&
            (duplicate.LifecycleStatus != ClientLifecycleStatuses.Duplicate ||
             duplicate.DuplicateOfClientId != canonical.Id))
            throw new InvalidOperationException("Live classification changed since preview; preview the package again.");
        var oldValue = JsonSerializer.Serialize(new
        {
            duplicate.LifecycleStatus,
            duplicate.DuplicateOfClientId,
            duplicate.LifecycleReason
        }, JsonOptions);
        duplicate.LifecycleStatus = ClientLifecycleStatuses.Duplicate;
        duplicate.DuplicateOfClientId = canonical.Id;
        duplicate.LifecycleReason = preview.Package.Reason;
        duplicate.LifecycleReviewedAtUtc = preview.Package.ReviewedAtUtc;
        duplicate.LifecycleReviewedBy = preview.Package.ReviewedBy;
        duplicate.IsActive = false;
        duplicate.UpdatedAtUtc = DateTime.UtcNow;
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(Client),
            EntityId = duplicate.Id,
            Action = "ClientDuplicateReconciliationImported",
            OldValueJson = oldValue,
            NewValueJson = JsonSerializer.Serialize(new
            {
                duplicate.LifecycleStatus,
                duplicate.DuplicateOfClientId,
                duplicate.LifecycleReason,
                SourcePackageId = preview.Package.PackageId
            }, JsonOptions),
            UserName = user,
            Reason = reason
        });
        var fileName = $"KCAS-duplicate-C{duplicate.LegacyClientId}-{preview.Package.PackageId[..8]}.kcas-duplicate-review";
        var directory = Path.Combine(reviewTransfers.StorageRoot, "incoming");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, encryptedPackage, cancellationToken);
        db.ClientReviewTransferRecords.Add(new ClientReviewTransferRecord
        {
            PackageId = preview.Package.PackageId,
            Direction = ClientReviewTransferDirections.Incoming,
            ContentSha256 = preview.ContentSha256,
            ClientId = duplicate.Id,
            Status = ClientReviewTransferStatuses.Applied,
            FileName = fileName,
            StoragePath = path,
            SummaryJson = JsonSerializer.Serialize(preview.Package, JsonOptions),
            AppliedAtUtc = DateTime.UtcNow,
            AppliedBy = user
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return preview;
    }

    private async Task<Client?> ResolveAsync(ClientDuplicateIdentity identity, CancellationToken cancellationToken)
    {
        var matches = await db.Clients.AsNoTracking()
            .Where(item => item.LegacyClientId == identity.LegacyClientId)
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 &&
            string.Equals(matches[0].KanaanId, identity.KanaanId, StringComparison.OrdinalIgnoreCase)
            ? matches[0] : null;
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();
}

public sealed record ClientDuplicateIdentity(int LegacyClientId, string? KanaanId, string DisplayName);
public sealed record ClientDuplicateSource(
    int ClientId, ClientDuplicateIdentity Duplicate, int CanonicalClientId,
    ClientDuplicateIdentity Canonical, string Reason, DateTime? ReviewedAtUtc, string? ReviewedBy);
public sealed record ClientDuplicateReviewPackage(
    int FormatVersion, string PackageId, DateTime CreatedAtUtc, string ExportedBy, string ExportReason,
    ClientDuplicateIdentity Duplicate, ClientDuplicateIdentity Canonical, string Reason,
    DateTime? ReviewedAtUtc, string? ReviewedBy);
public sealed record ClientDuplicateExportResult(string PackageId, string FileName, string StoragePath, int SizeBytes);
public sealed record ClientDuplicateReviewPreview(
    ClientDuplicateReviewPackage Package, int? DuplicateClientId, int? CanonicalClientId,
    string? DuplicateClientName, string? CanonicalClientName, string ContentSha256,
    bool AlreadyApplied, IReadOnlyList<string> Conflicts)
{
    public bool CanApply => !AlreadyApplied && Conflicts.Count == 0 && DuplicateClientId.HasValue && CanonicalClientId.HasValue;
}
