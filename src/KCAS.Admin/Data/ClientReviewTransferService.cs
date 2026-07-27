using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientReviewTransferService(
    ApplicationDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    private const string PackageMagic = "KCAS-CLIENT-REVIEW-1";
    private const int PackageVersion = 1;
    private const int Pbkdf2Iterations = 300_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string StorageRoot => ResolveStorageRoot();

    public async Task<List<InvestmentSummaryClientOption>> LoadClientOptionsAsync(
        CancellationToken cancellationToken = default) =>
        await db.Clients.AsNoTracking()
            .Where(client => client.RiskAssessments.Any(assessment =>
                assessment.Status == ClientRiskAssessmentStatuses.Finalised ||
                assessment.Status == ClientRiskAssessmentStatuses.Approved))
            .OrderBy(client => client.DisplayName)
            .Select(client => new InvestmentSummaryClientOption(
                client.Id, client.KanaanId, client.DisplayName, client.LifecycleStatus))
            .ToListAsync(cancellationToken);

    public async Task<ClientReviewExportResult> ExportAsync(
        int clientId,
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var user = Require(userName, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");

        var client = await db.Clients.AsNoTracking()
            .Include(item => item.EvidenceItems).ThenInclude(item => item.Requirement)
            .Include(item => item.EvidenceExceptions).ThenInclude(item => item.Requirement)
            .Include(item => item.VerificationItems)
            .Include(item => item.RiskAssessments)
                .ThenInclude(item => item.MethodologyVersion)
            .Include(item => item.RiskAssessments)
                .ThenInclude(item => item.Responses)
                    .ThenInclude(item => item.FactorDefinition)
            .Include(item => item.RiskAssessments)
                .ThenInclude(item => item.Responses)
                    .ThenInclude(item => item.SelectedOption)
            .Include(item => item.RiskAssessments)
                .ThenInclude(item => item.Responses)
                    .ThenInclude(item => item.EvidenceItem)
            .Include(item => item.RiskAssessments)
                .ThenInclude(item => item.Approvals)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == clientId, cancellationToken)
            ?? throw new KeyNotFoundException("Client not found.");

        var assessment = client.RiskAssessments
            .Where(item => item.Status is ClientRiskAssessmentStatuses.Finalised or
                ClientRiskAssessmentStatuses.Approved)
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Only a finalised or approved client assessment can be transferred.");

        var package = BuildPackage(client, assessment, user, reason);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        var contentSha256 = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
        var encrypted = Encrypt(plaintext, passphrase);
        var fileName =
            $"{Safe(client.KanaanId ?? client.LegacyClientId?.ToString() ?? client.Id.ToString())}" +
            $"-{package.PackageId}.kcas-review";
        var directory = Path.Combine(StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);

        var record = new ClientReviewTransferRecord
        {
            PackageId = package.PackageId,
            Direction = ClientReviewTransferDirections.Outgoing,
            ContentSha256 = contentSha256,
            ClientId = client.Id,
            Status = ClientReviewTransferStatuses.Exported,
            FileName = fileName,
            StoragePath = path,
            SummaryJson = JsonSerializer.Serialize(PackageSummary(package), JsonOptions)
        };
        db.ClientReviewTransferRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(ClientReviewTransferRecord),
            EntityId = checked((int)record.Id),
            Action = "ClientReviewPackageExported",
            NewValueJson = record.SummaryJson,
            UserName = user,
            Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);

        return new ClientReviewExportResult(
            package.PackageId, fileName, path, encrypted.Length, contentSha256);
    }

    public async Task<ClientReviewTransferPreview> PreviewAsync(
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
            conflicts.Add(
                $"Package format {package.FormatVersion} is not supported by this KCAS version.");
        }

        var client = await ResolveClientAsync(package.Client, cancellationToken);
        if (client is null)
        {
            conflicts.Add("No unique live client matches the package Legacy Client ID and Kanaan ID.");
        }

        var alreadyApplied = await db.ClientReviewTransferRecords.AsNoTracking().AnyAsync(record =>
            record.Direction == ClientReviewTransferDirections.Incoming &&
            record.Status == ClientReviewTransferStatuses.Applied &&
            (record.PackageId == package.PackageId || record.ContentSha256 == contentSha256),
            cancellationToken);
        if (alreadyApplied)
        {
            warnings.Add("This package, or identical package content, has already been applied.");
        }

        RiskMethodologyVersion? methodology = null;
        if (client is not null)
        {
            methodology = await db.RiskMethodologyVersions.AsNoTracking()
                .Include(item => item.Factors).ThenInclude(item => item.Options)
                .SingleOrDefaultAsync(item =>
                    item.Name == package.Assessment.MethodologyName &&
                    item.VersionLabel == package.Assessment.MethodologyVersionLabel,
                    cancellationToken);
            if (methodology is null)
            {
                conflicts.Add(
                    $"The live methodology '{package.Assessment.MethodologyName} " +
                    $"{package.Assessment.MethodologyVersionLabel}' was not found.");
            }

            if (client.LifecycleStatus != ClientLifecycleStatuses.Unreviewed &&
                client.LifecycleStatus != package.Client.LifecycleStatus)
            {
                conflicts.Add(
                    $"Live lifecycle is {client.LifecycleStatus}, but the package records " +
                    $"{package.Client.LifecycleStatus}.");
            }

            var hasAssessmentConflict = await db.ClientRiskAssessments.AsNoTracking().AnyAsync(item =>
                item.ClientId == client.Id &&
                item.Status != ClientRiskAssessmentStatuses.Superseded,
                cancellationToken);
            if (hasAssessmentConflict)
            {
                conflicts.Add(
                    "The live client already has an assessment in progress or a current assessment. " +
                    "The transfer will not overwrite it.");
            }
        }

        if (methodology is not null)
        {
            foreach (var response in package.Assessment.Responses)
            {
                var factor = methodology.Factors.SingleOrDefault(item =>
                    item.Code.Equals(response.FactorCode, StringComparison.OrdinalIgnoreCase));
                if (factor is null)
                {
                    conflicts.Add($"Risk factor '{response.FactorCode}' is missing on live.");
                    continue;
                }
                if (!factor.Options.Any(option =>
                        option.Code.Equals(response.OptionCode, StringComparison.OrdinalIgnoreCase)))
                {
                    conflicts.Add(
                        $"Option '{response.OptionCode}' for factor '{response.FactorCode}' is missing on live.");
                }
            }
        }

        var existingEvidenceCount = 0;
        if (client is not null)
        {
            var hashes = package.Evidence
                .Where(item => !string.IsNullOrWhiteSpace(item.FileSha256))
                .Select(item => item.FileSha256!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingHashes = await db.ClientEvidenceItems.AsNoTracking()
                .Where(item => item.ClientId == client.Id && item.FileSha256 != null)
                .Select(item => item.FileSha256!)
                .ToListAsync(cancellationToken);
            existingEvidenceCount = existingHashes.Count(hashes.Contains);
        }

        return new ClientReviewTransferPreview
        {
            Package = package,
            ContentSha256 = contentSha256,
            TargetClientId = client?.Id,
            TargetClientName = client?.DisplayName,
            AlreadyApplied = alreadyApplied,
            ExistingEvidenceCount = existingEvidenceCount,
            NewEvidenceCount = package.Evidence.Count - existingEvidenceCount,
            Conflicts = conflicts,
            Warnings = warnings
        };
    }

    public async Task<ClientReviewImportResult> ApplyAsync(
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
            throw new InvalidOperationException("This client review package has already been applied.");
        }
        if (!preview.CanApply || !preview.TargetClientId.HasValue)
        {
            throw new InvalidOperationException(
                "The package has unresolved conflicts and cannot be applied.");
        }

        var package = preview.Package;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var client = await db.Clients.SingleAsync(
            item => item.Id == preview.TargetClientId.Value, cancellationToken);
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .SingleAsync(item =>
                item.Name == package.Assessment.MethodologyName &&
                item.VersionLabel == package.Assessment.MethodologyVersionLabel,
                cancellationToken);

        client.LifecycleStatus = package.Client.LifecycleStatus;
        client.LifecycleReason = package.Client.LifecycleReason;
        client.LifecycleReviewedAtUtc = package.Client.LifecycleReviewedAtUtc;
        client.LifecycleReviewedBy = package.Client.LifecycleReviewedBy;
        client.IsActive = package.Client.LifecycleStatus == ClientLifecycleStatuses.Current;
        client.UpdatedAtUtc = DateTime.UtcNow;

        var requirements = await db.ClientEvidenceRequirements
            .Where(item => item.Status == ClientEvidenceRequirementStatuses.Active)
            .ToListAsync(cancellationToken);
        var evidenceByKey = new Dictionary<string, ClientEvidenceItem>(
            StringComparer.OrdinalIgnoreCase);
        var existingEvidence = await db.ClientEvidenceItems
            .Where(item => item.ClientId == client.Id)
            .ToListAsync(cancellationToken);
        foreach (var item in existingEvidence)
        {
            evidenceByKey[EvidenceKey(item.FileSha256, item.EvidenceType, item.FileName)] = item;
        }

        foreach (var source in package.Evidence)
        {
            var key = EvidenceKey(source.FileSha256, source.EvidenceType, source.FileName);
            if (evidenceByKey.ContainsKey(key))
            {
                continue;
            }

            var requirement = MatchRequirement(
                requirements, client.ClientCategory, source.RequirementEvidenceType, source.RequirementTitle);
            var item = new ClientEvidenceItem
            {
                ClientId = client.Id,
                ClientEvidenceRequirementId = requirement?.Id,
                EvidenceType = source.EvidenceType,
                Title = source.Title,
                SourcePath = source.SourcePath,
                RelativePath = source.RelativePath,
                FileName = source.FileName,
                FileSha256 = source.FileSha256,
                FileSizeBytes = source.FileSizeBytes,
                FileLastWriteTimeUtc = source.FileLastWriteTimeUtc,
                ReceivedDate = source.ReceivedDate,
                VerifiedDate = source.VerifiedDate,
                ExpiryDate = source.ExpiryDate,
                Reviewer = source.Reviewer,
                ScreeningReviewDate = source.ScreeningReviewDate,
                ScreeningSubjectType = source.ScreeningSubjectType,
                ScreeningSubjectName = source.ScreeningSubjectName,
                ScreeningOutcome = source.ScreeningOutcome,
                ScreeningRiskSignal = source.ScreeningRiskSignal,
                EscalationRequired = source.EscalationRequired,
                Status = source.Status,
                OwnershipStatus = source.OwnershipStatus,
                OwnershipConfidence = source.OwnershipConfidence,
                OwnershipReason = source.OwnershipReason,
                OwnershipReviewedAtUtc = source.OwnershipReviewedAtUtc,
                OwnershipReviewedBy = source.OwnershipReviewedBy,
                SelectionStatus = source.SelectionStatus,
                SelectionConfidence = source.SelectionConfidence,
                SelectionReason = source.SelectionReason,
                SelectedAtUtc = source.SelectedAtUtc,
                SelectedBy = source.SelectedBy,
                VerificationPolicy = source.VerificationPolicy,
                Notes = source.Notes,
                CreatedAtUtc = source.CreatedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = user
            };
            db.ClientEvidenceItems.Add(item);
            evidenceByKey[key] = item;
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var source in package.Exceptions)
        {
            var requirement = MatchRequirement(
                requirements, client.ClientCategory, source.RequirementEvidenceType, source.RequirementTitle);
            if (requirement is null)
            {
                throw new InvalidOperationException(
                    $"Evidence requirement '{source.RequirementTitle}' is missing on live.");
            }
            var exists = await db.ClientEvidenceExceptions.AnyAsync(item =>
                item.ClientId == client.Id &&
                item.ClientEvidenceRequirementId == requirement.Id &&
                item.IsActive, cancellationToken);
            if (!exists)
            {
                db.ClientEvidenceExceptions.Add(new ClientEvidenceException
                {
                    ClientId = client.Id,
                    ClientEvidenceRequirementId = requirement.Id,
                    Reason = source.Reason,
                    ApprovedBy = source.ApprovedBy,
                    ApprovedAtUtc = source.ApprovedAtUtc,
                    ReviewDate = source.ReviewDate,
                    IsActive = source.IsActive
                });
            }
        }

        foreach (var source in package.VerificationItems)
        {
            var exists = await db.ClientVerificationItems.AnyAsync(item =>
                item.ClientId == client.Id &&
                item.FieldCode == source.FieldCode &&
                item.SourceReference == source.SourceReference &&
                item.Status == source.Status, cancellationToken);
            if (!exists)
            {
                db.ClientVerificationItems.Add(new ClientVerificationItem
                {
                    ClientId = client.Id,
                    FieldCode = source.FieldCode,
                    FieldLabel = source.FieldLabel,
                    ChangeType = source.ChangeType,
                    ExistingValue = source.ExistingValue,
                    ProposedValue = source.ProposedValue,
                    SourceReference = source.SourceReference,
                    Recommendation = source.Recommendation,
                    Status = source.Status,
                    IsBlocking = source.IsBlocking,
                    CreatedAtUtc = source.CreatedAtUtc,
                    CreatedBy = source.CreatedBy,
                    DecidedAtUtc = source.DecidedAtUtc,
                    DecidedBy = source.DecidedBy,
                    DecisionReason = source.DecisionReason,
                    AppliedAtUtc = source.AppliedAtUtc,
                    AppliedBy = source.AppliedBy
                });
            }
        }

        var assessment = new ClientRiskAssessment
        {
            ClientId = client.Id,
            RiskMethodologyVersionId = methodology.Id,
            Status = package.Assessment.Status,
            CalculatedScore = package.Assessment.CalculatedScore,
            CalculatedRating = package.Assessment.CalculatedRating,
            FinalRating = package.Assessment.FinalRating,
            IsOverride = package.Assessment.IsOverride,
            OverrideReason = package.Assessment.OverrideReason,
            HasPepExposure = package.Assessment.HasPepExposure,
            HasSanctionsConcern = package.Assessment.HasSanctionsConcern,
            HasAdverseInformation = package.Assessment.HasAdverseInformation,
            RequiresEdd = package.Assessment.RequiresEdd,
            StandardControlsApplied = package.Assessment.StandardControlsApplied,
            Narrative = package.Assessment.Narrative,
            EffectiveDate = package.Assessment.EffectiveDate,
            NextReviewDate = package.Assessment.NextReviewDate,
            CreatedAtUtc = package.Assessment.CreatedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow,
            FinalisedAtUtc = package.Assessment.FinalisedAtUtc,
            ApprovedAtUtc = package.Assessment.ApprovedAtUtc,
            ReviewTriggerType = package.Assessment.ReviewTriggerType,
            ReviewTriggerReason = package.Assessment.ReviewTriggerReason,
            ReviewTriggeredAtUtc = package.Assessment.ReviewTriggeredAtUtc,
            PreparedBy = package.Assessment.PreparedBy,
            FinalisedBy = package.Assessment.FinalisedBy,
            ReviewTriggeredBy = package.Assessment.ReviewTriggeredBy,
            SnapshotJson = package.Assessment.SnapshotJson
        };
        foreach (var source in package.Assessment.Responses)
        {
            var factor = methodology.Factors.Single(item =>
                item.Code.Equals(source.FactorCode, StringComparison.OrdinalIgnoreCase));
            var option = factor.Options.Single(item =>
                item.Code.Equals(source.OptionCode, StringComparison.OrdinalIgnoreCase));
            evidenceByKey.TryGetValue(source.EvidenceKey ?? "", out var evidence);
            assessment.Responses.Add(new ClientRiskAssessmentResponse
            {
                RiskFactorDefinitionId = factor.Id,
                RiskFactorOptionId = option.Id,
                ClientEvidenceItemId = evidence?.Id,
                Score = source.Score,
                WeightedScore = source.WeightedScore,
                Explanation = source.Explanation,
                ConfirmedAtUtc = source.ConfirmedAtUtc,
                ConfirmedBy = source.ConfirmedBy
            });
        }
        foreach (var source in package.Assessment.Approvals)
        {
            assessment.Approvals.Add(new ClientRiskAssessmentApproval
            {
                Approver = source.Approver,
                Decision = source.Decision,
                Reason = source.Reason,
                DecidedAtUtc = source.DecidedAtUtc
            });
        }
        db.ClientRiskAssessments.Add(assessment);
        await db.SaveChangesAsync(cancellationToken);

        var incomingDirectory = Path.Combine(StorageRoot, "incoming");
        Directory.CreateDirectory(incomingDirectory);
        var fileName = $"{Safe(client.KanaanId ?? client.Id.ToString())}-{package.PackageId}.kcas-review";
        var storagePath = Path.Combine(incomingDirectory, fileName);
        await File.WriteAllBytesAsync(storagePath, encryptedPackage, cancellationToken);
        var record = new ClientReviewTransferRecord
        {
            PackageId = package.PackageId,
            Direction = ClientReviewTransferDirections.Incoming,
            ContentSha256 = preview.ContentSha256,
            ClientId = client.Id,
            Status = ClientReviewTransferStatuses.Applied,
            FileName = fileName,
            StoragePath = storagePath,
            SummaryJson = JsonSerializer.Serialize(PackageSummary(package), JsonOptions),
            AppliedAtUtc = DateTime.UtcNow,
            AppliedBy = user
        };
        db.ClientReviewTransferRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(ClientReviewTransferRecord),
            EntityId = checked((int)record.Id),
            Action = "ClientReviewPackageApplied",
            NewValueJson = record.SummaryJson,
            UserName = user,
            Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ClientReviewImportResult(
            package.PackageId, client.Id, client.DisplayName, assessment.Id,
            preview.NewEvidenceCount, fileName, storagePath);
    }

    private static ClientReviewPackage BuildPackage(
        Client client,
        ClientRiskAssessment assessment,
        string exportedBy,
        string reason)
    {
        var evidence = client.EvidenceItems
            .Where(item =>
                item.VerifiedDate.HasValue &&
                item.Status == ClientEvidenceStatuses.Verified &&
                item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed &&
                item.SelectionStatus == ClientEvidenceSelectionStatuses.Current)
            .OrderBy(item => item.EvidenceType)
            .ThenBy(item => item.FileName)
            .Select(item => new ClientReviewEvidencePackage
            {
                EvidenceKey = EvidenceKey(item.FileSha256, item.EvidenceType, item.FileName),
                RequirementEvidenceType = item.Requirement?.EvidenceType,
                RequirementTitle = item.Requirement?.Title,
                EvidenceType = item.EvidenceType,
                Title = item.Title,
                SourcePath = item.SourcePath,
                RelativePath = item.RelativePath,
                FileName = item.FileName,
                FileSha256 = item.FileSha256,
                FileSizeBytes = item.FileSizeBytes,
                FileLastWriteTimeUtc = item.FileLastWriteTimeUtc,
                ReceivedDate = item.ReceivedDate,
                VerifiedDate = item.VerifiedDate,
                ExpiryDate = item.ExpiryDate,
                Reviewer = item.Reviewer,
                ScreeningReviewDate = item.ScreeningReviewDate,
                ScreeningSubjectType = item.ScreeningSubjectType,
                ScreeningSubjectName = item.ScreeningSubjectName,
                ScreeningOutcome = item.ScreeningOutcome,
                ScreeningRiskSignal = item.ScreeningRiskSignal,
                EscalationRequired = item.EscalationRequired,
                Status = item.Status,
                OwnershipStatus = item.OwnershipStatus,
                OwnershipConfidence = item.OwnershipConfidence,
                OwnershipReason = item.OwnershipReason,
                OwnershipReviewedAtUtc = item.OwnershipReviewedAtUtc,
                OwnershipReviewedBy = item.OwnershipReviewedBy,
                SelectionStatus = item.SelectionStatus,
                SelectionConfidence = item.SelectionConfidence,
                SelectionReason = item.SelectionReason,
                SelectedAtUtc = item.SelectedAtUtc,
                SelectedBy = item.SelectedBy,
                VerificationPolicy = item.VerificationPolicy,
                Notes = item.Notes,
                CreatedAtUtc = item.CreatedAtUtc
            })
            .ToList();

        return new ClientReviewPackage
        {
            FormatVersion = PackageVersion,
            PackageId = Guid.NewGuid().ToString("D"),
            CreatedAtUtc = DateTime.UtcNow,
            ExportedBy = exportedBy,
            ExportReason = reason,
            SourceEnvironment = Environment.MachineName,
            Client = new ClientReviewClientPackage
            {
                LegacyClientId = client.LegacyClientId,
                KanaanId = client.KanaanId,
                DisplayName = client.DisplayName,
                ClientCategory = client.ClientCategory,
                LifecycleStatus = client.LifecycleStatus,
                LifecycleReason = client.LifecycleReason,
                LifecycleReviewedAtUtc = client.LifecycleReviewedAtUtc,
                LifecycleReviewedBy = client.LifecycleReviewedBy
            },
            Evidence = evidence,
            Exceptions = client.EvidenceExceptions
                .Where(item => item.IsActive)
                .Select(item => new ClientReviewExceptionPackage
                {
                    RequirementEvidenceType = item.Requirement.EvidenceType,
                    RequirementTitle = item.Requirement.Title,
                    Reason = item.Reason,
                    ApprovedBy = item.ApprovedBy,
                    ApprovedAtUtc = item.ApprovedAtUtc,
                    ReviewDate = item.ReviewDate,
                    IsActive = item.IsActive
                }).ToList(),
            VerificationItems = client.VerificationItems
                .Where(item => item.Status != ClientVerificationStatuses.Pending)
                .Select(item => new ClientReviewVerificationPackage
                {
                    FieldCode = item.FieldCode,
                    FieldLabel = item.FieldLabel,
                    ChangeType = item.ChangeType,
                    ExistingValue = item.ExistingValue,
                    ProposedValue = item.ProposedValue,
                    SourceReference = item.SourceReference,
                    Recommendation = item.Recommendation,
                    Status = item.Status,
                    IsBlocking = item.IsBlocking,
                    CreatedAtUtc = item.CreatedAtUtc,
                    CreatedBy = item.CreatedBy,
                    DecidedAtUtc = item.DecidedAtUtc,
                    DecidedBy = item.DecidedBy,
                    DecisionReason = item.DecisionReason,
                    AppliedAtUtc = item.AppliedAtUtc,
                    AppliedBy = item.AppliedBy
                }).ToList(),
            Assessment = new ClientReviewAssessmentPackage
            {
                MethodologyName = assessment.MethodologyVersion!.Name,
                MethodologyVersionLabel = assessment.MethodologyVersion.VersionLabel,
                MethodologyStatus = assessment.MethodologyVersion.Status,
                Status = assessment.Status,
                CalculatedScore = assessment.CalculatedScore,
                CalculatedRating = assessment.CalculatedRating,
                FinalRating = assessment.FinalRating,
                IsOverride = assessment.IsOverride,
                OverrideReason = assessment.OverrideReason,
                HasPepExposure = assessment.HasPepExposure,
                HasSanctionsConcern = assessment.HasSanctionsConcern,
                HasAdverseInformation = assessment.HasAdverseInformation,
                RequiresEdd = assessment.RequiresEdd,
                StandardControlsApplied = assessment.StandardControlsApplied,
                Narrative = assessment.Narrative,
                EffectiveDate = assessment.EffectiveDate,
                NextReviewDate = assessment.NextReviewDate,
                CreatedAtUtc = assessment.CreatedAtUtc,
                FinalisedAtUtc = assessment.FinalisedAtUtc,
                ApprovedAtUtc = assessment.ApprovedAtUtc,
                ReviewTriggerType = assessment.ReviewTriggerType,
                ReviewTriggerReason = assessment.ReviewTriggerReason,
                ReviewTriggeredAtUtc = assessment.ReviewTriggeredAtUtc,
                PreparedBy = assessment.PreparedBy,
                FinalisedBy = assessment.FinalisedBy,
                ReviewTriggeredBy = assessment.ReviewTriggeredBy,
                SnapshotJson = assessment.SnapshotJson,
                Responses = assessment.Responses
                    .OrderBy(item => item.FactorDefinition!.SortOrder)
                    .Select(item => new ClientReviewResponsePackage
                    {
                        FactorCode = item.FactorDefinition!.Code,
                        OptionCode = item.SelectedOption!.Code,
                        EvidenceKey = item.EvidenceItem is null
                            ? null
                            : EvidenceKey(
                                item.EvidenceItem.FileSha256,
                                item.EvidenceItem.EvidenceType,
                                item.EvidenceItem.FileName),
                        Score = item.Score,
                        WeightedScore = item.WeightedScore,
                        Explanation = item.Explanation,
                        ConfirmedAtUtc = item.ConfirmedAtUtc,
                        ConfirmedBy = item.ConfirmedBy
                    }).ToList(),
                Approvals = assessment.Approvals.Select(item => new ClientReviewApprovalPackage
                {
                    Approver = item.Approver,
                    Decision = item.Decision,
                    Reason = item.Reason,
                    DecidedAtUtc = item.DecidedAtUtc
                }).ToList()
            }
        };
    }

    private async Task<Client?> ResolveClientAsync(
        ClientReviewClientPackage source,
        CancellationToken cancellationToken)
    {
        var matches = await db.Clients.AsNoTracking()
            .Where(item =>
                (source.LegacyClientId.HasValue && item.LegacyClientId == source.LegacyClientId) ||
                (!string.IsNullOrWhiteSpace(source.KanaanId) && item.KanaanId == source.KanaanId))
            .ToListAsync(cancellationToken);
        if (matches.Count != 1)
        {
            return null;
        }
        var match = matches[0];
        if (source.LegacyClientId.HasValue && match.LegacyClientId != source.LegacyClientId ||
            !string.IsNullOrWhiteSpace(source.KanaanId) &&
            !string.Equals(match.KanaanId, source.KanaanId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return match;
    }

    private ClientReviewPackage DecryptPackage(
        byte[] encrypted,
        string passphrase,
        out string contentSha256)
    {
        try
        {
            var plaintext = Decrypt(encrypted, passphrase);
            contentSha256 = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
            var package = JsonSerializer.Deserialize<ClientReviewPackage>(plaintext, JsonOptions)
                ?? throw new ValidationException("The package payload is empty.");
            if (string.IsNullOrWhiteSpace(package.PackageId) ||
                package.Client is null ||
                package.Assessment is null)
            {
                throw new ValidationException("The package payload is incomplete.");
            }
            return package;
        }
        catch (CryptographicException)
        {
            throw new ValidationException(
                "The package could not be decrypted. Check the passphrase and package integrity.");
        }
        catch (JsonException)
        {
            throw new ValidationException("The decrypted package is not valid KCAS review data.");
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
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
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
        using var stream = new MemoryStream(encrypted);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadString() != PackageMagic)
        {
            throw new ValidationException("This is not a KCAS client review package.");
        }
        var iterations = reader.ReadInt32();
        if (iterations < 100_000 || iterations > 1_000_000)
        {
            throw new ValidationException("The package encryption parameters are invalid.");
        }
        var salt = ReadExact(reader, 16, "salt");
        var nonce = ReadExact(reader, 12, "nonce");
        var tag = ReadExact(reader, 16, "authentication tag");
        var ciphertextLength = reader.ReadInt32();
        if (ciphertextLength < 1 || ciphertextLength > 25 * 1024 * 1024 ||
            ciphertextLength > stream.Length - stream.Position)
        {
            throw new ValidationException("The package payload length is invalid.");
        }
        var ciphertext = reader.ReadBytes(ciphertextLength);
        var plaintext = new byte[ciphertext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(PackageMagic));
        }
        CryptographicOperations.ZeroMemory(key);
        return plaintext;
    }

    private static byte[] ReadExact(BinaryReader reader, int expectedLength, string label)
    {
        if (reader.ReadInt32() != expectedLength)
        {
            throw new ValidationException($"The package {label} is invalid.");
        }
        var value = reader.ReadBytes(expectedLength);
        if (value.Length != expectedLength)
        {
            throw new ValidationException($"The package {label} is truncated.");
        }
        return value;
    }

    private string ResolveStorageRoot()
    {
        var configured = configuration["ClientReviewTransfers:StorageRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        const string productionSharedRoot = @"D:\Deploy\KCAS\shared";
        if (Directory.Exists(productionSharedRoot))
        {
            return Path.Combine(productionSharedRoot, "client-review-packages");
        }
        return Path.GetFullPath(Path.Combine(
            environment.ContentRootPath, "..", "..", "backups", "client-review-packages"));
    }

    private static ClientEvidenceRequirement? MatchRequirement(
        IEnumerable<ClientEvidenceRequirement> requirements,
        string clientCategory,
        string? evidenceType,
        string? title) =>
        requirements
            .Where(item =>
                item.ClientCategory == "All" || item.ClientCategory == clientCategory)
            .OrderByDescending(item => item.ClientCategory == clientCategory)
            .FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(title) &&
                item.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
        ?? requirements
            .Where(item =>
                item.ClientCategory == "All" || item.ClientCategory == clientCategory)
            .OrderByDescending(item => item.ClientCategory == clientCategory)
            .FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(evidenceType) &&
                item.EvidenceType.Equals(evidenceType, StringComparison.OrdinalIgnoreCase));

    private static object PackageSummary(ClientReviewPackage package) => new
    {
        package.PackageId,
        package.CreatedAtUtc,
        package.ExportedBy,
        package.SourceEnvironment,
        package.Client.LegacyClientId,
        package.Client.KanaanId,
        package.Client.DisplayName,
        EvidenceCount = package.Evidence.Count,
        ExceptionCount = package.Exceptions.Count,
        package.Assessment.MethodologyName,
        package.Assessment.MethodologyVersionLabel,
        package.Assessment.Status,
        package.Assessment.FinalRating,
        package.Assessment.EffectiveDate
    };

    private static string EvidenceKey(string? hash, string evidenceType, string? fileName) =>
        !string.IsNullOrWhiteSpace(hash)
            ? $"sha256:{hash.Trim().ToLowerInvariant()}"
            : $"meta:{evidenceType.Trim().ToLowerInvariant()}:{fileName?.Trim().ToLowerInvariant()}";

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 12)
        {
            throw new ValidationException("Use a package passphrase of at least 12 characters.");
        }
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "client" : safe;
    }
}

