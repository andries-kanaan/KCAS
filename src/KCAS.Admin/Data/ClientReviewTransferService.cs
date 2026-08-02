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
    private const int PackageVersion = 2;
    private const int Pbkdf2Iterations = 300_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string StorageRoot => ResolveStorageRoot();

    public async Task<List<ClientReviewTransferClientOption>> LoadClientOptionsAsync(
        CancellationToken cancellationToken = default) =>
        await db.Clients.AsNoTracking()
            .Where(client => client.RiskAssessments.Any(assessment =>
                assessment.Status == ClientRiskAssessmentStatuses.Finalised ||
                assessment.Status == ClientRiskAssessmentStatuses.Approved))
            .OrderBy(client => client.DisplayName)
            .ThenBy(client => client.SurnameOrEntityName)
            .Select(client => new ClientReviewTransferClientOption(
                client.Id,
                client.LegacyClientId,
                client.KanaanId,
                client.DisplayName,
                client.SurnameOrEntityName,
                client.LifecycleStatus))
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

        var payload = await CreateEmbeddedExportAsync(
            clientId, passphrase, user, reason, cancellationToken);
        var directory = Path.Combine(StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, payload.FileName);
        await File.WriteAllBytesAsync(path, payload.EncryptedPackage, cancellationToken);

        var record = new ClientReviewTransferRecord
        {
            PackageId = payload.Package.PackageId,
            Direction = ClientReviewTransferDirections.Outgoing,
            ContentSha256 = payload.ContentSha256,
            ClientId = clientId,
            Status = ClientReviewTransferStatuses.Exported,
            FileName = payload.FileName,
            StoragePath = path,
            SummaryJson = JsonSerializer.Serialize(PackageSummary(payload.Package), JsonOptions)
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
            payload.Package.PackageId,
            payload.FileName,
            path,
            payload.EncryptedPackage.Length,
            payload.ContentSha256);
    }

    internal async Task<ClientReviewEmbeddedExport> CreateEmbeddedExportAsync(
        int clientId,
        string passphrase,
        string user,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        user = Require(user, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");

        var client = await db.Clients.AsNoTracking()
            .Include(item => item.EntityProfile)
            .Include(item => item.RelatedParties).ThenInclude(item => item.Roles)
            .Include(item => item.RelatedParties).ThenInclude(item => item.EvidenceLinks).ThenInclude(item => item.EvidenceItem)
            .Include(item => item.InvestmentAccounts).ThenInclude(item => item.Transactions)
            .Include(item => item.InvestmentReconciliationReviews)
            .Include(item => item.FundValuations)
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
        var fileName = BuildPackageFileName(
            client.Id,
            client.SurnameOrEntityName,
            package.CreatedAtUtc,
            package.PackageId);
        return new ClientReviewEmbeddedExport(package, fileName, encrypted, contentSha256);
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
        ValidatePackageStructure(package, conflicts);

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
        string? targetClientFolder = null;
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
            if (!string.Equals(client.ClientCategory, package.Client.ClientCategory, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"Client category will change from {client.ClientCategory} to {package.Client.ClientCategory}.");
            }

            targetClientFolder = client.ClientFolder;
            if (!string.IsNullOrWhiteSpace(package.Client.ClientFolder))
            {
                var liveRoot = await LoadActiveClientFolderRootAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(liveRoot))
                {
                    conflicts.Add(
                        "The package contains a client folder, but live KCAS has no active client evidence root.");
                }
                else
                {
                    targetClientFolder = MapClientFolderToLiveRoot(package.Client.ClientFolder, liveRoot);
                    if (targetClientFolder is null)
                    {
                        conflicts.Add(
                            $"Client folder '{package.Client.ClientFolder}' is outside the recognised local/live client roots and cannot be mapped safely.");
                    }
                    else if (!string.Equals(client.ClientFolder, targetClientFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add(
                            $"Client folder will map from '{package.Client.ClientFolder}' to '{targetClientFolder}'.");
                    }
                }
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

            var liveAccounts = await db.ClientInvestmentAccounts.AsNoTracking()
                .Include(item => item.Transactions)
                .Where(item => item.ClientId == client.Id)
                .ToListAsync(cancellationToken);
            var liveValuations = await db.ClientFundValuations.AsNoTracking()
                .Where(item => item.ClientId == client.Id)
                .ToListAsync(cancellationToken);
            foreach (var source in package.InvestmentReconciliations)
            {
                var match = MatchInvestmentAccount(liveAccounts, source.LegacyInvestmentAccountId, source.AccountNumber, source.Administrator);
                if (match is null)
                {
                    conflicts.Add($"Investment account '{source.AccountNumber ?? source.LegacyInvestmentAccountId?.ToString() ?? "unknown"}' could not be matched uniquely on live.");
                    continue;
                }
                if (match.SurrenderDate != source.SurrenderDate)
                {
                    warnings.Add($"Investment {match.AccountNumber}: surrender/transfer date will change from {match.SurrenderDate?.ToString("yyyy-MM-dd") ?? "blank"} to {source.SurrenderDate?.ToString("yyyy-MM-dd") ?? "blank"}.");
                }
                if (source.RelatedLegacyInvestmentAccountId.HasValue || !string.IsNullOrWhiteSpace(source.RelatedAccountNumber))
                {
                    var related = MatchInvestmentAccount(liveAccounts, source.RelatedLegacyInvestmentAccountId, source.RelatedAccountNumber, source.RelatedAdministrator);
                    if (related is null)
                    {
                        conflicts.Add($"Related investment for '{source.AccountNumber}' could not be matched uniquely on live.");
                    }
                }
                var matchedValuations = ClientInvestmentStatusClassifier.MatchingValuations(match, liveValuations);
                var outcomeError = InvestmentReconciliationService.ValidateOutcome(
                    source.Outcome,
                    source.SurrenderDate,
                    source.RelatedLegacyInvestmentAccountId.HasValue || !string.IsNullOrWhiteSpace(source.RelatedAccountNumber),
                    matchedValuations);
                if (outcomeError is not null)
                {
                    conflicts.Add($"Investment {match.AccountNumber}: {outcomeError}");
                }
                var oldSurrenderDate = match.SurrenderDate;
                match.SurrenderDate = source.Outcome == ClientInvestmentReconciliationOutcomes.Current
                    ? null
                    : source.SurrenderDate;
                var portableSnapshot = CalculatePortableInvestmentSnapshot(match, matchedValuations);
                match.SurrenderDate = oldSurrenderDate;
                if (!string.Equals(source.PortableSnapshotSha256, portableSnapshot, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(
                        $"Investment {match.AccountNumber}: the live account, transactions or valuations differ from the reviewed package snapshot.");
                }
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
            TargetClientFolder = targetClientFolder,
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
        var client = await db.Clients
            .Include(item => item.EntityProfile)
            .Include(item => item.RelatedParties).ThenInclude(item => item.Roles)
            .Include(item => item.RelatedParties).ThenInclude(item => item.EvidenceLinks)
            .Include(item => item.InvestmentAccounts).ThenInclude(item => item.Transactions)
            .Include(item => item.FundValuations)
            .SingleAsync(item => item.Id == preview.TargetClientId.Value, cancellationToken);
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .SingleAsync(item =>
                item.Name == package.Assessment.MethodologyName &&
                item.VersionLabel == package.Assessment.MethodologyVersionLabel,
                cancellationToken);

        var mappedClientFolder = client.ClientFolder;
        if (!string.IsNullOrWhiteSpace(package.Client.ClientFolder))
        {
            var liveRoot = await LoadActiveClientFolderRootAsync(cancellationToken)
                ?? throw new InvalidOperationException("Live KCAS has no active client evidence root.");
            mappedClientFolder = MapClientFolderToLiveRoot(package.Client.ClientFolder, liveRoot)
                ?? throw new InvalidOperationException("The package client folder cannot be mapped safely to the live root.");
        }

        var oldClientClassification = JsonSerializer.Serialize(new
        {
            client.ClientCategory,
            client.ClientCategorySource,
            client.LifecycleStatus,
            client.ClientFolder
        });
        client.ClientCategory = package.Client.ClientCategory;
        client.ClientCategorySource = package.Client.ClientCategorySource;
        client.ClientCategoryReason = package.Client.ClientCategoryReason;
        client.ClientCategoryUpdatedAtUtc = package.Client.ClientCategoryUpdatedAtUtc;
        client.ClientCategoryUpdatedBy = package.Client.ClientCategoryUpdatedBy;
        client.LifecycleStatus = package.Client.LifecycleStatus;
        client.LifecycleReason = package.Client.LifecycleReason;
        client.LifecycleReviewedAtUtc = package.Client.LifecycleReviewedAtUtc;
        client.LifecycleReviewedBy = package.Client.LifecycleReviewedBy;
        client.ClientFolder = mappedClientFolder;
        client.IsActive = package.Client.LifecycleStatus == ClientLifecycleStatuses.Current;
        client.UpdatedAtUtc = DateTime.UtcNow;
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(Client),
            EntityId = client.Id,
            Action = "ClientClassificationImported",
            OldValueJson = oldClientClassification,
            NewValueJson = JsonSerializer.Serialize(new
            {
                client.ClientCategory,
                client.ClientCategorySource,
                client.LifecycleStatus,
                client.ClientFolder,
                SourcePackageId = package.PackageId
            }),
            UserName = user,
            Reason = reason
        });

        if (package.EntityProfile is not null)
        {
            client.EntityProfile ??= new ClientEntityProfile { ClientId = client.Id };
            var profile = client.EntityProfile;
            profile.LegalForm = package.EntityProfile.LegalForm;
            profile.RegistrationNumber = package.EntityProfile.RegistrationNumber;
            profile.RegistrationCountry = package.EntityProfile.RegistrationCountry;
            profile.EstablishmentDate = package.EntityProfile.EstablishmentDate;
            profile.NatureOfBusinessOrPurpose = package.EntityProfile.NatureOfBusinessOrPurpose;
            profile.OwnershipReviewStatus = package.EntityProfile.OwnershipReviewStatus;
            profile.ControlConclusion = package.EntityProfile.ControlConclusion;
            profile.ControlConclusionReason = package.EntityProfile.ControlConclusionReason;
            profile.OwnershipReviewedAtUtc = package.EntityProfile.OwnershipReviewedAtUtc;
            profile.OwnershipReviewedBy = package.EntityProfile.OwnershipReviewedBy;
            profile.NextOwnershipReviewDate = package.EntityProfile.NextOwnershipReviewDate;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            profile.UpdatedBy = user;
        }

        var partyByKey = new Dictionary<string, ClientRelatedParty>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in package.RelatedParties)
        {
            var party = MatchRelatedParty(client.RelatedParties, source)
                ?? new ClientRelatedParty { ClientId = client.Id };
            if (party.Id == 0 && !client.RelatedParties.Contains(party))
            {
                client.RelatedParties.Add(party);
            }
            party.PartyType = source.PartyType;
            party.DisplayName = source.DisplayName;
            party.SouthAfricanIdNumber = source.SouthAfricanIdNumber;
            party.PassportNumber = source.PassportNumber;
            party.PassportCountry = source.PassportCountry;
            party.RegistrationNumber = source.RegistrationNumber;
            party.BirthDate = source.BirthDate;
            party.Nationality = source.Nationality;
            party.CountryOfResidence = source.CountryOfResidence;
            party.OwnershipPercent = source.OwnershipPercent;
            party.ControlBasis = source.ControlBasis;
            party.AuthorityBasis = source.AuthorityBasis;
            party.EffectiveFrom = source.EffectiveFrom;
            party.EffectiveTo = source.EffectiveTo;
            party.IsActive = source.IsActive;
            party.Notes = source.Notes;
            party.UpdatedAtUtc = DateTime.UtcNow;
            party.UpdatedBy = user;
            db.ClientRelatedPartyRoles.RemoveRange(party.Roles);
            party.Roles.Clear();
            foreach (var role in source.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                party.Roles.Add(new ClientRelatedPartyRole { RoleCode = role });
            }
            partyByKey[source.PartyKey] = party;
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var source in package.InvestmentReconciliations)
        {
            var account = MatchInvestmentAccount(client.InvestmentAccounts, source.LegacyInvestmentAccountId, source.AccountNumber, source.Administrator)
                ?? throw new InvalidOperationException($"Investment account '{source.AccountNumber}' could not be matched uniquely on live.");
            ClientInvestmentAccount? related = null;
            if (source.RelatedLegacyInvestmentAccountId.HasValue || !string.IsNullOrWhiteSpace(source.RelatedAccountNumber))
            {
                related = MatchInvestmentAccount(client.InvestmentAccounts, source.RelatedLegacyInvestmentAccountId, source.RelatedAccountNumber, source.RelatedAdministrator)
                    ?? throw new InvalidOperationException($"Related investment for '{source.AccountNumber}' could not be matched uniquely on live.");
            }
            var oldSurrenderDate = account.SurrenderDate;
            var matchedValuations = ClientInvestmentStatusClassifier.MatchingValuations(account, client.FundValuations);
            var outcomeError = InvestmentReconciliationService.ValidateOutcome(
                source.Outcome,
                source.SurrenderDate,
                related is not null,
                matchedValuations);
            if (outcomeError is not null)
            {
                throw new InvalidOperationException($"Investment {account.AccountNumber}: {outcomeError}");
            }
            account.SurrenderDate = source.Outcome == ClientInvestmentReconciliationOutcomes.Current
                ? null
                : source.SurrenderDate;
            var portableSnapshot = CalculatePortableInvestmentSnapshot(account, matchedValuations);
            if (!string.Equals(source.PortableSnapshotSha256, portableSnapshot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Investment {account.AccountNumber}: the live account, transactions or valuations differ from the reviewed package snapshot.");
            }
            account.UpdatedBy = user;
            db.ClientInvestmentReconciliationReviews.Add(new ClientInvestmentReconciliationReview
            {
                ClientId = client.Id,
                ClientInvestmentAccountId = account.Id,
                Outcome = source.Outcome,
                RelatedClientInvestmentAccountId = related?.Id,
                AppliedSurrenderDate = source.SurrenderDate,
                EvidenceReference = source.EvidenceReference,
                Reason = source.Reason,
                SnapshotSha256 = InvestmentReconciliationService.CalculateSnapshot(account, matchedValuations),
                ReviewedAtUtc = source.ReviewedAtUtc,
                ReviewedBy = source.ReviewedBy
            });
            db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
            {
                EntityType = nameof(ClientInvestmentAccount),
                EntityId = account.Id,
                Action = "InvestmentReconciliationImported",
                OldValueJson = JsonSerializer.Serialize(new { SurrenderDate = oldSurrenderDate }),
                NewValueJson = JsonSerializer.Serialize(new
                {
                    source.Outcome,
                    source.SurrenderDate,
                    RelatedClientInvestmentAccountId = related?.Id,
                    source.EvidenceReference,
                    SourcePackageId = package.PackageId
                }),
                UserName = user,
                Reason = reason
            });
        }

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
            evidenceByKey[EvidenceKey(item)] = item;
        }

        foreach (var source in package.Evidence)
        {
            var key = source.EvidenceKey;
            partyByKey.TryGetValue(source.RelatedPartyKey ?? "", out var screeningParty);
            if (evidenceByKey.TryGetValue(key, out var existingItem))
            {
                if (screeningParty is not null && existingItem.ClientRelatedPartyId != screeningParty.Id)
                {
                    existingItem.ClientRelatedPartyId = screeningParty.Id;
                    existingItem.UpdatedAtUtc = DateTime.UtcNow;
                    existingItem.UpdatedBy = user;
                }
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
                ClientRelatedPartyId = screeningParty?.Id,
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

        foreach (var sourceParty in package.RelatedParties)
        {
            var party = partyByKey[sourceParty.PartyKey];
            foreach (var sourceLink in sourceParty.EvidenceLinks)
            {
                if (!evidenceByKey.TryGetValue(sourceLink.EvidenceKey, out var linkedEvidence))
                {
                    throw new InvalidOperationException(
                        $"Related-party evidence '{sourceLink.EvidenceKey}' is missing from the package import.");
                }
                var exists = await db.ClientRelatedPartyEvidenceLinks.AnyAsync(link =>
                    link.ClientRelatedPartyId == party.Id &&
                    link.ClientEvidenceItemId == linkedEvidence.Id &&
                    link.Purpose == sourceLink.Purpose, cancellationToken);
                if (!exists)
                {
                    db.ClientRelatedPartyEvidenceLinks.Add(new ClientRelatedPartyEvidenceLink
                    {
                        ClientRelatedPartyId = party.Id,
                        ClientEvidenceItemId = linkedEvidence.Id,
                        Purpose = sourceLink.Purpose,
                        LinkedAtUtc = sourceLink.LinkedAtUtc,
                        LinkedBy = sourceLink.LinkedBy
                    });
                }
            }
        }
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(ClientEntityProfile),
            EntityId = client.EntityProfile?.Id ?? client.Id,
            Action = "EntityOwnershipImported",
            NewValueJson = JsonSerializer.Serialize(new
            {
                RelatedPartyCount = package.RelatedParties.Count,
                EvidenceLinkCount = package.RelatedParties.Sum(party => party.EvidenceLinks.Count),
                SourcePackageId = package.PackageId
            }),
            UserName = user,
            Reason = reason
        });

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

        await db.SaveChangesAsync(cancellationToken);
        var readiness = await new ClientEvidenceReadinessService(db).LoadClientReadinessAsync(client.Id);
        if (!readiness.IsReadyForRiskAssessment)
        {
            throw new InvalidOperationException(
                $"The imported client review is not evidence-ready on live; {readiness.BlockedCount} blocking item(s) remain.");
        }
        var investmentReadiness = await new InvestmentReconciliationService(db)
            .LoadClientReviewAsync(client.Id, cancellationToken);
        if (!investmentReadiness.IsComplete)
        {
            var blockerCount = investmentReadiness.Accounts.Count(item => !item.IsVerified) +
                               investmentReadiness.UnmatchedIssues.Count;
            throw new InvalidOperationException(
                $"The imported investment reconciliation is incomplete on live; {blockerCount} item(s) remain.");
        }
        var blockingVerificationCount = await db.ClientVerificationItems.CountAsync(item =>
            item.ClientId == client.Id &&
            item.Status == ClientVerificationStatuses.Pending &&
            item.IsBlocking, cancellationToken);
        if (blockingVerificationCount > 0)
        {
            throw new InvalidOperationException(
                $"The imported client review has {blockingVerificationCount} blocking verification item(s) on live.");
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
        var fileName = BuildPackageFileName(
            client.Id,
            client.SurnameOrEntityName,
            package.CreatedAtUtc,
            package.PackageId);
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
        var partyKeys = client.RelatedParties.ToDictionary(
            party => party.Id,
            party => $"party:{party.Id}");
        var relatedPartyEvidenceIds = client.RelatedParties
            .SelectMany(party => party.EvidenceLinks)
            .Select(link => link.ClientEvidenceItemId)
            .ToHashSet();
        var evidence = client.EvidenceItems
            .Where(item =>
                item.VerifiedDate.HasValue &&
                item.Status == ClientEvidenceStatuses.Verified &&
                item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed &&
                (item.SelectionStatus == ClientEvidenceSelectionStatuses.Current ||
                 item.ClientRelatedPartyId.HasValue ||
                 relatedPartyEvidenceIds.Contains(item.Id) ||
                 item.EvidenceType is "PepPip" or "SanctionsTfs" or "AdverseInformation"))
            .OrderBy(item => item.EvidenceType)
            .ThenBy(item => item.FileName)
            .Select(item => new ClientReviewEvidencePackage
            {
                EvidenceKey = EvidenceKey(item),
                RelatedPartyKey = item.ClientRelatedPartyId.HasValue &&
                    partyKeys.TryGetValue(item.ClientRelatedPartyId.Value, out var partyKey)
                        ? partyKey
                        : null,
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
                ClientFolder = client.ClientFolder,
                ClientCategory = client.ClientCategory,
                ClientCategorySource = client.ClientCategorySource,
                ClientCategoryReason = client.ClientCategoryReason,
                ClientCategoryUpdatedAtUtc = client.ClientCategoryUpdatedAtUtc,
                ClientCategoryUpdatedBy = client.ClientCategoryUpdatedBy,
                LifecycleStatus = client.LifecycleStatus,
                LifecycleReason = client.LifecycleReason,
                LifecycleReviewedAtUtc = client.LifecycleReviewedAtUtc,
                LifecycleReviewedBy = client.LifecycleReviewedBy
            },
            EntityProfile = client.EntityProfile is null ? null : new ClientReviewEntityProfilePackage
            {
                LegalForm = client.EntityProfile.LegalForm,
                RegistrationNumber = client.EntityProfile.RegistrationNumber,
                RegistrationCountry = client.EntityProfile.RegistrationCountry,
                EstablishmentDate = client.EntityProfile.EstablishmentDate,
                NatureOfBusinessOrPurpose = client.EntityProfile.NatureOfBusinessOrPurpose,
                OwnershipReviewStatus = client.EntityProfile.OwnershipReviewStatus,
                ControlConclusion = client.EntityProfile.ControlConclusion,
                ControlConclusionReason = client.EntityProfile.ControlConclusionReason,
                OwnershipReviewedAtUtc = client.EntityProfile.OwnershipReviewedAtUtc,
                OwnershipReviewedBy = client.EntityProfile.OwnershipReviewedBy,
                NextOwnershipReviewDate = client.EntityProfile.NextOwnershipReviewDate
            },
            RelatedParties = client.RelatedParties
                .OrderBy(party => party.Id)
                .Select(party => new ClientReviewRelatedPartyPackage
                {
                    PartyKey = partyKeys[party.Id],
                    PartyType = party.PartyType,
                    DisplayName = party.DisplayName,
                    SouthAfricanIdNumber = party.SouthAfricanIdNumber,
                    PassportNumber = party.PassportNumber,
                    PassportCountry = party.PassportCountry,
                    RegistrationNumber = party.RegistrationNumber,
                    BirthDate = party.BirthDate,
                    Nationality = party.Nationality,
                    CountryOfResidence = party.CountryOfResidence,
                    OwnershipPercent = party.OwnershipPercent,
                    ControlBasis = party.ControlBasis,
                    AuthorityBasis = party.AuthorityBasis,
                    EffectiveFrom = party.EffectiveFrom,
                    EffectiveTo = party.EffectiveTo,
                    IsActive = party.IsActive,
                    Notes = party.Notes,
                    Roles = party.Roles.Select(role => role.RoleCode).OrderBy(role => role).ToList(),
                    EvidenceLinks = party.EvidenceLinks.Select(link => new ClientReviewRelatedPartyEvidenceLinkPackage
                    {
                        EvidenceKey = EvidenceKey(link.EvidenceItem),
                        Purpose = link.Purpose,
                        LinkedAtUtc = link.LinkedAtUtc,
                        LinkedBy = link.LinkedBy
                    }).ToList()
                }).ToList(),
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
            InvestmentReconciliations = client.InvestmentAccounts
                .Select(account => new
                {
                    Account = account,
                    Snapshot = InvestmentReconciliationService.CalculateSnapshot(
                        account,
                        ClientInvestmentStatusClassifier.MatchingValuations(account, client.FundValuations)),
                    Review = client.InvestmentReconciliationReviews
                        .Where(review => review.ClientInvestmentAccountId == account.Id)
                        .OrderByDescending(review => review.ReviewedAtUtc)
                        .ThenByDescending(review => review.Id)
                        .FirstOrDefault()
                })
                .Where(entry => entry.Review is not null &&
                    entry.Review.Outcome != ClientInvestmentReconciliationOutcomes.NeedsFollowUp &&
                    string.Equals(entry.Review.SnapshotSha256, entry.Snapshot, StringComparison.OrdinalIgnoreCase))
                .Select(entry =>
                {
                    var related = entry.Review!.RelatedClientInvestmentAccountId.HasValue
                        ? client.InvestmentAccounts.FirstOrDefault(account => account.Id == entry.Review.RelatedClientInvestmentAccountId.Value)
                        : null;
                    return new ClientReviewInvestmentReconciliationPackage
                    {
                        LegacyInvestmentAccountId = entry.Account.LegacyInvestmentAccountId,
                        AccountNumber = entry.Account.AccountNumber,
                        Administrator = entry.Account.Administrator,
                        Outcome = entry.Review.Outcome,
                        SurrenderDate = entry.Account.SurrenderDate,
                        PortableSnapshotSha256 = CalculatePortableInvestmentSnapshot(
                            entry.Account,
                            ClientInvestmentStatusClassifier.MatchingValuations(entry.Account, client.FundValuations)),
                        RelatedLegacyInvestmentAccountId = related?.LegacyInvestmentAccountId,
                        RelatedAccountNumber = related?.AccountNumber,
                        RelatedAdministrator = related?.Administrator,
                        EvidenceReference = entry.Review.EvidenceReference,
                        Reason = entry.Review.Reason,
                        ReviewedAtUtc = entry.Review.ReviewedAtUtc,
                        ReviewedBy = entry.Review.ReviewedBy
                    };
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
                            : EvidenceKey(item.EvidenceItem),
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
        Client? match;
        if (source.LegacyClientId.HasValue)
        {
            match = await db.Clients.AsNoTracking().SingleOrDefaultAsync(
                item => item.LegacyClientId == source.LegacyClientId.Value,
                cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(source.KanaanId))
        {
            var matches = await db.Clients.AsNoTracking()
                .Where(item => item.KanaanId == source.KanaanId)
                .Take(2)
                .ToListAsync(cancellationToken);
            match = matches.Count == 1 ? matches[0] : null;
        }
        else
        {
            match = null;
        }
        if (match is null) return null;
        if (source.LegacyClientId.HasValue && match.LegacyClientId != source.LegacyClientId ||
            !string.IsNullOrWhiteSpace(source.KanaanId) &&
            !string.Equals(match.KanaanId, source.KanaanId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return match;
    }

    private async Task<string?> LoadActiveClientFolderRootAsync(CancellationToken cancellationToken) =>
        await db.ClientEvidenceScanRoots.AsNoTracking()
            .Where(root => root.IsActive)
            .OrderByDescending(root => root.Id)
            .Select(root => root.RootPath)
            .FirstOrDefaultAsync(cancellationToken);

    internal static string? MapClientFolderToLiveRoot(string? sourceFolder, string? liveRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || string.IsNullOrWhiteSpace(liveRoot))
        {
            return null;
        }
        var source = NormalizeWindowsPath(sourceFolder);
        var destinationRoot = NormalizeWindowsPath(liveRoot).TrimEnd('\\');
        var recognisedRoots = new[]
        {
            @"C:\Download\_kanaan\ClientsKanaan",
            @"E:\Userdata\Kanaan Trust\Clients",
            @"Z:\Userdata\Kanaan Trust\Clients",
            destinationRoot
        };
        foreach (var root in recognisedRoots
            .Select(NormalizeWindowsPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(root => root.Length))
        {
            if (!source.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var relative = source[root.Length..].TrimStart('\\');
            var segments = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
            {
                return null;
            }
            return segments.Length == 0
                ? destinationRoot
                : destinationRoot + "\\" + string.Join("\\", segments);
        }
        return null;
    }

    private static string NormalizeWindowsPath(string path) =>
        path.Trim().Replace('/', '\\').TrimEnd('\\');

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
            package.RelatedParties ??= [];
            package.Evidence ??= [];
            package.Exceptions ??= [];
            package.VerificationItems ??= [];
            package.InvestmentReconciliations ??= [];
            package.Assessment.Responses ??= [];
            package.Assessment.Approvals ??= [];
            foreach (var party in package.RelatedParties)
            {
                party.Roles ??= [];
                party.EvidenceLinks ??= [];
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

    private static void ValidatePackageStructure(ClientReviewPackage package, ICollection<string> conflicts)
    {
        if (package.Client.ClientCategory is not (ClientCategories.NaturalPerson or
            ClientCategories.LegalPerson or ClientCategories.Trust or ClientCategories.Other))
        {
            conflicts.Add($"Client category '{package.Client.ClientCategory}' is not supported.");
        }

        var partyKeys = package.RelatedParties.Select(party => party.PartyKey).ToList();
        if (partyKeys.Any(string.IsNullOrWhiteSpace) ||
            partyKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != partyKeys.Count)
        {
            conflicts.Add("Related-party keys are missing or duplicated in the package.");
        }
        var partyKeySet = partyKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evidenceKeys = package.Evidence.Select(item => item.EvidenceKey).ToList();
        if (evidenceKeys.Any(string.IsNullOrWhiteSpace) ||
            evidenceKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != evidenceKeys.Count)
        {
            conflicts.Add("Evidence keys are missing or duplicated in the package.");
        }
        var evidenceKeySet = evidenceKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (package.Client.ClientCategory is ClientCategories.Trust or ClientCategories.LegalPerson &&
            package.EntityProfile is null)
        {
            conflicts.Add("The entity ownership profile is missing from the package.");
        }
        foreach (var party in package.RelatedParties)
        {
            if (!ClientRelatedPartyTypes.All.Contains(party.PartyType))
            {
                conflicts.Add($"Related party '{party.DisplayName}' has an invalid type.");
            }
            if (party.Roles.Any(role => !ClientRelatedPartyRoles.All.Contains(role)))
            {
                conflicts.Add($"Related party '{party.DisplayName}' has an invalid role.");
            }
            foreach (var link in party.EvidenceLinks)
            {
                if (!evidenceKeySet.Contains(link.EvidenceKey))
                {
                    conflicts.Add($"Related-party evidence for '{party.DisplayName}' is missing from the package.");
                }
                if (!ClientRelatedPartyEvidencePurposes.All.Contains(link.Purpose))
                {
                    conflicts.Add($"Related-party evidence for '{party.DisplayName}' has an invalid purpose.");
                }
            }
        }
        foreach (var item in package.Evidence.Where(item => !string.IsNullOrWhiteSpace(item.RelatedPartyKey)))
        {
            if (!partyKeySet.Contains(item.RelatedPartyKey!))
            {
                conflicts.Add($"Evidence '{item.Title}' refers to a related party that is missing from the package.");
            }
        }
        foreach (var response in package.Assessment.Responses.Where(response => !string.IsNullOrWhiteSpace(response.EvidenceKey)))
        {
            if (!evidenceKeySet.Contains(response.EvidenceKey!))
            {
                conflicts.Add($"Risk factor '{response.FactorCode}' refers to evidence that is missing from the package.");
            }
        }
        foreach (var investment in package.InvestmentReconciliations)
        {
            if (investment.PortableSnapshotSha256.Length != 64 ||
                investment.PortableSnapshotSha256.Any(character => !Uri.IsHexDigit(character)))
            {
                conflicts.Add($"Investment '{investment.AccountNumber}' has an invalid portable review snapshot.");
            }
        }
    }

    private static ClientRelatedParty? MatchRelatedParty(
        IEnumerable<ClientRelatedParty> parties,
        ClientReviewRelatedPartyPackage source)
    {
        var list = parties.ToList();
        List<ClientRelatedParty> matches;
        if (!string.IsNullOrWhiteSpace(source.SouthAfricanIdNumber))
        {
            matches = list.Where(party => party.SouthAfricanIdNumber == source.SouthAfricanIdNumber).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(source.PassportNumber))
        {
            matches = list.Where(party =>
                string.Equals(party.PassportNumber, source.PassportNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(party.PassportCountry, source.PassportCountry, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(source.RegistrationNumber))
        {
            matches = list.Where(party =>
                string.Equals(party.RegistrationNumber, source.RegistrationNumber, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            matches = list.Where(party =>
                party.PartyType == source.PartyType &&
                string.Equals(party.DisplayName, source.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                party.BirthDate == source.BirthDate).ToList();
        }
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"Related party '{source.DisplayName}' could not be matched uniquely on live.")
        };
    }

    private static string CalculatePortableInvestmentSnapshot(
        ClientInvestmentAccount account,
        IEnumerable<ClientFundValuation> valuations)
    {
        var payload = JsonSerializer.Serialize(new
        {
            account.LegacyInvestmentAccountId,
            account.InvestmentDate,
            account.SurrenderDate,
            account.Administrator,
            account.AccountNumber,
            account.ProductName,
            account.ProductType,
            account.FundName,
            Transactions = account.Transactions.Where(item => !item.IsDeleted)
                .OrderBy(item => item.LegacyInvestmentHistoryId)
                .ThenBy(item => item.TransactionDate)
                .ThenBy(item => item.Description)
                .Select(item => new
                {
                    item.LegacyInvestmentHistoryId,
                    item.TransactionDate,
                    item.Description,
                    item.ExchangeRate,
                    item.InvestmentAmountForeign,
                    item.InvestmentAmountZar,
                    item.WithdrawalAmountForeign,
                    item.WithdrawalAmountZar,
                    item.BalanceForeign,
                    item.BalanceZar
                }),
            Valuations = valuations
                .OrderBy(item => item.LegacyFundId)
                .ThenBy(item => item.ValuationDate)
                .ThenBy(item => item.FundName)
                .Select(item => new
                {
                    item.LegacyFundId,
                    item.ValuationDate,
                    item.AmountZar,
                    item.AmountForeign,
                    item.InvestmentUniqueNumber,
                    item.Administrator,
                    item.FundName
                })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
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
        package.Client.ClientFolder,
        EvidenceCount = package.Evidence.Count,
        ExceptionCount = package.Exceptions.Count,
        RelatedPartyCount = package.RelatedParties.Count,
        InvestmentReconciliationCount = package.InvestmentReconciliations.Count,
        package.Assessment.MethodologyName,
        package.Assessment.MethodologyVersionLabel,
        package.Assessment.Status,
        package.Assessment.FinalRating,
        package.Assessment.EffectiveDate
    };

    private static string EvidenceKey(ClientEvidenceItem item) =>
        EvidenceKey(item.FileSha256, item.EvidenceType, item.FileName, item.Title,
            item.ScreeningSubjectType, item.ScreeningSubjectName, item.ScreeningReviewDate);

    private static string EvidenceKey(
        string? hash,
        string evidenceType,
        string? fileName,
        string? title,
        string? screeningSubjectType,
        string? screeningSubjectName,
        DateOnly? screeningReviewDate)
    {
        if (!string.IsNullOrWhiteSpace(hash))
        {
            return $"sha256:{hash.Trim().ToLowerInvariant()}";
        }
        var metadata = string.Join("|", new[]
        {
            evidenceType.Trim().ToLowerInvariant(),
            fileName?.Trim().ToLowerInvariant() ?? "",
            title?.Trim().ToLowerInvariant() ?? "",
            screeningSubjectType?.Trim().ToLowerInvariant() ?? "",
            screeningSubjectName?.Trim().ToLowerInvariant() ?? "",
            screeningReviewDate?.ToString("yyyy-MM-dd") ?? ""
        });
        return $"meta:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata))).ToLowerInvariant()}";
    }

    private static ClientInvestmentAccount? MatchInvestmentAccount(
        IEnumerable<ClientInvestmentAccount> accounts,
        int? legacyInvestmentAccountId,
        string? accountNumber,
        string? administrator)
    {
        var list = accounts.ToList();
        if (legacyInvestmentAccountId.HasValue)
        {
            return list.SingleOrDefault(item => item.LegacyInvestmentAccountId == legacyInvestmentAccountId.Value);
        }
        var normalized = ClientInvestmentStatusClassifier.NormalizeAccountNumber(accountNumber);
        if (normalized is null)
        {
            return null;
        }
        var matches = list.Where(item => string.Equals(
            ClientInvestmentStatusClassifier.NormalizeAccountNumber(item.AccountNumber),
            normalized,
            StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }
        var administratorMatches = matches.Where(item =>
            !string.IsNullOrWhiteSpace(administrator) &&
            !string.IsNullOrWhiteSpace(item.Administrator) &&
            (item.Administrator.Equals(administrator, StringComparison.OrdinalIgnoreCase) ||
             item.Administrator.Contains(administrator, StringComparison.OrdinalIgnoreCase) ||
             administrator.Contains(item.Administrator, StringComparison.OrdinalIgnoreCase))).ToList();
        return administratorMatches.Count == 1 ? administratorMatches[0] : null;
    }

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 12)
        {
            throw new ValidationException("Use a package passphrase of at least 12 characters.");
        }
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();

    private static string BuildPackageFileName(
        int clientId,
        string clientLabel,
        DateTime createdAtUtc,
        string packageId)
    {
        var label = SafeFileNameSegment(clientLabel, 60);
        var packageToken = SafeFileNameSegment(packageId.Replace("-", ""), 12);
        return $"KCAS-review-C{clientId}-{label}-{createdAtUtc:yyyyMMdd}-{packageToken}.kcas-review";
    }

    private static string SafeFileNameSegment(string? value, int maximumLength)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? "")
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var safe = builder.ToString().Trim('-');
        if (safe.Length > maximumLength)
        {
            safe = safe[..maximumLength].TrimEnd('-');
        }
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
    public ClientReviewEntityProfilePackage? EntityProfile { get; set; }
    public List<ClientReviewRelatedPartyPackage> RelatedParties { get; set; } = [];
    public List<ClientReviewEvidencePackage> Evidence { get; set; } = [];
    public List<ClientReviewExceptionPackage> Exceptions { get; set; } = [];
    public List<ClientReviewVerificationPackage> VerificationItems { get; set; } = [];
    public List<ClientReviewInvestmentReconciliationPackage> InvestmentReconciliations { get; set; } = [];
    public ClientReviewAssessmentPackage Assessment { get; set; } = new();
}

public sealed class ClientReviewClientPackage
{
    public int? LegacyClientId { get; set; }
    public string? KanaanId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? ClientFolder { get; set; }
    public string ClientCategory { get; set; } = "";
    public string ClientCategorySource { get; set; } = ClientCategorySources.Unknown;
    public string? ClientCategoryReason { get; set; }
    public DateTime? ClientCategoryUpdatedAtUtc { get; set; }
    public string? ClientCategoryUpdatedBy { get; set; }
    public string LifecycleStatus { get; set; } = "";
    public string? LifecycleReason { get; set; }
    public DateTime? LifecycleReviewedAtUtc { get; set; }
    public string? LifecycleReviewedBy { get; set; }
}

public sealed class ClientReviewEntityProfilePackage
{
    public string? LegalForm { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? RegistrationCountry { get; set; }
    public DateOnly? EstablishmentDate { get; set; }
    public string? NatureOfBusinessOrPurpose { get; set; }
    public string OwnershipReviewStatus { get; set; } = ClientOwnershipReviewStatuses.Draft;
    public string? ControlConclusion { get; set; }
    public string? ControlConclusionReason { get; set; }
    public DateTime? OwnershipReviewedAtUtc { get; set; }
    public string? OwnershipReviewedBy { get; set; }
    public DateOnly? NextOwnershipReviewDate { get; set; }
}

public sealed class ClientReviewRelatedPartyPackage
{
    public string PartyKey { get; set; } = "";
    public string PartyType { get; set; } = ClientRelatedPartyTypes.NaturalPerson;
    public string DisplayName { get; set; } = "";
    public string? SouthAfricanIdNumber { get; set; }
    public string? PassportNumber { get; set; }
    public string? PassportCountry { get; set; }
    public string? RegistrationNumber { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Nationality { get; set; }
    public string? CountryOfResidence { get; set; }
    public decimal? OwnershipPercent { get; set; }
    public string? ControlBasis { get; set; }
    public string? AuthorityBasis { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public List<string> Roles { get; set; } = [];
    public List<ClientReviewRelatedPartyEvidenceLinkPackage> EvidenceLinks { get; set; } = [];
}

public sealed class ClientReviewRelatedPartyEvidenceLinkPackage
{
    public string EvidenceKey { get; set; } = "";
    public string Purpose { get; set; } = ClientRelatedPartyEvidencePurposes.Other;
    public DateTime LinkedAtUtc { get; set; }
    public string? LinkedBy { get; set; }
}

public sealed class ClientReviewEvidencePackage
{
    public string EvidenceKey { get; set; } = "";
    public string? RelatedPartyKey { get; set; }
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

public sealed class ClientReviewInvestmentReconciliationPackage
{
    public int? LegacyInvestmentAccountId { get; set; }
    public string? AccountNumber { get; set; }
    public string? Administrator { get; set; }
    public string Outcome { get; set; } = "";
    public DateOnly? SurrenderDate { get; set; }
    public string PortableSnapshotSha256 { get; set; } = "";
    public int? RelatedLegacyInvestmentAccountId { get; set; }
    public string? RelatedAccountNumber { get; set; }
    public string? RelatedAdministrator { get; set; }
    public string EvidenceReference { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime ReviewedAtUtc { get; set; }
    public string ReviewedBy { get; set; } = "";
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
    public string? TargetClientFolder { get; init; }
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

public sealed record ClientReviewTransferClientOption(
    int Id,
    int? LegacyClientId,
    string? KanaanId,
    string DisplayName,
    string? SurnameOrEntityName,
    string LifecycleStatus);

internal sealed record ClientReviewEmbeddedExport(
    ClientReviewPackage Package,
    string FileName,
    byte[] EncryptedPackage,
    string ContentSha256);

public sealed record ClientReviewImportResult(
    string PackageId,
    int ClientId,
    string ClientDisplayName,
    int AssessmentId,
    int EvidenceImported,
    string FileName,
    string StoragePath);
