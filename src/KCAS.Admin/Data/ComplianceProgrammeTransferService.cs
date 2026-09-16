using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ComplianceProgrammeTransferService(
    ApplicationDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    public const string DefaultLocalSignedFinalRoot =
        @"C:\Download\_kanaan\Compliance\FSCA inspections\2026\RMCP and Policy Approval\06 Signed final";
    public const string DefaultLiveSignedFinalRoot =
        @"E:\Userdata\Kanaan Trust\Compliance\FSCA inspections\2026\RMCP and Policy Approval\06 Signed final";

    private const string PackageMagic = "KCAS-COMPLIANCE-PROGRAMME-1";
    private const int PackageVersion = 1;
    private const int Pbkdf2Iterations = 300_000;
    private const int MaximumPackageBytes = 25 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string StorageRoot => ResolveStorageRoot();
    public string LocalSignedFinalRoot => NormalizeRoot(configuration["ComplianceProgrammeTransfers:LocalSignedFinalRoot"]
        ?? DefaultLocalSignedFinalRoot);
    public string LiveSignedFinalRoot => NormalizeRoot(configuration["ComplianceProgrammeTransfers:LiveSignedFinalRoot"]
        ?? DefaultLiveSignedFinalRoot);

    public async Task<ComplianceProgrammeTransferExportResult> ExportAsync(
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var user = Require(userName, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");

        var bra = await db.BusinessRiskAssessments.AsNoTracking()
            .Include(item => item.Items)
            .Include(item => item.Approvals)
            .Where(item => item.Status == ComplianceStatuses.Active)
            .OrderByDescending(item => item.ActivatedAtUtc ?? item.ApprovedAtUtc ?? item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active business risk assessment exists to transfer.");

        var rmcp = await db.RmcpVersions.AsNoTracking()
            .Include(item => item.Controls)
                .ThenInclude(item => item.BusinessRiskItem)
            .Where(item => item.Status == ComplianceStatuses.Active && item.BusinessRiskAssessmentId == bra.Id)
            .OrderByDescending(item => item.ActivatedAtUtc ?? item.ApprovedAtUtc ?? item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active RMCP linked to the active BRA exists to transfer.");

        var rmcpApprovals = await db.ComplianceApprovals.AsNoTracking()
            .Where(item => item.TargetEntityType == nameof(RmcpVersion) && item.TargetEntityId == rmcp.Id)
            .OrderBy(item => item.DecidedAtUtc)
            .ToListAsync(cancellationToken);

        var root = LocalSignedFinalRoot;
        var documents = (await db.ControlledDocuments.AsNoTracking()
                .Where(item => item.Location != null && item.Location != "")
                .OrderBy(item => item.Title)
                .ToListAsync(cancellationToken))
            .Where(item => IsUnderRoot(item.Location, root))
            .ToList();
        var evidence = (await db.ComplianceEvidence.AsNoTracking()
                .Where(item => item.Location != null && item.Location != "")
                .OrderBy(item => item.Title)
                .ToListAsync(cancellationToken))
            .Where(item => IsUnderRoot(item.Location, root))
            .ToList();

        if (documents.Count == 0 || evidence.Count == 0)
        {
            throw new InvalidOperationException("No signed-final controlled documents or evidence records were found to transfer.");
        }

        var package = new ComplianceProgrammeTransferPackage
        {
            FormatVersion = PackageVersion,
            PackageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            ExportedBy = user,
            ExportReason = reason,
            SourceEnvironment = environment.EnvironmentName,
            LocalSignedFinalRoot = root,
            LiveSignedFinalRoot = LiveSignedFinalRoot,
            BusinessRiskAssessment = FromBra(bra),
            RmcpVersion = FromRmcp(rmcp, rmcpApprovals),
            ControlledDocuments = documents.Select(FromDocument).ToList(),
            Evidence = evidence.Select(FromEvidence).ToList()
        };

        var validationMessages = ValidatePackage(package, validatePaths: true);
        if (validationMessages.Count > 0)
        {
            throw new InvalidOperationException("The programme bundle is not transferable: " +
                string.Join(" ", validationMessages));
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        if (plaintext.Length > MaximumPackageBytes)
        {
            throw new ValidationException("The programme package exceeds the 25 MB safety limit.");
        }

        var contentSha256 = Sha256(plaintext);
        var encrypted = Encrypt(plaintext, passphrase);
        var fileName = BuildFileName(package);
        var directory = Path.Combine(StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);

        var record = NewRecord(package, ComplianceProgrammeTransferDirections.Outgoing,
            ComplianceProgrammeTransferStatuses.Exported, contentSha256, fileName, path);
        db.ComplianceProgrammeTransferRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        db.ComplianceAuditEvents.Add(CreateAudit(nameof(ComplianceProgrammeTransferRecord), checked((int)record.Id),
            "ComplianceProgrammePackageExported", user, reason, PackageSummary(package)));
        await db.SaveChangesAsync(cancellationToken);

        return new ComplianceProgrammeTransferExportResult(package.PackageId, fileName, path, encrypted.Length,
            contentSha256, package.BusinessRiskAssessment.Name, package.RmcpVersion.Title,
            package.ControlledDocuments.Count, package.Evidence.Count);
    }

    public async Task<ComplianceProgrammeTransferPreview> PreviewAsync(
        byte[] encryptedPackage,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var package = DecryptPackage(encryptedPackage, passphrase, out var contentSha256);
        var conflicts = ValidatePackage(package, validatePaths: true);
        var warnings = new List<string>();

        var alreadyApplied = await db.ComplianceProgrammeTransferRecords.AsNoTracking().AnyAsync(record =>
            record.Direction == ComplianceProgrammeTransferDirections.Incoming &&
            record.Status == ComplianceProgrammeTransferStatuses.Applied &&
            (record.PackageId == package.PackageId || record.ContentSha256 == contentSha256),
            cancellationToken);
        if (alreadyApplied)
        {
            warnings.Add("This programme package, or identical package content, has already been applied.");
        }

        var existingBra = await db.BusinessRiskAssessments.AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Name == package.BusinessRiskAssessment.Name &&
                item.AssessmentYear == package.BusinessRiskAssessment.AssessmentYear,
                cancellationToken);
        if (existingBra is null)
        {
            warnings.Add("The business risk assessment will be created on this KCAS instance.");
        }
        else if (!Equivalent(existingBra, package.BusinessRiskAssessment))
        {
            warnings.Add("The matching business risk assessment exists and will be updated to match the package.");
        }

        var existingRmcp = await db.RmcpVersions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.VersionReference == package.RmcpVersion.VersionReference,
                cancellationToken);
        if (existingRmcp is null)
        {
            warnings.Add("The RMCP version will be created on this KCAS instance.");
        }
        else if (!Equivalent(existingRmcp, package.RmcpVersion))
        {
            warnings.Add("The matching RMCP version exists and will be updated to match the package.");
        }

        var missingFiles = package.AllLocations()
            .Select(MapToLiveRoot)
            .Where(item => !string.IsNullOrWhiteSpace(item) && !File.Exists(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (missingFiles.Count > 0)
        {
            warnings.Add("Some mapped live files do not currently exist. The records can still be imported, but evidence file opening will fail until the signed-final folder is present on live.");
        }

        return new ComplianceProgrammeTransferPreview
        {
            Package = package,
            ContentSha256 = contentSha256,
            AlreadyApplied = alreadyApplied,
            Conflicts = conflicts.Distinct(StringComparer.Ordinal).ToList(),
            Warnings = warnings,
            MappedLiveRoot = LiveSignedFinalRoot,
            MissingMappedFiles = missingFiles
        };
    }

    public async Task<ComplianceProgrammeTransferImportResult> ApplyAsync(
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
            throw new InvalidOperationException("This programme package has already been applied.");
        }
        if (!preview.CanApply)
        {
            throw new InvalidOperationException("The package has unresolved conflicts and cannot be applied.");
        }

        var package = preview.Package;
        var incomingDirectory = Path.Combine(StorageRoot, "incoming");
        Directory.CreateDirectory(incomingDirectory);
        var incomingFileName = BuildFileName(package);
        var incomingPath = Path.Combine(incomingDirectory, incomingFileName);
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

            var bra = await UpsertBraAsync(package.BusinessRiskAssessment, user, cancellationToken);
            var rmcp = await UpsertRmcpAsync(package.RmcpVersion, bra, user, cancellationToken);
            var documentCount = await UpsertDocumentsAsync(package.ControlledDocuments, user, cancellationToken);
            var evidenceCount = await UpsertEvidenceAsync(package.Evidence, bra.Id, rmcp.Id, user, cancellationToken);

            var record = NewRecord(package, ComplianceProgrammeTransferDirections.Incoming,
                ComplianceProgrammeTransferStatuses.Applied, preview.ContentSha256, incomingFileName, incomingPath);
            record.BusinessRiskAssessmentId = bra.Id;
            record.RmcpVersionId = rmcp.Id;
            record.AppliedAtUtc = DateTime.UtcNow;
            record.AppliedBy = user;
            db.ComplianceProgrammeTransferRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            db.ComplianceAuditEvents.Add(CreateAudit(nameof(ComplianceProgrammeTransferRecord), checked((int)record.Id),
                "ComplianceProgrammePackageApplied", user, reason,
                new
                {
                    package.PackageId,
                    BusinessRiskAssessmentId = bra.Id,
                    RmcpVersionId = rmcp.Id,
                    ControlledDocuments = documentCount,
                    Evidence = evidenceCount,
                    LiveSignedFinalRoot
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ComplianceProgrammeTransferImportResult(package.PackageId, bra.Id, rmcp.Id,
                documentCount, evidenceCount, incomingFileName, incomingPath);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            if (incomingCreated && File.Exists(incomingPath)) File.Delete(incomingPath);
            throw;
        }
    }

    private async Task<BusinessRiskAssessment> UpsertBraAsync(
        BusinessRiskAssessmentPackage source,
        string user,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var target = await db.BusinessRiskAssessments
            .Include(item => item.Items)
            .Include(item => item.Approvals)
            .FirstOrDefaultAsync(item => item.Name == source.Name && item.AssessmentYear == source.AssessmentYear,
                cancellationToken);
        if (target is null)
        {
            target = new BusinessRiskAssessment
            {
                CreatedAtUtc = now
            };
            db.BusinessRiskAssessments.Add(target);
        }

        target.Name = source.Name;
        target.AssessmentYear = source.AssessmentYear;
        target.AsAtDate = source.AsAtDate;
        target.Status = source.Status;
        target.Scope = source.Scope;
        target.MethodologyNarrative = source.MethodologyNarrative;
        target.ManagementJudgement = source.ManagementJudgement;
        target.Limitations = source.Limitations;
        target.RiskTolerance = source.RiskTolerance;
        target.PortfolioSnapshotJson = source.PortfolioSnapshotJson;
        target.SnapshotJson = source.SnapshotJson;
        target.SubmittedAtUtc = source.SubmittedAtUtc;
        target.ApprovedAtUtc = source.ApprovedAtUtc;
        target.ActivatedAtUtc = source.ActivatedAtUtc;
        target.PreparedBy = source.PreparedBy;
        target.UpdatedBy = user;
        target.UpdatedAtUtc = now;

        foreach (var prior in await db.BusinessRiskAssessments
                     .Where(item => item.Id != target.Id && item.Status == ComplianceStatuses.Active)
                     .ToListAsync(cancellationToken))
        {
            prior.Status = ComplianceStatuses.Superseded;
            prior.UpdatedAtUtc = now;
            prior.UpdatedBy = user;
        }

        db.BusinessRiskItems.RemoveRange(target.Items);
        target.Items = source.Items.Select(item => new BusinessRiskItem
        {
            Category = item.Category,
            RiskStatement = item.RiskStatement,
            EvidenceAndRationale = item.EvidenceAndRationale,
            Likelihood = item.Likelihood,
            Impact = item.Impact,
            InherentScore = item.InherentScore,
            InherentRating = item.InherentRating,
            KeyControls = item.KeyControls,
            ControlEffectiveness = item.ControlEffectiveness,
            ResidualRating = item.ResidualRating,
            ResidualRationale = item.ResidualRationale,
            TreatmentDecision = item.TreatmentDecision,
            Owner = item.Owner,
            DueDate = item.DueDate,
            SortOrder = item.SortOrder
        }).ToList();

        db.BusinessRiskApprovals.RemoveRange(target.Approvals);
        target.Approvals = source.Approvals.Select(item => new BusinessRiskApproval
        {
            Approver = item.Approver,
            Reason = item.Reason,
            ApprovedAtUtc = item.ApprovedAtUtc
        }).ToList();

        await db.SaveChangesAsync(cancellationToken);
        return target;
    }

    private async Task<RmcpVersion> UpsertRmcpAsync(
        RmcpVersionPackage source,
        BusinessRiskAssessment bra,
        string user,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var target = await db.RmcpVersions
            .Include(item => item.Controls)
            .FirstOrDefaultAsync(item => item.VersionReference == source.VersionReference, cancellationToken);
        if (target is null)
        {
            target = new RmcpVersion
            {
                CreatedAtUtc = now
            };
            db.RmcpVersions.Add(target);
        }

        target.BusinessRiskAssessmentId = bra.Id;
        target.Title = source.Title;
        target.VersionReference = source.VersionReference;
        target.Status = source.Status;
        target.Scope = source.Scope;
        target.Owner = source.Owner;
        target.ReviewMonths = source.ReviewMonths;
        target.EffectiveDate = source.EffectiveDate;
        target.NextReviewDate = source.NextReviewDate;
        target.SignedDocumentLocation = MapToLiveRoot(source.SignedDocumentLocation) ?? "";
        target.ApprovalResolutionLocation = MapToLiveRoot(source.ApprovalResolutionLocation) ?? "";
        target.ChangeSummary = source.ChangeSummary;
        target.SnapshotJson = source.SnapshotJson;
        target.SubmittedAtUtc = source.SubmittedAtUtc;
        target.ApprovedAtUtc = source.ApprovedAtUtc;
        target.ActivatedAtUtc = source.ActivatedAtUtc;
        target.PreparedBy = source.PreparedBy;
        target.UpdatedBy = user;
        target.UpdatedAtUtc = now;

        foreach (var prior in await db.RmcpVersions
                     .Where(item => item.Id != target.Id && item.Status == ComplianceStatuses.Active)
                     .ToListAsync(cancellationToken))
        {
            prior.Status = ComplianceStatuses.Superseded;
            prior.UpdatedAtUtc = now;
            prior.UpdatedBy = user;
        }

        var braItemsByCategory = await db.BusinessRiskItems
            .Where(item => item.BusinessRiskAssessmentId == bra.Id)
            .ToDictionaryAsync(item => item.Category, cancellationToken);

        db.RmcpControls.RemoveRange(target.Controls);
        target.Controls = source.Controls.Select(item => new RmcpControl
        {
            BusinessRiskItemId = item.BusinessRiskItemCategory is not null &&
                braItemsByCategory.TryGetValue(item.BusinessRiskItemCategory, out var riskItem)
                    ? riskItem.Id
                    : null,
            Domain = item.Domain,
            Code = item.Code,
            Title = item.Title,
            ProcedureSummary = item.ProcedureSummary,
            Owner = item.Owner,
            Frequency = item.Frequency,
            EvidenceExpectation = item.EvidenceExpectation,
            MonitoringMethod = item.MonitoringMethod,
            EscalationProcedure = item.EscalationProcedure,
            HasGap = item.HasGap,
            GapDescription = item.GapDescription,
            TreatmentOwner = item.TreatmentOwner,
            TreatmentDueDate = item.TreatmentDueDate,
            ComplianceTaskId = null,
            SortOrder = item.SortOrder
        }).ToList();

        await db.SaveChangesAsync(cancellationToken);

        var approvals = await db.ComplianceApprovals
            .Where(item => item.TargetEntityType == nameof(RmcpVersion) && item.TargetEntityId == target.Id)
            .ToListAsync(cancellationToken);
        db.ComplianceApprovals.RemoveRange(approvals);
        db.ComplianceApprovals.AddRange(source.Approvals.Select(item => new ComplianceApproval
        {
            TargetEntityType = nameof(RmcpVersion),
            TargetEntityId = target.Id,
            Decision = item.Decision,
            Approver = item.Approver,
            DecidedAtUtc = item.DecidedAtUtc,
            Reason = item.Reason
        }));
        await db.SaveChangesAsync(cancellationToken);
        return target;
    }

    private async Task<int> UpsertDocumentsAsync(
        IReadOnlyList<ControlledDocumentPackage> documents,
        string user,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var source in documents)
        {
            var target = await db.ControlledDocuments.FirstOrDefaultAsync(item =>
                    item.DocumentType == source.DocumentType &&
                    item.Title == source.Title &&
                    item.VersionReference == source.VersionReference,
                cancellationToken);
            if (target is null)
            {
                target = new ControlledDocument { CreatedAtUtc = DateTime.UtcNow };
                db.ControlledDocuments.Add(target);
            }

            target.DocumentType = source.DocumentType;
            target.Title = source.Title;
            target.Owner = source.Owner;
            target.VersionReference = source.VersionReference;
            target.Status = source.Status;
            target.EffectiveDate = source.EffectiveDate;
            target.NextReviewDate = source.NextReviewDate;
            target.Location = MapToLiveRoot(source.Location);
            target.Notes = source.Notes;
            target.UpdatedAtUtc = DateTime.UtcNow;
            target.UpdatedBy = user;
            count++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task<int> UpsertEvidenceAsync(
        IReadOnlyList<ComplianceEvidencePackage> evidence,
        int braId,
        int rmcpId,
        string user,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var source in evidence)
        {
            var linkedType = source.LinkedEntityType;
            var linkedId = source.LinkedEntityType switch
            {
                nameof(BusinessRiskAssessment) => braId,
                nameof(RmcpVersion) => rmcpId,
                _ => source.LinkedEntityId
            };

            var target = await db.ComplianceEvidence.FirstOrDefaultAsync(item =>
                    item.EvidenceType == source.EvidenceType &&
                    item.Title == source.Title &&
                    item.LinkedEntityType == linkedType &&
                    item.LinkedEntityId == linkedId,
                cancellationToken);
            if (target is null)
            {
                target = new ComplianceEvidence { CreatedAtUtc = DateTime.UtcNow };
                db.ComplianceEvidence.Add(target);
            }

            target.EvidenceType = source.EvidenceType;
            target.Title = source.Title;
            target.Source = source.Source;
            target.Location = MapToLiveRoot(source.Location);
            target.ReceivedDate = source.ReceivedDate;
            target.VerifiedDate = source.VerifiedDate;
            target.ExpiryDate = source.ExpiryDate;
            target.Reviewer = source.Reviewer;
            target.Notes = source.Notes;
            target.LinkedEntityType = linkedType;
            target.LinkedEntityId = linkedId;
            target.UpdatedAtUtc = DateTime.UtcNow;
            target.UpdatedBy = user;
            count++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    public async Task<ComplianceProgrammePackageFile?> OpenExportAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var record = await db.ComplianceProgrammeTransferRecords.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Direction == ComplianceProgrammeTransferDirections.Outgoing &&
                item.PackageId == packageId,
                cancellationToken);
        if (record is null || !File.Exists(record.StoragePath)) return null;
        return new ComplianceProgrammePackageFile(record.StoragePath, record.FileName);
    }

    public string? MapToLiveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var normalized = NormalizePath(path);
        var localRoot = LocalSignedFinalRoot;
        var liveRoot = LiveSignedFinalRoot;
        if (IsUnderRoot(normalized, liveRoot)) return normalized;
        if (!IsUnderRoot(normalized, localRoot))
        {
            throw new ValidationException(
                $"Path '{path}' is outside the recognised local/live signed-final roots and cannot be mapped safely.");
        }

        var relative = normalized[localRoot.Length..].TrimStart('\\', '/');
        return UsesWindowsSeparators(liveRoot)
            ? liveRoot + "\\" + relative.Replace('/', '\\')
            : NormalizePath(Path.Combine(liveRoot, relative));
    }

    private ComplianceProgrammeTransferPackage DecryptPackage(byte[] encrypted, string passphrase, out string contentSha256)
    {
        try
        {
            var plaintext = Decrypt(encrypted, passphrase);
            contentSha256 = Sha256(plaintext);
            var package = JsonSerializer.Deserialize<ComplianceProgrammeTransferPackage>(plaintext, JsonOptions)
                ?? throw new ValidationException("The package payload is empty.");
            if (!Guid.TryParse(package.PackageId, out _) || package.BusinessRiskAssessment is null || package.RmcpVersion is null)
            {
                throw new ValidationException("The package payload is incomplete.");
            }

            return package;
        }
        catch (CryptographicException)
        {
            throw new ValidationException("The package could not be decrypted. Check the passphrase and package integrity.");
        }
        catch (JsonException)
        {
            throw new ValidationException("The decrypted package is not valid KCAS programme transfer data.");
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

    private List<string> ValidatePackage(ComplianceProgrammeTransferPackage package, bool validatePaths)
    {
        var conflicts = new List<string>();
        if (package.FormatVersion != PackageVersion)
        {
            conflicts.Add($"Package format {package.FormatVersion} is not supported by this KCAS version.");
        }
        if (string.IsNullOrWhiteSpace(package.BusinessRiskAssessment.Name))
        {
            conflicts.Add("The package has no business risk assessment name.");
        }
        if (package.BusinessRiskAssessment.Items.Count == 0)
        {
            conflicts.Add("The package has no business risk assessment risk items.");
        }
        if (string.IsNullOrWhiteSpace(package.RmcpVersion.VersionReference))
        {
            conflicts.Add("The package has no RMCP version reference.");
        }
        if (package.RmcpVersion.Controls.Count == 0)
        {
            conflicts.Add("The package has no RMCP controls.");
        }
        if (package.ControlledDocuments.Count == 0)
        {
            conflicts.Add("The package has no controlled documents.");
        }
        if (package.Evidence.Count == 0)
        {
            conflicts.Add("The package has no compliance evidence records.");
        }
        if (!validatePaths) return conflicts;

        foreach (var location in package.AllLocations())
        {
            try
            {
                _ = MapToLiveRoot(location);
            }
            catch (ValidationException ex)
            {
                conflicts.Add(ex.Message);
            }
        }

        return conflicts;
    }

    private static BusinessRiskAssessmentPackage FromBra(BusinessRiskAssessment source) => new()
    {
        Name = source.Name,
        AssessmentYear = source.AssessmentYear,
        AsAtDate = source.AsAtDate,
        Status = source.Status,
        Scope = source.Scope,
        MethodologyNarrative = source.MethodologyNarrative,
        ManagementJudgement = source.ManagementJudgement,
        Limitations = source.Limitations,
        RiskTolerance = source.RiskTolerance,
        PortfolioSnapshotJson = source.PortfolioSnapshotJson,
        SnapshotJson = source.SnapshotJson,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc,
        SubmittedAtUtc = source.SubmittedAtUtc,
        ApprovedAtUtc = source.ApprovedAtUtc,
        ActivatedAtUtc = source.ActivatedAtUtc,
        PreparedBy = source.PreparedBy,
        UpdatedBy = source.UpdatedBy,
        Items = source.Items.OrderBy(item => item.SortOrder).Select(FromRiskItem).ToList(),
        Approvals = source.Approvals.OrderBy(item => item.ApprovedAtUtc).Select(FromBraApproval).ToList()
    };

    private static BusinessRiskItemPackage FromRiskItem(BusinessRiskItem source) => new()
    {
        Category = source.Category,
        RiskStatement = source.RiskStatement,
        EvidenceAndRationale = source.EvidenceAndRationale,
        Likelihood = source.Likelihood,
        Impact = source.Impact,
        InherentScore = source.InherentScore,
        InherentRating = source.InherentRating,
        KeyControls = source.KeyControls,
        ControlEffectiveness = source.ControlEffectiveness,
        ResidualRating = source.ResidualRating,
        ResidualRationale = source.ResidualRationale,
        TreatmentDecision = source.TreatmentDecision,
        Owner = source.Owner,
        DueDate = source.DueDate,
        SortOrder = source.SortOrder
    };

    private static BusinessRiskApprovalPackage FromBraApproval(BusinessRiskApproval source) => new()
    {
        Approver = source.Approver,
        Reason = source.Reason,
        ApprovedAtUtc = source.ApprovedAtUtc
    };

    private static RmcpVersionPackage FromRmcp(RmcpVersion source, IReadOnlyList<ComplianceApproval> approvals) => new()
    {
        Title = source.Title,
        VersionReference = source.VersionReference,
        Status = source.Status,
        Scope = source.Scope,
        Owner = source.Owner,
        ReviewMonths = source.ReviewMonths,
        EffectiveDate = source.EffectiveDate,
        NextReviewDate = source.NextReviewDate,
        SignedDocumentLocation = source.SignedDocumentLocation,
        ApprovalResolutionLocation = source.ApprovalResolutionLocation,
        ChangeSummary = source.ChangeSummary,
        SnapshotJson = source.SnapshotJson,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc,
        SubmittedAtUtc = source.SubmittedAtUtc,
        ApprovedAtUtc = source.ApprovedAtUtc,
        ActivatedAtUtc = source.ActivatedAtUtc,
        PreparedBy = source.PreparedBy,
        UpdatedBy = source.UpdatedBy,
        Controls = source.Controls.OrderBy(item => item.SortOrder).Select(FromControl).ToList(),
        Approvals = approvals.Select(FromComplianceApproval).ToList()
    };

    private static RmcpControlPackage FromControl(RmcpControl source) => new()
    {
        BusinessRiskItemCategory = source.BusinessRiskItem?.Category,
        Domain = source.Domain,
        Code = source.Code,
        Title = source.Title,
        ProcedureSummary = source.ProcedureSummary,
        Owner = source.Owner,
        Frequency = source.Frequency,
        EvidenceExpectation = source.EvidenceExpectation,
        MonitoringMethod = source.MonitoringMethod,
        EscalationProcedure = source.EscalationProcedure,
        HasGap = source.HasGap,
        GapDescription = source.GapDescription,
        TreatmentOwner = source.TreatmentOwner,
        TreatmentDueDate = source.TreatmentDueDate,
        SortOrder = source.SortOrder
    };

    private static ComplianceApprovalPackage FromComplianceApproval(ComplianceApproval source) => new()
    {
        Decision = source.Decision,
        Approver = source.Approver,
        DecidedAtUtc = source.DecidedAtUtc,
        Reason = source.Reason
    };

    private static ControlledDocumentPackage FromDocument(ControlledDocument source) => new()
    {
        DocumentType = source.DocumentType,
        Title = source.Title,
        Owner = source.Owner,
        VersionReference = source.VersionReference,
        Status = source.Status,
        EffectiveDate = source.EffectiveDate,
        NextReviewDate = source.NextReviewDate,
        Location = source.Location,
        Notes = source.Notes,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc,
        UpdatedBy = source.UpdatedBy
    };

    private static ComplianceEvidencePackage FromEvidence(ComplianceEvidence source) => new()
    {
        EvidenceType = source.EvidenceType,
        Title = source.Title,
        Source = source.Source,
        Location = source.Location,
        ReceivedDate = source.ReceivedDate,
        VerifiedDate = source.VerifiedDate,
        ExpiryDate = source.ExpiryDate,
        Reviewer = source.Reviewer,
        Notes = source.Notes,
        LinkedEntityType = source.LinkedEntityType,
        LinkedEntityId = source.LinkedEntityId,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc,
        UpdatedBy = source.UpdatedBy
    };

    private static bool Equivalent(BusinessRiskAssessment target, BusinessRiskAssessmentPackage source) =>
        target.Status == source.Status &&
        target.AsAtDate == source.AsAtDate &&
        target.SnapshotJson == source.SnapshotJson;

    private static bool Equivalent(RmcpVersion target, RmcpVersionPackage source) =>
        target.Status == source.Status &&
        target.VersionReference == source.VersionReference &&
        target.SnapshotJson == source.SnapshotJson;

    private static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(PackageMagic));
        }
        CryptographicOperations.ZeroMemory(key);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(PackageMagic);
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

    private static byte[] Decrypt(byte[] encrypted, string passphrase)
    {
        if (encrypted.Length > MaximumPackageBytes + 1024)
        {
            throw new ValidationException("The package exceeds the 25 MB safety limit.");
        }

        using var stream = new MemoryStream(encrypted);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadString() != PackageMagic)
        {
            throw new ValidationException("This is not a KCAS compliance programme transfer package.");
        }

        var iterations = reader.ReadInt32();
        if (iterations is < 100_000 or > 1_000_000)
        {
            throw new ValidationException("The package encryption parameters are invalid.");
        }

        var salt = ReadExact(reader, 16, "salt");
        var nonce = ReadExact(reader, 12, "nonce");
        var tag = ReadExact(reader, 16, "authentication tag");
        var length = reader.ReadInt32();
        if (length is < 1 or > MaximumPackageBytes || length > stream.Length - stream.Position)
        {
            throw new ValidationException("The package payload length is invalid.");
        }

        var ciphertext = reader.ReadBytes(length);
        var plaintext = new byte[ciphertext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(PackageMagic));
        }
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
        var configured = configuration["ComplianceProgrammeTransfers:StorageRoot"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        const string productionSharedRoot = @"D:\Deploy\KCAS\shared";
        if (Directory.Exists(productionSharedRoot)) return Path.Combine(productionSharedRoot, "programme-transfer-packages");
        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "backups", "programme-transfer-packages"));
    }

    private static ComplianceProgrammeTransferRecord NewRecord(
        ComplianceProgrammeTransferPackage package,
        string direction,
        string status,
        string hash,
        string fileName,
        string path) => new()
        {
            PackageId = package.PackageId,
            Direction = direction,
            ContentSha256 = hash,
            Status = status,
            FileName = fileName,
            StoragePath = path,
            ControlledDocumentCount = package.ControlledDocuments.Count,
            EvidenceCount = package.Evidence.Count,
            SummaryJson = JsonSerializer.Serialize(PackageSummary(package), JsonOptions)
        };

    private static object PackageSummary(ComplianceProgrammeTransferPackage package) => new
    {
        package.PackageId,
        package.CreatedAtUtc,
        package.ExportedBy,
        package.SourceEnvironment,
        BusinessRiskAssessment = package.BusinessRiskAssessment.Name,
        package.BusinessRiskAssessment.AssessmentYear,
        Rmcp = package.RmcpVersion.Title,
        package.RmcpVersion.VersionReference,
        ControlledDocuments = package.ControlledDocuments.Count,
        Evidence = package.Evidence.Count,
        package.LocalSignedFinalRoot,
        package.LiveSignedFinalRoot
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

    private static string BuildFileName(ComplianceProgrammeTransferPackage package) =>
        $"KCAS-programme-{package.BusinessRiskAssessment.AssessmentYear}-{package.PackageId.Replace("-", "")[..12]}.kcas-programme";

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 7)
        {
            throw new ValidationException("Use a package passphrase of at least 7 characters.");
        }
    }

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string NormalizeRoot(string value) =>
        NormalizePath(value).TrimEnd('\\', '/');

    private static string NormalizePath(string value)
    {
        var trimmed = value.Trim();
        return IsWindowsRootedPath(trimmed)
            ? trimmed.Replace('/', '\\')
            : Path.GetFullPath(trimmed);
    }

    private static bool IsUnderRoot(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var normalizedPath = NormalizePath(path).TrimEnd('\\', '/');
        var normalizedRoot = NormalizeRoot(root);
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UsesWindowsSeparators(string value) => IsWindowsRootedPath(value) || value.Contains('\\');
    private static bool IsWindowsRootedPath(string value) => Regex.IsMatch(value, "^[A-Za-z]:[\\\\/]");
}

public sealed class ComplianceProgrammeTransferPackage
{
    public int FormatVersion { get; set; }
    public string PackageId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string ExportedBy { get; set; } = "";
    public string ExportReason { get; set; } = "";
    public string SourceEnvironment { get; set; } = "";
    public string LocalSignedFinalRoot { get; set; } = "";
    public string LiveSignedFinalRoot { get; set; } = "";
    public BusinessRiskAssessmentPackage BusinessRiskAssessment { get; set; } = new();
    public RmcpVersionPackage RmcpVersion { get; set; } = new();
    public List<ControlledDocumentPackage> ControlledDocuments { get; set; } = [];
    public List<ComplianceEvidencePackage> Evidence { get; set; } = [];

    public IEnumerable<string?> AllLocations()
    {
        yield return RmcpVersion.SignedDocumentLocation;
        yield return RmcpVersion.ApprovalResolutionLocation;
        foreach (var document in ControlledDocuments) yield return document.Location;
        foreach (var item in Evidence) yield return item.Location;
    }
}

public sealed class BusinessRiskAssessmentPackage
{
    public string Name { get; set; } = "";
    public int AssessmentYear { get; set; }
    public DateOnly AsAtDate { get; set; }
    public string Status { get; set; } = "";
    public string Scope { get; set; } = "";
    public string MethodologyNarrative { get; set; } = "";
    public string ManagementJudgement { get; set; } = "";
    public string Limitations { get; set; } = "";
    public string RiskTolerance { get; set; } = "";
    public string? PortfolioSnapshotJson { get; set; }
    public string? SnapshotJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public string? PreparedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public List<BusinessRiskItemPackage> Items { get; set; } = [];
    public List<BusinessRiskApprovalPackage> Approvals { get; set; } = [];
}

public sealed class BusinessRiskItemPackage
{
    public string Category { get; set; } = "";
    public string RiskStatement { get; set; } = "";
    public string EvidenceAndRationale { get; set; } = "";
    public int Likelihood { get; set; }
    public int Impact { get; set; }
    public int InherentScore { get; set; }
    public string InherentRating { get; set; } = "";
    public string KeyControls { get; set; } = "";
    public string ControlEffectiveness { get; set; } = "";
    public string ResidualRating { get; set; } = "";
    public string ResidualRationale { get; set; } = "";
    public string TreatmentDecision { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BusinessRiskApprovalPackage
{
    public string Approver { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; }
}

public sealed class RmcpVersionPackage
{
    public string Title { get; set; } = "";
    public string VersionReference { get; set; } = "";
    public string Status { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Owner { get; set; } = "";
    public int ReviewMonths { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public string SignedDocumentLocation { get; set; } = "";
    public string ApprovalResolutionLocation { get; set; } = "";
    public string ChangeSummary { get; set; } = "";
    public string? SnapshotJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public string? PreparedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public List<RmcpControlPackage> Controls { get; set; } = [];
    public List<ComplianceApprovalPackage> Approvals { get; set; } = [];
}

public sealed class RmcpControlPackage
{
    public string? BusinessRiskItemCategory { get; set; }
    public string Domain { get; set; } = "";
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string ProcedureSummary { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string EvidenceExpectation { get; set; } = "";
    public string MonitoringMethod { get; set; } = "";
    public string EscalationProcedure { get; set; } = "";
    public bool HasGap { get; set; }
    public string? GapDescription { get; set; }
    public string? TreatmentOwner { get; set; }
    public DateOnly? TreatmentDueDate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ComplianceApprovalPackage
{
    public string Decision { get; set; } = "";
    public string? Approver { get; set; }
    public DateTime DecidedAtUtc { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class ControlledDocumentPackage
{
    public string DocumentType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Owner { get; set; }
    public string? VersionReference { get; set; }
    public string Status { get; set; } = "";
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ComplianceEvidencePackage
{
    public string EvidenceType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Source { get; set; }
    public string? Location { get; set; }
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? VerifiedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Reviewer { get; set; }
    public string? Notes { get; set; }
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ComplianceProgrammeTransferPreview
{
    public ComplianceProgrammeTransferPackage Package { get; init; } = new();
    public string ContentSha256 { get; init; } = "";
    public bool AlreadyApplied { get; init; }
    public string MappedLiveRoot { get; init; } = "";
    public List<string> Conflicts { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> MissingMappedFiles { get; init; } = [];
    public bool CanApply => !AlreadyApplied && Conflicts.Count == 0;
}

public sealed record ComplianceProgrammeTransferExportResult(
    string PackageId,
    string FileName,
    string StoragePath,
    long SizeBytes,
    string ContentSha256,
    string BusinessRiskAssessmentName,
    string RmcpTitle,
    int ControlledDocumentCount,
    int EvidenceCount);

public sealed record ComplianceProgrammeTransferImportResult(
    string PackageId,
    int BusinessRiskAssessmentId,
    int RmcpVersionId,
    int ControlledDocumentCount,
    int EvidenceCount,
    string FileName,
    string StoragePath);

public sealed record ComplianceProgrammePackageFile(string StoragePath, string FileName);