public sealed class ClientReviewPackage
{
    public int FormatVersion { get; set; }
    public string PackageId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string ExportedBy { get; set; } = "";
    public string ExportReason { get; set; } = "";
    public string SourceEnvironment { get; set; } = "";
    public ClientReviewClientPackage Client { get; set; } = new();
    public List<ClientReviewEvidencePackage> Evidence { get; set; } = [];
    public List<ClientReviewExceptionPackage> Exceptions { get; set; } = [];
    public List<ClientReviewVerificationPackage> VerificationItems { get; set; } = [];
    public ClientReviewAssessmentPackage Assessment { get; set; } = new();
}

public sealed class ClientReviewClientPackage
{
    public int? LegacyClientId { get; set; }
    public string? KanaanId { get; set; }
    public string DisplayName { get; set; } = "";
    public string ClientCategory { get; set; } = "";
    public string LifecycleStatus { get; set; } = "";
    public string? LifecycleReason { get; set; }
    public DateTime? LifecycleReviewedAtUtc { get; set; }
    public string? LifecycleReviewedBy { get; set; }
}

public sealed class ClientReviewEvidencePackage
{
    public string EvidenceKey { get; set; } = "";
    public string? RequirementEvidenceType { get; set; }
    public string? RequirementTitle { get; set; }
    public string EvidenceType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? RelativePath { get; set; }
    public string? FileName { get; set; }
    public string? FileSha256 { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? FileLastWriteTimeUtc { get; set; }
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? VerifiedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Reviewer { get; set; }
    public DateOnly? ScreeningReviewDate { get; set; }
    public string? ScreeningSubjectType { get; set; }
    public string? ScreeningSubjectName { get; set; }
    public string? ScreeningOutcome { get; set; }
    public string? ScreeningRiskSignal { get; set; }
    public bool EscalationRequired { get; set; }
    public string Status { get; set; } = "";
    public string OwnershipStatus { get; set; } = "";
    public int? OwnershipConfidence { get; set; }
    public string? OwnershipReason { get; set; }
    public DateTime? OwnershipReviewedAtUtc { get; set; }
    public string? OwnershipReviewedBy { get; set; }
    public string SelectionStatus { get; set; } = "";
    public int? SelectionConfidence { get; set; }
    public string? SelectionReason { get; set; }
    public DateTime? SelectedAtUtc { get; set; }
    public string? SelectedBy { get; set; }
    public string VerificationPolicy { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ClientReviewExceptionPackage
{
    public string RequirementEvidenceType { get; set; } = "";
    public string RequirementTitle { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; }
    public DateOnly? ReviewDate { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ClientReviewVerificationPackage
{
    public string FieldCode { get; set; } = "";
    public string FieldLabel { get; set; } = "";
    public string ChangeType { get; set; } = "";
    public string? ExistingValue { get; set; }
    public string? ProposedValue { get; set; }
    public string SourceReference { get; set; } = "";
    public string? Recommendation { get; set; }
    public string Status { get; set; } = "";
    public bool IsBlocking { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public string? AppliedBy { get; set; }
}

public sealed class ClientReviewAssessmentPackage
{
    public string MethodologyName { get; set; } = "";
    public string? MethodologyVersionLabel { get; set; }
    public string MethodologyStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal CalculatedScore { get; set; }
    public string? CalculatedRating { get; set; }
    public string? FinalRating { get; set; }
    public bool IsOverride { get; set; }
    public string? OverrideReason { get; set; }
    public bool HasPepExposure { get; set; }
    public bool HasSanctionsConcern { get; set; }
    public bool HasAdverseInformation { get; set; }
    public bool RequiresEdd { get; set; }
    public bool StandardControlsApplied { get; set; }
    public string? Narrative { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FinalisedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string ReviewTriggerType { get; set; } = "";
    public string? ReviewTriggerReason { get; set; }
    public DateTime? ReviewTriggeredAtUtc { get; set; }
    public string? PreparedBy { get; set; }
    public string? FinalisedBy { get; set; }
    public string? ReviewTriggeredBy { get; set; }
    public string? SnapshotJson { get; set; }
    public List<ClientReviewResponsePackage> Responses { get; set; } = [];
    public List<ClientReviewApprovalPackage> Approvals { get; set; } = [];
}

public sealed class ClientReviewResponsePackage
{
    public string FactorCode { get; set; } = "";
    public string OptionCode { get; set; } = "";
    public string? EvidenceKey { get; set; }
    public int Score { get; set; }
    public decimal WeightedScore { get; set; }
    public string? Explanation { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? ConfirmedBy { get; set; }
}

public sealed class ClientReviewApprovalPackage
{
    public string Approver { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime DecidedAtUtc { get; set; }
}

public sealed class ClientReviewTransferPreview
{
    public ClientReviewPackage Package { get; init; } = new();
    public string ContentSha256 { get; init; } = "";
    public int? TargetClientId { get; init; }
    public string? TargetClientName { get; init; }
    public bool AlreadyApplied { get; init; }
    public int ExistingEvidenceCount { get; init; }
    public int NewEvidenceCount { get; init; }
    public List<string> Conflicts { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool CanApply => !AlreadyApplied && Conflicts.Count == 0;
}

public sealed record ClientReviewExportResult(
    string PackageId,
    string FileName,
    string StoragePath,
    long SizeBytes,
    string ContentSha256);

public sealed record ClientReviewImportResult(
    string PackageId,
    int ClientId,
    string ClientDisplayName,
    int AssessmentId,
    int EvidenceImported,
    string FileName,
    string StoragePath);
