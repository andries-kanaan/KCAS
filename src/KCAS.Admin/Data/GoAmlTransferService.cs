using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class GoAmlTransferService(
    ApplicationDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    private const string PackageMagic = "KCAS-GOAML-CHECKS-1";
    private const int PackageVersion = 1;
    private const int Pbkdf2Iterations = 300_000;
    private const int MaximumChecks = 366;
    private const int MaximumPackageBytes = 50 * 1024 * 1024;
    private const int MaximumEvidenceBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string StorageRoot => ResolveStorageRoot();

    public async Task<GoAmlExportRange?> LoadExportRangeAsync(
        CancellationToken cancellationToken = default)
    {
        var completed = db.GoAmlDailyChecks.AsNoTracking()
            .Where(item => item.Status != GoAmlCheckStatuses.Started);
        var first = await completed
            .OrderBy(item => item.CheckDate)
            .Select(item => (DateOnly?)item.CheckDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (!first.HasValue) return null;
        var last = await completed
            .OrderByDescending(item => item.CheckDate)
            .Select(item => item.CheckDate)
            .FirstAsync(cancellationToken);
        return new GoAmlExportRange(first.Value, last);
    }

    public async Task<GoAmlExportResult> ExportAsync(
        DateOnly firstCheckDate,
        DateOnly lastCheckDate,
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(firstCheckDate, lastCheckDate);
        ValidatePassphrase(passphrase);
        var user = Require(userName, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");

        var checks = await db.GoAmlDailyChecks.AsNoTracking()
            .Where(item =>
                item.CheckDate >= firstCheckDate &&
                item.CheckDate <= lastCheckDate &&
                item.Status != GoAmlCheckStatuses.Started)
            .OrderBy(item => item.CheckDate)
            .ToListAsync(cancellationToken);
        if (checks.Count == 0)
        {
            throw new InvalidOperationException("No completed goAML checks exist in the selected date range.");
        }

        var package = new GoAmlTransferPackage
        {
            FormatVersion = PackageVersion,
            PackageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            ExportedBy = user,
            ExportReason = reason,
            SourceEnvironment = environment.EnvironmentName
        };
        foreach (var check in checks)
        {
            package.Checks.Add(await BuildCheckPackageAsync(check, cancellationToken));
        }
        var exportConflicts = new List<string>();
        foreach (var check in package.Checks) ValidateCheck(check, exportConflicts);
        if (exportConflicts.Count > 0)
        {
            throw new InvalidOperationException(
                "The selected checks are not transferable: " + string.Join(" ", exportConflicts));
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        if (plaintext.Length > MaximumPackageBytes)
        {
            throw new ValidationException("The selected goAML checks exceed the 50 MB package safety limit. Export a smaller date range.");
        }
        var contentSha256 = Sha256(plaintext);
        var encrypted = Encrypt(plaintext, passphrase);
        var fileName = BuildFileName(package);
        var directory = Path.Combine(StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);

        var record = NewRecord(package, GoAmlTransferDirections.Outgoing,
            GoAmlTransferStatuses.Exported, contentSha256, fileName, path);
        db.GoAmlTransferRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        db.ComplianceAuditEvents.Add(CreateAudit(nameof(GoAmlTransferRecord), checked((int)record.Id),
            "GoAmlPackageExported", user, reason, PackageSummary(package)));
        await db.SaveChangesAsync(cancellationToken);

        return new GoAmlExportResult(package.PackageId, fileName, path, encrypted.Length,
            contentSha256, package.Checks.Count, package.Checks.Count(item => item.Evidence is not null),
            package.Checks.Min(item => item.CheckDate), package.Checks.Max(item => item.CheckDate));
    }

    public async Task<GoAmlTransferPreview> PreviewAsync(
        byte[] encryptedPackage,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var package = DecryptPackage(encryptedPackage, passphrase, out var contentSha256);
        var conflicts = new List<string>();
        var warnings = new List<string>();

        if (package.FormatVersion != PackageVersion)
        {
            conflicts.Add($"Package format {package.FormatVersion} is not supported by this KCAS version.");
        }
        if (package.Checks.Count is 0 or > MaximumChecks)
        {
            conflicts.Add($"The package must contain between 1 and {MaximumChecks} checks.");
        }
        foreach (var duplicate in package.Checks.GroupBy(item => item.CheckDate).Where(group => group.Count() > 1))
        {
            conflicts.Add($"The package contains more than one check for {duplicate.Key:yyyy-MM-dd}.");
        }
        foreach (var check in package.Checks)
        {
            ValidateCheck(check, conflicts);
        }

        var alreadyApplied = await db.GoAmlTransferRecords.AsNoTracking().AnyAsync(record =>
            record.Direction == GoAmlTransferDirections.Incoming &&
            record.Status == GoAmlTransferStatuses.Applied &&
            (record.PackageId == package.PackageId || record.ContentSha256 == contentSha256),
            cancellationToken);
        if (alreadyApplied)
        {
            warnings.Add("This package, or identical package content, has already been applied.");
        }

        var dates = package.Checks.Select(item => item.CheckDate).Distinct().ToHashSet();
        var existing = new List<GoAmlDailyCheck>();
        if (dates.Count > 0)
        {
            var firstDate = dates.Min();
            var lastDate = dates.Max();
            existing = (await db.GoAmlDailyChecks.AsNoTracking()
                    .Where(item => item.CheckDate >= firstDate && item.CheckDate <= lastDate)
                    .ToListAsync(cancellationToken))
                .Where(item => dates.Contains(item.CheckDate))
                .ToList();
        }
        var existingByDate = existing.ToDictionary(item => item.CheckDate);
        var existingCount = 0;
        var conflictingExistingCount = 0;
        foreach (var source in package.Checks)
        {
            if (!existingByDate.TryGetValue(source.CheckDate, out var target)) continue;
            if (Equivalent(target, source))
            {
                existingCount++;
            }
            else
            {
                conflictingExistingCount++;
                conflicts.Add(
                    $"Live already has a different goAML check for {source.CheckDate:yyyy-MM-dd}. Existing checks are never overwritten.");
            }
        }
        if (existingCount > 0)
        {
            warnings.Add($"{existingCount} identical check(s) already exist on live and will be skipped.");
        }

        return new GoAmlTransferPreview
        {
            Package = package,
            ContentSha256 = contentSha256,
            AlreadyApplied = alreadyApplied,
            ExistingCheckCount = existingCount,
            NewCheckCount = package.Checks.Count - existingCount - conflictingExistingCount,
            EvidenceCount = package.Checks.Count(item => item.Evidence is not null),
            Conflicts = conflicts.Distinct(StringComparer.Ordinal).ToList(),
            Warnings = warnings
        };
    }

    public async Task<GoAmlImportResult> ApplyAsync(
        byte[] encryptedPackage,
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var user = Require(userName, "A signed-in importer is required.");
        reason = Require(reason, "An import approval reason is required.");
        var preview = await PreviewAsync(encryptedPackage, passphrase, cancellationToken);
        if (preview.AlreadyApplied)
        {
            throw new InvalidOperationException("This goAML package has already been applied.");
        }
        if (!preview.CanApply)
        {
            throw new InvalidOperationException("The package has unresolved conflicts and cannot be applied.");
        }

        var package = preview.Package;
        var settings = await db.GoAmlSettings.AsNoTracking().OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        var evidenceRoot = ValidateEvidenceRoot(settings?.EvidenceRootPath ?? GoAmlDefaults.EvidenceRootPath);
        var incomingDirectory = Path.Combine(StorageRoot, "incoming");
        Directory.CreateDirectory(incomingDirectory);
        var incomingFileName = BuildFileName(package);
        var incomingPath = Path.Combine(incomingDirectory, incomingFileName);
        var createdEvidencePaths = new List<string>();
        var incomingCreated = false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var incoming = new FileStream(incomingPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, true))
            {
                await incoming.WriteAsync(encryptedPackage, cancellationToken);
            }
            incomingCreated = true;

            var dates = package.Checks.Select(item => item.CheckDate).ToHashSet();
            var firstDate = dates.Min();
            var lastDate = dates.Max();
            var existingDates = (await db.GoAmlDailyChecks.AsNoTracking()
                    .Where(item => item.CheckDate >= firstDate && item.CheckDate <= lastDate)
                    .Select(item => item.CheckDate)
                    .ToListAsync(cancellationToken))
                .Where(dates.Contains)
                .ToHashSet();
            var imported = 0;
            var evidenceImported = 0;
            var tasksCreated = 0;
            foreach (var source in package.Checks.OrderBy(item => item.CheckDate))
            {
                if (existingDates.Contains(source.CheckDate)) continue;

                var check = new GoAmlDailyCheck
                {
                    CheckDate = source.CheckDate,
                    Status = source.Status,
                    StartedAtUtc = source.StartedAtUtc,
                    StartedBy = source.StartedBy,
                    CompletedAtUtc = source.CompletedAtUtc,
                    CompletedBy = source.CompletedBy,
                    Notes = NormalizeOrNull(source.Notes),
                    MessageReference = NormalizeOrNull(source.MessageReference),
                    ActionOwner = NormalizeOrNull(source.ActionOwner),
                    ActionDueDate = source.ActionDueDate
                };
                db.GoAmlDailyChecks.Add(check);
                await db.SaveChangesAsync(cancellationToken);

                if (source.Evidence is not null)
                {
                    var folder = Path.Combine(evidenceRoot, source.CheckDate.Year.ToString("0000"),
                        source.CheckDate.Month.ToString("00"));
                    Directory.CreateDirectory(folder);
                    var fileName = $"goAML-{source.CheckDate:yyyy-MM-dd}-transfer-{check.Id}.jpg";
                    var evidencePath = Path.Combine(folder, fileName);
                    await using (var evidenceFile = new FileStream(evidencePath, FileMode.CreateNew,
                                     FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await evidenceFile.WriteAsync(source.Evidence.Content, cancellationToken);
                    }
                    createdEvidencePaths.Add(evidencePath);
                    check.EvidenceFileName = fileName;
                    check.EvidencePath = evidencePath;
                    check.EvidenceContentType = "image/jpeg";
                    check.EvidenceSizeBytes = source.Evidence.Content.LongLength;
                    check.EvidenceSha256 = source.Evidence.Sha256;
                    evidenceImported++;
                }

                if (source.Status == GoAmlCheckStatuses.ActionRequired)
                {
                    var task = new ComplianceTask
                    {
                        TaskType = ComplianceTaskTypes.Remediation,
                        Title = $"goAML message: {source.MessageReference}",
                        Description = source.Notes,
                        Owner = source.ActionOwner,
                        DueDate = source.ActionDueDate,
                        Priority = "High",
                        Status = ComplianceWorkStatuses.Open,
                        LinkedEntityType = nameof(GoAmlDailyCheck),
                        LinkedEntityId = check.Id,
                        CreatedAtUtc = source.CompletedAtUtc ?? DateTime.UtcNow,
                        UpdatedBy = user
                    };
                    db.ComplianceTasks.Add(task);
                    await db.SaveChangesAsync(cancellationToken);
                    check.ComplianceTaskId = task.Id;
                    db.ComplianceAuditEvents.Add(CreateAudit(nameof(ComplianceTask), task.Id,
                        "CreatedFromGoAmlTransfer", user, reason,
                        new { task.Title, task.Owner, task.DueDate, task.Priority, task.Status, GoAmlDailyCheckId = check.Id }));
                    tasksCreated++;
                }

                db.ComplianceAuditEvents.Add(CreateAudit(nameof(GoAmlDailyCheck), check.Id,
                    "ImportedFromGoAmlPackage", user, reason,
                    new
                    {
                        package.PackageId,
                        check.CheckDate,
                        check.Status,
                        check.CompletedBy,
                        check.MessageReference,
                        check.ActionOwner,
                        check.ActionDueDate,
                        check.ComplianceTaskId,
                        check.EvidenceFileName,
                        check.EvidenceSizeBytes,
                        check.EvidenceSha256
                    }));
                await db.SaveChangesAsync(cancellationToken);
                imported++;
            }

            var record = NewRecord(package, GoAmlTransferDirections.Incoming,
                GoAmlTransferStatuses.Applied, preview.ContentSha256, incomingFileName, incomingPath);
            record.AppliedAtUtc = DateTime.UtcNow;
            record.AppliedBy = user;
            db.GoAmlTransferRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            db.ComplianceAuditEvents.Add(CreateAudit(nameof(GoAmlTransferRecord), checked((int)record.Id),
                "GoAmlPackageApplied", user, reason,
                new
                {
                    package.PackageId,
                    ImportedChecks = imported,
                    SkippedChecks = preview.ExistingCheckCount,
                    EvidenceImported = evidenceImported,
                    TasksCreated = tasksCreated,
                    FirstCheckDate = package.Checks.Min(item => item.CheckDate),
                    LastCheckDate = package.Checks.Max(item => item.CheckDate)
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new GoAmlImportResult(package.PackageId, imported, preview.ExistingCheckCount,
                evidenceImported, tasksCreated, incomingFileName, incomingPath);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var path in createdEvidencePaths.Where(File.Exists)) File.Delete(path);
            if (incomingCreated && File.Exists(incomingPath)) File.Delete(incomingPath);
            throw;
        }
    }

    private static async Task<GoAmlCheckPackage> BuildCheckPackageAsync(
        GoAmlDailyCheck check,
        CancellationToken cancellationToken)
    {
        GoAmlEvidencePackage? evidence = null;
        if (!string.IsNullOrWhiteSpace(check.EvidencePath))
        {
            if (!File.Exists(check.EvidencePath))
            {
                throw new InvalidOperationException(
                    $"Screenshot evidence for {check.CheckDate:yyyy-MM-dd} is missing from disk.");
            }
            var info = new FileInfo(check.EvidencePath);
            if (info.Length > MaximumEvidenceBytes)
            {
                throw new ValidationException($"Screenshot evidence for {check.CheckDate:yyyy-MM-dd} exceeds 5 MB.");
            }
            var content = await File.ReadAllBytesAsync(check.EvidencePath, cancellationToken);
            var sha256 = Sha256(content);
            if (!string.IsNullOrWhiteSpace(check.EvidenceSha256) &&
                !sha256.Equals(check.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Screenshot evidence for {check.CheckDate:yyyy-MM-dd} no longer matches its recorded hash.");
            }
            evidence = new GoAmlEvidencePackage
            {
                OriginalFileName = check.EvidenceFileName ?? Path.GetFileName(check.EvidencePath),
                ContentType = check.EvidenceContentType ?? "image/jpeg",
                Sha256 = sha256,
                Content = content
            };
        }

        return new GoAmlCheckPackage
        {
            CheckDate = check.CheckDate,
            Status = check.Status,
            StartedAtUtc = check.StartedAtUtc,
            StartedBy = check.StartedBy,
            CompletedAtUtc = check.CompletedAtUtc,
            CompletedBy = check.CompletedBy,
            Notes = check.Notes,
            MessageReference = check.MessageReference,
            ActionOwner = check.ActionOwner,
            ActionDueDate = check.ActionDueDate,
            Evidence = evidence
        };
    }

    private static void ValidateCheck(GoAmlCheckPackage check, List<string> conflicts)
    {
        if (!GoAmlCheckStatuses.Completed.Contains(check.Status, StringComparer.Ordinal))
            conflicts.Add($"{check.CheckDate:yyyy-MM-dd} is not a completed goAML check.");
        if (check.CompletedAtUtc is null || string.IsNullOrWhiteSpace(check.CompletedBy))
            conflicts.Add($"{check.CheckDate:yyyy-MM-dd} has incomplete completion metadata.");
        if (string.IsNullOrWhiteSpace(check.StartedBy))
            conflicts.Add($"{check.CheckDate:yyyy-MM-dd} has no checker identity.");
        if (check.Status != GoAmlCheckStatuses.Unavailable && check.Evidence is null)
            conflicts.Add($"{check.CheckDate:yyyy-MM-dd} is missing required screenshot evidence.");
        if (check.Status == GoAmlCheckStatuses.Unavailable && check.Evidence is null && string.IsNullOrWhiteSpace(check.Notes))
            conflicts.Add($"{check.CheckDate:yyyy-MM-dd} has neither evidence nor an outage description.");
        if (check.Status == GoAmlCheckStatuses.ActionRequired &&
            (string.IsNullOrWhiteSpace(check.MessageReference) || string.IsNullOrWhiteSpace(check.ActionOwner) || check.ActionDueDate is null))
            conflicts.Add($"{check.CheckDate:yyyy-MM-dd} has incomplete action-required details.");
        if (check.Evidence is not null)
        {
            if (!string.Equals(check.Evidence.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                conflicts.Add($"{check.CheckDate:yyyy-MM-dd} evidence is not JPEG.");
            if (check.Evidence.Content.Length is 0 or > MaximumEvidenceBytes)
                conflicts.Add($"{check.CheckDate:yyyy-MM-dd} evidence has an invalid size.");
            if (!Sha256(check.Evidence.Content).Equals(check.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                conflicts.Add($"{check.CheckDate:yyyy-MM-dd} evidence failed its SHA-256 integrity check.");
        }
    }

    private static bool Equivalent(GoAmlDailyCheck target, GoAmlCheckPackage source) =>
        target.Status == source.Status &&
        target.StartedAtUtc == source.StartedAtUtc &&
        string.Equals(target.StartedBy, source.StartedBy, StringComparison.Ordinal) &&
        target.CompletedAtUtc == source.CompletedAtUtc &&
        string.Equals(target.CompletedBy, source.CompletedBy, StringComparison.Ordinal) &&
        string.Equals(target.Notes, NormalizeOrNull(source.Notes), StringComparison.Ordinal) &&
        string.Equals(target.MessageReference, NormalizeOrNull(source.MessageReference), StringComparison.Ordinal) &&
        string.Equals(target.ActionOwner, NormalizeOrNull(source.ActionOwner), StringComparison.Ordinal) &&
        target.ActionDueDate == source.ActionDueDate &&
        EvidenceEquivalent(target, source.Evidence);

    private static bool EvidenceEquivalent(GoAmlDailyCheck target, GoAmlEvidencePackage? source)
    {
        if (source is null)
        {
            return string.IsNullOrWhiteSpace(target.EvidenceSha256) &&
                   string.IsNullOrWhiteSpace(target.EvidencePath);
        }
        if (!string.Equals(target.EvidenceSha256, source.Sha256, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(target.EvidencePath) ||
            !File.Exists(target.EvidencePath))
        {
            return false;
        }
        try
        {
            var info = new FileInfo(target.EvidencePath);
            if (info.Length != source.Content.LongLength) return false;
            using var stream = File.OpenRead(target.EvidencePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return hash.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private GoAmlTransferPackage DecryptPackage(byte[] encrypted, string passphrase, out string contentSha256)
    {
        try
        {
            var plaintext = Decrypt(encrypted, passphrase);
            contentSha256 = Sha256(plaintext);
            var package = JsonSerializer.Deserialize<GoAmlTransferPackage>(plaintext, JsonOptions)
                ?? throw new ValidationException("The package payload is empty.");
            if (!Guid.TryParse(package.PackageId, out _) || package.Checks is null)
                throw new ValidationException("The package payload is incomplete.");
            return package;
        }
        catch (CryptographicException)
        {
            throw new ValidationException("The package could not be decrypted. Check the passphrase and package integrity.");
        }
        catch (JsonException)
        {
            throw new ValidationException("The decrypted package is not valid KCAS goAML data.");
        }
        catch (EndOfStreamException)
        {
            throw new ValidationException("The encrypted package is truncated or invalid.");
        }
        catch (IOException)
        {
            throw new ValidationException("The encrypted package could not be read.");
        }
    }

    private static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(PackageMagic));
        CryptographicOperations.ZeroMemory(key);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(PackageMagic);
        writer.Write(Pbkdf2Iterations);
        writer.Write(salt.Length); writer.Write(salt);
        writer.Write(nonce.Length); writer.Write(nonce);
        writer.Write(tag.Length); writer.Write(tag);
        writer.Write(ciphertext.Length); writer.Write(ciphertext);
        return stream.ToArray();
    }

    private static byte[] Decrypt(byte[] encrypted, string passphrase)
    {
        if (encrypted.Length > MaximumPackageBytes + 1024)
            throw new ValidationException("The package exceeds the 50 MB safety limit.");
        using var stream = new MemoryStream(encrypted);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadString() != PackageMagic)
            throw new ValidationException("This is not a KCAS goAML transfer package.");
        var iterations = reader.ReadInt32();
        if (iterations is < 100_000 or > 1_000_000)
            throw new ValidationException("The package encryption parameters are invalid.");
        var salt = ReadExact(reader, 16, "salt");
        var nonce = ReadExact(reader, 12, "nonce");
        var tag = ReadExact(reader, 16, "authentication tag");
        var length = reader.ReadInt32();
        if (length is < 1 or > MaximumPackageBytes || length > stream.Length - stream.Position)
            throw new ValidationException("The package payload length is invalid.");
        var ciphertext = reader.ReadBytes(length);
        var plaintext = new byte[ciphertext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(PackageMagic));
        CryptographicOperations.ZeroMemory(key);
        return plaintext;
    }

    private static byte[] ReadExact(BinaryReader reader, int expectedLength, string label)
    {
        if (reader.ReadInt32() != expectedLength) throw new ValidationException($"The package {label} is invalid.");
        var value = reader.ReadBytes(expectedLength);
        if (value.Length != expectedLength) throw new ValidationException($"The package {label} is truncated.");
        return value;
    }

    private string ResolveStorageRoot()
    {
        var configured = configuration["GoAmlTransfers:StorageRoot"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        const string productionSharedRoot = @"D:\Deploy\KCAS\shared";
        if (Directory.Exists(productionSharedRoot)) return Path.Combine(productionSharedRoot, "goaml-check-packages");
        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "backups", "goaml-check-packages"));
    }

    private static GoAmlTransferRecord NewRecord(GoAmlTransferPackage package, string direction,
        string status, string hash, string fileName, string path) => new()
        {
            PackageId = package.PackageId,
            Direction = direction,
            ContentSha256 = hash,
            Status = status,
            FileName = fileName,
            StoragePath = path,
            FirstCheckDate = package.Checks.Min(item => item.CheckDate),
            LastCheckDate = package.Checks.Max(item => item.CheckDate),
            CheckCount = package.Checks.Count,
            SummaryJson = JsonSerializer.Serialize(PackageSummary(package), JsonOptions)
        };

    private static object PackageSummary(GoAmlTransferPackage package) => new
    {
        package.PackageId,
        package.CreatedAtUtc,
        package.ExportedBy,
        package.SourceEnvironment,
        FirstCheckDate = package.Checks.Min(item => item.CheckDate),
        LastCheckDate = package.Checks.Max(item => item.CheckDate),
        CheckCount = package.Checks.Count,
        EvidenceCount = package.Checks.Count(item => item.Evidence is not null),
        ActionRequiredCount = package.Checks.Count(item => item.Status == GoAmlCheckStatuses.ActionRequired)
    };

    private static ComplianceAuditEvent CreateAudit(string entityType, int entityId, string action,
        string user, string reason, object value) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserName = user,
            Reason = reason,
            NewValueJson = JsonSerializer.Serialize(value, JsonOptions),
            TimestampUtc = DateTime.UtcNow
        };

    private static void ValidateDateRange(DateOnly first, DateOnly last)
    {
        if (last < first) throw new ValidationException("The last check date cannot be before the first check date.");
        if (last.DayNumber - first.DayNumber + 1 > MaximumChecks)
            throw new ValidationException($"A goAML package cannot span more than {MaximumChecks} days.");
    }

    private static string ValidateEvidenceRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new ValidationException("The live goAML evidence folder must be an absolute path.");
        var full = Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("The live goAML evidence folder cannot be a drive root.");
        Directory.CreateDirectory(full);
        return full;
    }

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 12)
            throw new ValidationException("Use a package passphrase of at least 12 characters.");
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();

    private static string BuildFileName(GoAmlTransferPackage package) =>
        $"KCAS-goAML-{package.Checks.Min(item => item.CheckDate):yyyyMMdd}-" +
        $"{package.Checks.Max(item => item.CheckDate):yyyyMMdd}-{package.PackageId.Replace("-", "")[..12]}.kcas-goaml";

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class GoAmlTransferPackage
{
    public int FormatVersion { get; set; }
    public string PackageId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string ExportedBy { get; set; } = "";
    public string ExportReason { get; set; } = "";
    public string SourceEnvironment { get; set; } = "";
    public List<GoAmlCheckPackage> Checks { get; set; } = [];
}

public sealed class GoAmlCheckPackage
{
    public DateOnly CheckDate { get; set; }
    public string Status { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public string StartedBy { get; set; } = "";
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedBy { get; set; }
    public string? Notes { get; set; }
    public string? MessageReference { get; set; }
    public string? ActionOwner { get; set; }
    public DateOnly? ActionDueDate { get; set; }
    public GoAmlEvidencePackage? Evidence { get; set; }
}

public sealed class GoAmlEvidencePackage
{
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public string Sha256 { get; set; } = "";
    public byte[] Content { get; set; } = [];
}

public sealed class GoAmlTransferPreview
{
    public GoAmlTransferPackage Package { get; init; } = new();
    public string ContentSha256 { get; init; } = "";
    public bool AlreadyApplied { get; init; }
    public int ExistingCheckCount { get; init; }
    public int NewCheckCount { get; init; }
    public int EvidenceCount { get; init; }
    public List<string> Conflicts { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool CanApply => !AlreadyApplied && Conflicts.Count == 0;
}

public sealed record GoAmlExportResult(string PackageId, string FileName, string StoragePath,
    long SizeBytes, string ContentSha256, int CheckCount, int EvidenceCount,
    DateOnly FirstCheckDate, DateOnly LastCheckDate);

public sealed record GoAmlExportRange(DateOnly FirstCheckDate, DateOnly LastCheckDate);

public sealed record GoAmlImportResult(string PackageId, int ChecksImported, int ChecksSkipped,
    int EvidenceImported, int TasksCreated, string FileName, string StoragePath);
