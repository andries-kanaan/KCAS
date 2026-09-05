using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientReviewFamilyTransferService(
    ApplicationDbContext db,
    ClientReviewTransferService clientTransfers)
{
    private const string BundleMagic = "KCAS-CLIENT-REVIEW-FAMILY-1";
    private const int BundleVersion = 1;
    private const int Pbkdf2Iterations = 300_000;
    private const int MaximumBundleBytes = 100 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ClientReviewFamilyGroup?> LoadFamilyAsync(
        int clientId,
        CancellationToken cancellationToken = default)
    {
        var anchor = await db.Clients.AsNoTracking()
            .Where(client => client.Id == clientId)
            .Select(client => new { client.KanaanId })
            .SingleOrDefaultAsync(cancellationToken);
        if (anchor is null || string.IsNullOrWhiteSpace(anchor.KanaanId))
        {
            return null;
        }

        var members = await db.Clients.AsNoTracking()
            .Where(client => client.KanaanId == anchor.KanaanId)
            .OrderBy(client => client.DisplayName)
            .Select(client => new ClientReviewFamilyMemberOption
            {
                ClientId = client.Id,
                LegacyClientId = client.LegacyClientId,
                DisplayName = client.DisplayName,
                LifecycleStatus = client.LifecycleStatus,
                HasCompletedAssessment = client.RiskAssessments.Any(assessment =>
                    assessment.Status == ClientRiskAssessmentStatuses.Finalised ||
                    assessment.Status == ClientRiskAssessmentStatuses.Approved)
            })
            .ToListAsync(cancellationToken);

        return new ClientReviewFamilyGroup
        {
            KanaanId = anchor.KanaanId,
            Members = members
        };
    }

    public async Task<ClientReviewFamilyExportResult> ExportAsync(
        int anchorClientId,
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var user = Require(userName, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");
        var family = await LoadFamilyAsync(anchorClientId, cancellationToken)
            ?? throw new InvalidOperationException("The selected client has no Kanaan family identifier.");
        var includedMembers = family.Members.Where(member => member.HasCompletedAssessment).ToList();
        if (includedMembers.Count < 2)
        {
            throw new InvalidOperationException(
                "A family export requires at least two linked clients with finalised or approved assessments.");
        }
        if (includedMembers.Any(member => !member.LegacyClientId.HasValue))
        {
            throw new InvalidOperationException(
                "Every included family member requires a legacy client ID for unambiguous live matching.");
        }

        var bundle = new ClientReviewFamilyBundle
        {
            FormatVersion = BundleVersion,
            BundleId = Guid.NewGuid().ToString("D"),
            KanaanId = family.KanaanId,
            CreatedAtUtc = DateTime.UtcNow,
            ExportedBy = user,
            ExportReason = reason,
            SourceEnvironment = Environment.MachineName,
            ExcludedMembers = family.Members
                .Where(member => !member.HasCompletedAssessment)
                .Select(member => new ClientReviewFamilyExcludedMember
                {
                    LegacyClientId = member.LegacyClientId,
                    DisplayName = member.DisplayName,
                    Reason = "No finalised or approved assessment was available at export."
                })
                .ToList()
        };
        foreach (var member in includedMembers)
        {
            var embedded = await clientTransfers.CreateEmbeddedExportAsync(
                member.ClientId, passphrase, user, reason, cancellationToken);
            bundle.Members.Add(new ClientReviewFamilyBundleMember
            {
                PackageId = embedded.Package.PackageId,
                LegacyClientId = embedded.Package.Client.LegacyClientId,
                DisplayName = embedded.Package.Client.DisplayName,
                EncryptedPackageSha256 = Sha256(embedded.EncryptedPackage),
                EncryptedPackage = embedded.EncryptedPackage
            });
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(bundle, JsonOptions);
        var contentSha256 = Sha256(plaintext);
        var encrypted = Encrypt(plaintext, passphrase);
        var safeKanaanId = SafeFilePart(family.KanaanId);
        var fileName = $"KCAS-family-{safeKanaanId}-{bundle.CreatedAtUtc:yyyyMMdd}-{bundle.BundleId[..12].Replace("-", "")}.kcas-family-review";
        var directory = Path.Combine(clientTransfers.StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);

        var summary = JsonSerializer.Serialize(BundleSummary(bundle), JsonOptions);
        var record = new ClientReviewTransferRecord
        {
            PackageId = bundle.BundleId,
            Direction = ClientReviewTransferDirections.Outgoing,
            ContentSha256 = contentSha256,
            ClientId = anchorClientId,
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
            Action = "ClientReviewFamilyBundleExported",
            NewValueJson = summary,
            UserName = user,
            Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);

        return new ClientReviewFamilyExportResult(
            bundle.BundleId, fileName, path, encrypted.LongLength, contentSha256,
            family.KanaanId, bundle.Members.Count, bundle.ExcludedMembers.Count);
    }

    public async Task<ClientReviewFamilyPreview> PreviewAsync(
        byte[] encryptedBundle,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var bundle = DecryptBundle(encryptedBundle, passphrase, out var contentSha256);
        var conflicts = ValidateBundle(bundle);
        var alreadyRecorded = await db.ClientReviewTransferRecords.AsNoTracking().AnyAsync(record =>
            record.Direction == ClientReviewTransferDirections.Incoming &&
            record.Status == ClientReviewTransferStatuses.Applied &&
            (record.PackageId == bundle.BundleId || record.ContentSha256 == contentSha256),
            cancellationToken);

        var members = new List<ClientReviewFamilyMemberPreview>();
        foreach (var member in bundle.Members)
        {
            var memberConflicts = new List<string>();
            ClientReviewTransferPreview? clientPreview = null;
            if (!string.Equals(member.EncryptedPackageSha256, Sha256(member.EncryptedPackage), StringComparison.OrdinalIgnoreCase))
            {
                memberConflicts.Add("The embedded client package hash does not match the family manifest.");
            }
            else
            {
                try
                {
                    clientPreview = await clientTransfers.PreviewAsync(
                        member.EncryptedPackage, passphrase, cancellationToken);
                    if (!string.Equals(clientPreview.Package.PackageId, member.PackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        memberConflicts.Add("The embedded client package ID does not match the family manifest.");
                    }
                    if (!string.Equals(clientPreview.Package.Client.KanaanId, bundle.KanaanId, StringComparison.OrdinalIgnoreCase))
                    {
                        memberConflicts.Add("The embedded client does not share the family Kanaan ID.");
                    }
                    if (member.LegacyClientId != clientPreview.Package.Client.LegacyClientId)
                    {
                        memberConflicts.Add("The embedded client legacy ID does not match the family manifest.");
                    }
                }
                catch (ValidationException exception)
                {
                    memberConflicts.Add(exception.Message);
                }
            }
            members.Add(new ClientReviewFamilyMemberPreview
            {
                Manifest = member,
                ClientPreview = clientPreview,
                Conflicts = memberConflicts
            });
        }

        return new ClientReviewFamilyPreview
        {
            Bundle = bundle,
            ContentSha256 = contentSha256,
            AlreadyApplied = alreadyRecorded,
            Conflicts = conflicts,
            Members = members
        };
    }

    public async Task<ClientReviewFamilyImportResult> ApplyAsync(
        byte[] encryptedBundle,
        string passphrase,
        string? userName,
        string reason,
        IReadOnlyCollection<string>? selectedPackageIds = null,
        CancellationToken cancellationToken = default)
    {
        var user = Require(userName, "A signed-in importer is required.");
        reason = Require(reason, "A live import approval reason is required.");
        var preview = await PreviewAsync(encryptedBundle, passphrase, cancellationToken);
        if (preview.Conflicts.Count > 0)
        {
            throw new InvalidOperationException("The family bundle has structural conflicts and cannot be applied.");
        }
        var selected = selectedPackageIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ??
            preview.Members.Where(member => member.CanApply)
                .Select(member => member.Manifest.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("Select at least one valid family member package to apply.");
        }
        if (selected.Any(packageId => preview.Members.All(member =>
            !string.Equals(member.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("The selected family member is not present in the bundle.");
        }

        var results = new List<ClientReviewFamilyMemberImportResult>();
        foreach (var member in preview.Members.Where(member => selected.Contains(member.Manifest.PackageId)))
        {
            if (!member.CanApply)
            {
                results.Add(new ClientReviewFamilyMemberImportResult
                {
                    PackageId = member.Manifest.PackageId,
                    DisplayName = member.Manifest.DisplayName,
                    Status = member.ClientPreview?.AlreadyApplied == true ? "AlreadyApplied" : "Conflict",
                    Message = string.Join(" ", member.AllConflicts)
                });
                continue;
            }
            try
            {
                var imported = await clientTransfers.ApplyAsync(
                    member.Manifest.EncryptedPackage, passphrase, user, reason, cancellationToken);
                results.Add(new ClientReviewFamilyMemberImportResult
                {
                    PackageId = member.Manifest.PackageId,
                    ClientId = imported.ClientId,
                    DisplayName = imported.ClientDisplayName,
                    AssessmentId = imported.AssessmentId,
                    EvidenceImported = imported.EvidenceImported,
                    Status = "Applied",
                    Message = imported.EvidenceVerificationWarning ??
                        (imported.EvidenceVerificationScanRunId.HasValue
                            ? $"Live evidence folder verified by scan {imported.EvidenceVerificationScanRunId.Value}."
                            : null)
                });
            }
            catch (InvalidOperationException exception)
            {
                results.Add(new ClientReviewFamilyMemberImportResult
                {
                    PackageId = member.Manifest.PackageId,
                    DisplayName = member.Manifest.DisplayName,
                    Status = "Conflict",
                    Message = exception.Message
                });
            }
        }

        var applied = results.Where(result => result.Status == "Applied").ToList();
        if (applied.Count > 0)
        {
            var memberPackageIds = preview.Bundle.Members.Select(member => member.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var appliedPackageIds = await db.ClientReviewTransferRecords.AsNoTracking()
                .Where(item => item.Direction == ClientReviewTransferDirections.Incoming &&
                    item.Status == ClientReviewTransferStatuses.Applied)
                .Select(item => item.PackageId)
                .ToListAsync(cancellationToken);
            appliedPackageIds = appliedPackageIds
                .Where(memberPackageIds.Contains)
                .ToList();
            var record = await db.ClientReviewTransferRecords.SingleOrDefaultAsync(item =>
                item.Direction == ClientReviewTransferDirections.Incoming &&
                item.PackageId == preview.Bundle.BundleId, cancellationToken);
            var summaryJson = JsonSerializer.Serialize(new
            {
                Bundle = BundleSummary(preview.Bundle),
                AppliedPackageIds = appliedPackageIds.OrderBy(packageId => packageId),
                LatestResults = results
            }, JsonOptions);
            if (record is null)
            {
                record = new ClientReviewTransferRecord
                {
                    PackageId = preview.Bundle.BundleId,
                    Direction = ClientReviewTransferDirections.Incoming,
                    ContentSha256 = preview.ContentSha256,
                    ClientId = applied[0].ClientId!.Value,
                    Status = ClientReviewTransferStatuses.Applied,
                    FileName = $"family-{preview.Bundle.BundleId}.kcas-family-review",
                    StoragePath = "Imported upload"
                };
                db.ClientReviewTransferRecords.Add(record);
            }
            record.SummaryJson = summaryJson;
            record.AppliedAtUtc = DateTime.UtcNow;
            record.AppliedBy = user;
            await db.SaveChangesAsync(cancellationToken);
            db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
            {
                EntityType = nameof(ClientReviewTransferRecord),
                EntityId = checked((int)record.Id),
                Action = "ClientReviewFamilyBundleApplied",
                NewValueJson = record.SummaryJson,
                UserName = user,
                Reason = reason
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ClientReviewFamilyImportResult
        {
            BundleId = preview.Bundle.BundleId,
            KanaanId = preview.Bundle.KanaanId,
            Members = results
        };
    }

    public static bool IsFamilyBundle(byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            return reader.ReadString() == BundleMagic;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> ValidateBundle(ClientReviewFamilyBundle bundle)
    {
        var conflicts = new List<string>();
        if (bundle.FormatVersion != BundleVersion)
        {
            conflicts.Add($"Family bundle format {bundle.FormatVersion} is not supported.");
        }
        if (!Guid.TryParse(bundle.BundleId, out _)) conflicts.Add("The family bundle ID is invalid.");
        if (string.IsNullOrWhiteSpace(bundle.KanaanId)) conflicts.Add("The family Kanaan ID is missing.");
        if (bundle.Members.Count < 2) conflicts.Add("A family bundle must contain at least two client packages.");
        if (bundle.Members.Select(member => member.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundle.Members.Count)
        {
            conflicts.Add("The family bundle contains duplicate client package IDs.");
        }
        if (bundle.Members.Select(member => member.LegacyClientId)
            .Distinct().Count() != bundle.Members.Count)
        {
            conflicts.Add("The family bundle contains duplicate client legacy IDs.");
        }
        return conflicts;
    }

    private static ClientReviewFamilyBundle DecryptBundle(
        byte[] encrypted,
        string passphrase,
        out string contentSha256)
    {
        try
        {
            using var stream = new MemoryStream(encrypted);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadString() != BundleMagic)
            {
                throw new ValidationException("This is not a KCAS family review bundle.");
            }
            var iterations = reader.ReadInt32();
            if (iterations < 100_000 || iterations > 1_000_000)
            {
                throw new ValidationException("The family bundle encryption parameters are invalid.");
            }
            var salt = ReadExact(reader, 16, "salt");
            var nonce = ReadExact(reader, 12, "nonce");
            var tag = ReadExact(reader, 16, "authentication tag");
            var ciphertextLength = reader.ReadInt32();
            if (ciphertextLength < 1 || ciphertextLength > MaximumBundleBytes ||
                ciphertextLength > stream.Length - stream.Position)
            {
                throw new ValidationException("The family bundle payload length is invalid.");
            }
            var ciphertext = reader.ReadBytes(ciphertextLength);
            var plaintext = new byte[ciphertext.Length];
            var key = Rfc2898DeriveBytes.Pbkdf2(
                passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
            using (var aes = new AesGcm(key, 16))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(BundleMagic));
            }
            CryptographicOperations.ZeroMemory(key);
            contentSha256 = Sha256(plaintext);
            var bundle = JsonSerializer.Deserialize<ClientReviewFamilyBundle>(plaintext, JsonOptions)
                ?? throw new ValidationException("The family bundle is empty.");
            bundle.Members ??= [];
            bundle.ExcludedMembers ??= [];
            return bundle;
        }
        catch (CryptographicException)
        {
            throw new ValidationException(
                "The family bundle could not be decrypted. Check the passphrase and bundle integrity.");
        }
        catch (JsonException)
        {
            throw new ValidationException("The decrypted family bundle is not valid KCAS review data.");
        }
        catch (EndOfStreamException)
        {
            throw new ValidationException("The encrypted family bundle is truncated or invalid.");
        }
    }

    private static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(BundleMagic));
        }
        CryptographicOperations.ZeroMemory(key);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(BundleMagic);
        writer.Write(Pbkdf2Iterations);
        writer.Write(salt.Length);
        writer.Write(salt);
        writer.Write(nonce.Length);
        writer.Write(nonce);
        writer.Write(tag.Length);
        writer.Write(tag);
        writer.Write(ciphertext.Length);
        writer.Write(ciphertext);
        return stream.ToArray();
    }

    private static byte[] ReadExact(BinaryReader reader, int expectedLength, string label)
    {
        if (reader.ReadInt32() != expectedLength)
        {
            throw new ValidationException($"The family bundle {label} is invalid.");
        }
        var value = reader.ReadBytes(expectedLength);
        if (value.Length != expectedLength)
        {
            throw new ValidationException($"The family bundle {label} is truncated.");
        }
        return value;
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string SafeFilePart(string value)
    {
        var safe = string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        return string.IsNullOrWhiteSpace(safe) ? "family" : safe.Trim('-');
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 7)
        {
            throw new ValidationException("Use a package passphrase of at least 7 characters.");
        }
    }

    private static object BundleSummary(ClientReviewFamilyBundle bundle) => new
    {
        bundle.BundleId,
        bundle.KanaanId,
        bundle.CreatedAtUtc,
        bundle.ExportedBy,
        MemberCount = bundle.Members.Count,
        ExcludedMemberCount = bundle.ExcludedMembers.Count,
        Members = bundle.Members.Select(member => new
        {
            member.PackageId,
            member.LegacyClientId,
            member.DisplayName
        })
    };
}

public sealed class ClientReviewFamilyGroup
{
    public string KanaanId { get; set; } = "";
    public List<ClientReviewFamilyMemberOption> Members { get; set; } = [];
}

public sealed class ClientReviewFamilyMemberOption
{
    public int ClientId { get; set; }
    public int? LegacyClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public string LifecycleStatus { get; set; } = "";
    public bool HasCompletedAssessment { get; set; }
}

public sealed class ClientReviewFamilyBundle
{
    public int FormatVersion { get; set; }
    public string BundleId { get; set; } = "";
    public string KanaanId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string ExportedBy { get; set; } = "";
    public string ExportReason { get; set; } = "";
    public string SourceEnvironment { get; set; } = "";
    public List<ClientReviewFamilyBundleMember> Members { get; set; } = [];
    public List<ClientReviewFamilyExcludedMember> ExcludedMembers { get; set; } = [];
}

public sealed class ClientReviewFamilyBundleMember
{
    public string PackageId { get; set; } = "";
    public int? LegacyClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public string EncryptedPackageSha256 { get; set; } = "";
    public byte[] EncryptedPackage { get; set; } = [];
}

public sealed class ClientReviewFamilyExcludedMember
{
    public int? LegacyClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed record ClientReviewFamilyExportResult(
    string PackageId,
    string FileName,
    string StoragePath,
    long SizeBytes,
    string ContentSha256,
    string KanaanId,
    int MemberCount,
    int ExcludedMemberCount);

public sealed class ClientReviewFamilyPreview
{
    public ClientReviewFamilyBundle Bundle { get; set; } = new();
    public string ContentSha256 { get; set; } = "";
    public bool AlreadyApplied { get; set; }
    public List<string> Conflicts { get; set; } = [];
    public List<ClientReviewFamilyMemberPreview> Members { get; set; } = [];
    public bool CanApply => Conflicts.Count == 0 && Members.Any(member => member.CanApply);
}

public sealed class ClientReviewFamilyMemberPreview
{
    public ClientReviewFamilyBundleMember Manifest { get; set; } = new();
    public ClientReviewTransferPreview? ClientPreview { get; set; }
    public List<string> Conflicts { get; set; } = [];
    public IEnumerable<string> AllConflicts =>
        Conflicts.Concat(ClientPreview?.Conflicts ?? []);
    public bool CanApply =>
        Conflicts.Count == 0 && ClientPreview is { CanApply: true };
}

public sealed class ClientReviewFamilyImportResult
{
    public string BundleId { get; set; } = "";
    public string KanaanId { get; set; } = "";
    public List<ClientReviewFamilyMemberImportResult> Members { get; set; } = [];
}

public sealed class ClientReviewFamilyMemberImportResult
{
    public string PackageId { get; set; } = "";
    public int? ClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public int? AssessmentId { get; set; }
    public int EvidenceImported { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
}
