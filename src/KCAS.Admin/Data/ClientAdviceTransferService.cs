using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientAdviceTransferService(
    ApplicationDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    private const string PackageMagic = "KCAS-CLIENT-ADVICE-1";
    private const int PackageVersion = 1;
    private const int Pbkdf2Iterations = 300_000;
    private const int MaximumPackageBytes = 25 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string StorageRoot => ResolveStorageRoot();

    public async Task<List<ClientAdviceTransferClientOption>> LoadClientOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Clients.AsNoTracking()
            .Where(client => client.AdviceCases.Any() || client.AdviceDocuments.Any())
            .OrderBy(client => client.DisplayName)
            .Select(client => new ClientAdviceTransferClientOption(
                client.Id, client.DisplayName, client.KanaanId,
                client.AdviceCases.Count, client.AdviceDocuments.Count))
            .ToListAsync(cancellationToken);

    public async Task<ClientAdviceTransferExportResult> ExportAsync(
        int clientId,
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var user = Require(userName, "A signed-in exporter is required.");
        reason = Require(reason, "An export reason is required.");

        var client = await db.Clients.SingleOrDefaultAsync(value => value.Id == clientId, cancellationToken)
            ?? throw new ValidationException("The selected client was not found.");
        var historicalDocuments = await db.ClientAdviceDocuments.AsNoTracking()
            .Where(value => value.ClientId == clientId && value.ClientAdviceCaseId == null)
            .OrderBy(value => value.RecordedAtUtc).ToListAsync(cancellationToken);
        var cases = await CaseQuery().Where(value => value.ClientId == clientId)
            .OrderBy(value => value.AdviceDate).ThenBy(value => value.Revision).ToListAsync(cancellationToken);
        if (cases.Count == 0 && historicalDocuments.Count == 0)
            throw new ValidationException("The selected client has no advice records to transfer.");

        foreach (var item in cases.Where(value => string.IsNullOrWhiteSpace(value.TransferKey)))
            item.TransferKey = Guid.NewGuid().ToString();
        await db.SaveChangesAsync(cancellationToken);

        var caseIds = cases.Select(value => value.Id).ToList();
        var audits = await db.ComplianceAuditEvents.AsNoTracking()
            .Where(value => value.EntityType == nameof(ClientAdviceCase) && caseIds.Contains(value.EntityId))
            .OrderBy(value => value.TimestampUtc).ToListAsync(cancellationToken);
        var keyById = cases.ToDictionary(value => value.Id, value => value.TransferKey!);

        var packagedCases = cases.Select(ToPackage).ToList();
        var transferKeyByCaseId = cases.ToDictionary(value => value.Id, value => value.TransferKey!);
        for (var index = 0; index < cases.Count; index++)
            packagedCases[index].PreviousCaseTransferKey = cases[index].PreviousAdviceCaseId.HasValue
                ? transferKeyByCaseId[cases[index].PreviousAdviceCaseId!.Value]
                : null;

        var package = new ClientAdviceTransferPackage
        {
            FormatVersion = PackageVersion,
            PackageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            ExportedBy = user,
            ExportReason = reason,
            SourceEnvironment = environment.EnvironmentName,
            Client = ClientRef(client),
            SourceClientFolder = client.ClientFolder,
            Cases = packagedCases,
            HistoricalDocuments = historicalDocuments.Select(ToPackage).ToList(),
            AuditEvents = audits.Select(value => new ClientAdviceAuditPackage
            {
                CaseTransferKey = keyById[value.EntityId], Action = value.Action, UserName = value.UserName ?? "",
                TimestampUtc = value.TimestampUtc, Reason = value.Reason,
                OldValueJson = value.OldValueJson, NewValueJson = value.NewValueJson
            }).ToList()
        };
        package.EmbeddedFiles = await BuildEmbeddedFilesAsync(package, cancellationToken);

        var validation = ValidatePackage(package);
        if (validation.Count > 0)
            throw new ValidationException("The advice package is not transferable: " + string.Join(" ", validation));

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        if (plaintext.Length > MaximumPackageBytes)
            throw new ValidationException("The advice package exceeds the 25 MB safety limit.");
        var hash = Sha256(plaintext);
        var encrypted = Encrypt(plaintext, passphrase);
        var fileName = BuildFileName(package);
        var directory = Path.Combine(StorageRoot, "outgoing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);

        var record = TransferRecord(package, client.Id, ClientAdviceTransferDirections.Outgoing,
            ClientAdviceTransferStatuses.Exported, hash, fileName, path);
        db.ClientAdviceTransferRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        AddAudit(nameof(ClientAdviceTransferRecord), checked((int)record.Id), "ClientAdvicePackageExported", user,
            reason, PackageSummary(package));
        await db.SaveChangesAsync(cancellationToken);

        return new(package.PackageId, fileName, path, encrypted.LongLength, client.DisplayName,
            package.Cases.Count, package.DocumentCount);
    }

    public async Task<ClientAdviceTransferPreview> PreviewAsync(
        byte[] encryptedPackage,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        var package = DecryptPackage(encryptedPackage, passphrase, out var hash);
        var conflicts = ValidatePackage(package);
        var warnings = new List<string>();
        var matches = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var alreadyApplied = await db.ClientAdviceTransferRecords.AsNoTracking().AnyAsync(value =>
            value.Direction == ClientAdviceTransferDirections.Incoming &&
            value.Status == ClientAdviceTransferStatuses.Applied &&
            (value.PackageId == package.PackageId || value.ContentSha256 == hash), cancellationToken);
        if (alreadyApplied) warnings.Add("This advice package, or identical package content, has already been applied.");

        foreach (var source in package.AllClientReferences().DistinctBy(ClientKey))
        {
            var candidates = await FindClientsAsync(source, cancellationToken);
            if (candidates.Count != 1)
            {
                conflicts.Add($"Client '{source.DisplayName}' could not be matched uniquely on live.");
                continue;
            }
            matches[ClientKey(source)] = candidates[0].Id;
        }

        Client? targetClient = null;
        if (matches.TryGetValue(ClientKey(package.Client), out var targetClientId))
        {
            targetClient = await db.Clients.AsNoTracking().SingleAsync(value => value.Id == targetClientId, cancellationToken);
            if (string.IsNullOrWhiteSpace(targetClient.ClientFolder) && package.AllDocumentPaths().Any(IsFilePathReference))
                conflicts.Add("The live client has no client folder, so advice document paths cannot be mapped safely.");
        }

        var investmentMatches = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in package.Cases.SelectMany(value => value.InvestmentLinks))
        {
            if (!matches.TryGetValue(ClientKey(source.Owner), out var ownerId)) continue;
            var candidates = await FindInvestmentsAsync(ownerId, source, cancellationToken);
            if (candidates.Count != 1)
            {
                conflicts.Add($"Investment account '{source.AccountNumber ?? source.LegacyInvestmentAccountId?.ToString() ?? "unknown"}' for {source.Owner.DisplayName} could not be matched uniquely on live.");
                continue;
            }
            investmentMatches[InvestmentKey(source)] = candidates[0].Id;
        }

        if (targetClient is not null)
        {
            foreach (var path in package.AllDocumentPaths().Where(IsFilePathReference).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var mapped = MapPath(path, package, targetClient.ClientFolder);
                    var isEmbedded = package.EmbeddedFiles.Any(value =>
                        string.Equals(NormalizeWindowsPath(value.OriginalPath), NormalizeWindowsPath(path!), StringComparison.OrdinalIgnoreCase));
                    if (!isEmbedded && !string.IsNullOrWhiteSpace(mapped) && !File.Exists(mapped))
                        warnings.Add($"Mapped advice file is not currently available on live: {mapped}");
                }
                catch (ValidationException ex) { conflicts.Add(ex.Message); }
            }
        }

        var packagedCaseKeys = package.Cases.Select(source => source.TransferKey).ToList();
        var existingKeys = (await db.ClientAdviceCases.AsNoTracking()
                .Where(value => value.TransferKey != null)
                .Select(value => value.TransferKey!).ToListAsync(cancellationToken))
            .Where(value => packagedCaseKeys.Contains(value, StringComparer.OrdinalIgnoreCase)).ToList();
        if (existingKeys.Count > 0)
            warnings.Add($"{existingKeys.Count} matching advice case(s) already exist and will be updated from the package.");

        return new ClientAdviceTransferPreview
        {
            Package = package, ContentSha256 = hash, AlreadyApplied = alreadyApplied,
            Conflicts = conflicts.Distinct(StringComparer.Ordinal).ToList(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            TargetClientId = targetClient?.Id, TargetClientFolder = targetClient?.ClientFolder,
            ClientMatches = matches, InvestmentMatches = investmentMatches
        };
    }

    public async Task<ClientAdviceTransferImportResult> ApplyAsync(
        byte[] encryptedPackage,
        string passphrase,
        string? userName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var user = Require(userName, "A signed-in importer is required.");
        reason = Require(reason, "An import approval reason is required.");
        var preview = await PreviewAsync(encryptedPackage, passphrase, cancellationToken);
        if (preview.AlreadyApplied) throw new InvalidOperationException("This advice package has already been applied.");
        if (!preview.CanApply || !preview.TargetClientId.HasValue)
            throw new InvalidOperationException("The advice package has unresolved conflicts and cannot be applied.");

        var package = preview.Package;
        var targetClient = await db.Clients.SingleAsync(value => value.Id == preview.TargetClientId.Value, cancellationToken);
        var incomingDirectory = Path.Combine(StorageRoot, "incoming");
        Directory.CreateDirectory(incomingDirectory);
        var fileName = BuildFileName(package);
        var path = Path.Combine(incomingDirectory, fileName);
        var created = false;
        string? extractedDirectory = null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await output.WriteAsync(encryptedPackage, cancellationToken);
            created = true;
            extractedDirectory = await ExtractEmbeddedFilesAsync(package, targetClient.ClientFolder, cancellationToken);

            var targetCases = new Dictionary<string, ClientAdviceCase>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in package.Cases)
            {
                var target = await CaseQuery().SingleOrDefaultAsync(value => value.TransferKey == source.TransferKey, cancellationToken);
                if (target is null)
                {
                    target = new ClientAdviceCase { TransferKey = source.TransferKey, ClientId = targetClient.Id };
                    db.ClientAdviceCases.Add(target);
                }
                else
                {
                    ClearChildren(target);
                }
                CopyCase(source, target);
                target.ClientId = targetClient.Id;
                foreach (var participant in source.Participants)
                    target.Participants.Add(new ClientAdviceParticipant { ClientId = preview.ClientMatches[ClientKey(participant.Client)], Role = participant.Role });
                foreach (var response in source.RiskResponses)
                    target.RiskResponses.Add(new ClientAdviceRiskResponse { QuestionCode = response.QuestionCode, AnswerCode = response.AnswerCode, Score = response.Score, Explanation = response.Explanation });
                foreach (var product in source.Products)
                    target.Products.Add(new ClientAdviceProduct { ProductName = product.ProductName, Provider = product.Provider, ProductType = product.ProductType, IsRecommended = product.IsRecommended, Amount = product.Amount, AllocationPercent = product.AllocationPercent, Motivation = product.Motivation, SupportingDocumentPath = MapPath(product.SupportingDocumentPath, package, targetClient.ClientFolder) });
                foreach (var fact in source.FactSources)
                    target.FactSources.Add(new ClientAdviceFactSource { FactName = fact.FactName, SourceDate = fact.SourceDate, DocumentPath = MapPath(fact.DocumentPath, package, targetClient.ClientFolder) ?? "", Notes = fact.Notes });
                foreach (var link in source.InvestmentLinks)
                    target.InvestmentLinks.Add(new ClientAdviceInvestmentLink { ClientInvestmentAccountId = preview.InvestmentMatches[InvestmentKey(link)], Role = link.Role });
                foreach (var finding in source.Findings)
                    target.ReviewFindings.Add(new ClientAdviceReviewFinding { Severity = finding.Severity, Category = finding.Category, AffectedField = finding.AffectedField, Finding = finding.Finding, EvidenceReference = finding.EvidenceReference, RecommendedCorrection = finding.RecommendedCorrection, Status = finding.Status, Resolution = finding.Resolution, PerformedBy = finding.PerformedBy, PerformedAtUtc = finding.PerformedAtUtc, ResolvedBy = finding.ResolvedBy, ResolvedAtUtc = finding.ResolvedAtUtc });
                foreach (var approval in source.Approvals)
                    target.Approvals.Add(new ClientAdviceApproval { Reviewer = approval.Reviewer, Decision = approval.Decision, Reason = approval.Reason, DecidedAtUtc = approval.DecidedAtUtc });
                foreach (var document in source.Documents)
                    target.Documents.Add(ToEntity(document, targetClient.Id, package, targetClient.ClientFolder));
                await db.SaveChangesAsync(cancellationToken);
                targetCases[source.TransferKey] = target;
            }

            foreach (var source in package.Cases.Where(value => !string.IsNullOrWhiteSpace(value.PreviousCaseTransferKey)))
                targetCases[source.TransferKey].PreviousAdviceCaseId = targetCases[source.PreviousCaseTransferKey!].Id;

            foreach (var document in package.HistoricalDocuments)
            {
                var mappedPath = MapPath(document.SourcePath, package, targetClient.ClientFolder);
                var existing = await db.ClientAdviceDocuments.FirstOrDefaultAsync(value =>
                    value.ClientId == targetClient.Id && value.ClientAdviceCaseId == null &&
                    value.DocumentType == document.DocumentType && value.FileName == document.FileName &&
                    value.FileSha256 == document.FileSha256, cancellationToken);
                if (existing is null) db.ClientAdviceDocuments.Add(ToEntity(document, targetClient.Id, package, targetClient.ClientFolder));
                else CopyDocument(document, existing, mappedPath);
            }

            foreach (var audit in package.AuditEvents)
            {
                var caseId = targetCases[audit.CaseTransferKey].Id;
                var exists = await db.ComplianceAuditEvents.AnyAsync(value => value.EntityType == nameof(ClientAdviceCase) &&
                    value.EntityId == caseId && value.Action == audit.Action && value.UserName == audit.UserName &&
                    value.TimestampUtc == audit.TimestampUtc, cancellationToken);
                if (!exists) db.ComplianceAuditEvents.Add(new ComplianceAuditEvent { EntityType = nameof(ClientAdviceCase), EntityId = caseId, Action = audit.Action, UserName = audit.UserName ?? "", TimestampUtc = audit.TimestampUtc, Reason = audit.Reason ?? "", OldValueJson = audit.OldValueJson, NewValueJson = audit.NewValueJson });
            }

            var record = TransferRecord(package, targetClient.Id, ClientAdviceTransferDirections.Incoming,
                ClientAdviceTransferStatuses.Applied, preview.ContentSha256, fileName, path);
            record.AppliedAtUtc = DateTime.UtcNow; record.AppliedBy = user;
            db.ClientAdviceTransferRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            AddAudit(nameof(ClientAdviceTransferRecord), checked((int)record.Id), "ClientAdvicePackageApplied", user, reason,
                new { package.PackageId, targetClient.Id, Cases = package.Cases.Count, Documents = package.DocumentCount });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(package.PackageId, targetClient.Id, package.Cases.Count, package.DocumentCount, fileName, path);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            if (created && File.Exists(path)) File.Delete(path);
            if (!string.IsNullOrWhiteSpace(extractedDirectory) && Directory.Exists(extractedDirectory))
                Directory.Delete(extractedDirectory, true);
            throw;
        }
    }

    public async Task<ClientAdvicePackageFile?> OpenExportAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var record = await db.ClientAdviceTransferRecords.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Direction == ClientAdviceTransferDirections.Outgoing && value.PackageId == packageId, cancellationToken);
        return record is not null && File.Exists(record.StoragePath) ? new(record.StoragePath, record.FileName) : null;
    }

    private IQueryable<ClientAdviceCase> CaseQuery() => db.ClientAdviceCases
        .Include(value => value.Participants).ThenInclude(value => value.Client)
        .Include(value => value.RiskResponses).Include(value => value.Products).Include(value => value.FactSources)
        .Include(value => value.InvestmentLinks).ThenInclude(value => value.InvestmentAccount).ThenInclude(value => value.Client)
        .Include(value => value.ReviewFindings).Include(value => value.Approvals).Include(value => value.Documents);

    private async Task<List<Client>> FindClientsAsync(ClientAdviceClientRef source, CancellationToken token)
    {
        if (source.LegacyClientId.HasValue)
            return await db.Clients.AsNoTracking().Where(value => value.LegacyClientId == source.LegacyClientId).ToListAsync(token);
        return await db.Clients.AsNoTracking().Where(value => value.KanaanId == source.KanaanId && value.DisplayName == source.DisplayName).ToListAsync(token);
    }

    private async Task<List<ClientInvestmentAccount>> FindInvestmentsAsync(int clientId, ClientAdviceInvestmentPackage source, CancellationToken token)
    {
        var accounts = await db.ClientInvestmentAccounts.AsNoTracking().Where(value => value.ClientId == clientId).ToListAsync(token);
        if (source.LegacyInvestmentAccountId.HasValue)
        {
            var legacy = accounts.Where(value => value.LegacyInvestmentAccountId == source.LegacyInvestmentAccountId).ToList();
            if (legacy.Count == 1) return legacy;
        }
        var number = ClientInvestmentStatusClassifier.NormalizeAccountNumber(source.AccountNumber);
        return accounts.Where(value => ClientInvestmentStatusClassifier.NormalizeAccountNumber(value.AccountNumber) == number &&
            (string.IsNullOrWhiteSpace(source.Administrator) || string.Equals(value.Administrator?.Trim(), source.Administrator.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private static ClientAdviceCasePackage ToPackage(ClientAdviceCase source) => new()
    {
        TransferKey = source.TransferKey!,
        AdviceType = source.AdviceType, RiskMethodologyCode = source.RiskMethodologyCode, Status = source.Status,
        Revision = source.Revision, AdviceDate = source.AdviceDate, AdviserName = source.AdviserName, PreparedBy = source.PreparedBy,
        CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.UpdatedAtUtc, AdviceScope = source.AdviceScope,
        MeetingSummary = source.MeetingSummary, NeedsAndObjectives = source.NeedsAndObjectives, FinancialSituation = source.FinancialSituation,
        AdviceLimitations = source.AdviceLimitations, ProductKnowledgeSummary = source.ProductKnowledgeSummary,
        InvestmentAmount = source.InvestmentAmount, InvestmentPortfolioPercent = source.InvestmentPortfolioPercent,
        DomesticPreferencePercent = source.DomesticPreferencePercent, OffshorePreferencePercent = source.OffshorePreferencePercent,
        CalculatedRiskScore = source.CalculatedRiskScore, CalculatedRiskLevel = source.CalculatedRiskLevel, FinalRiskLevel = source.FinalRiskLevel,
        RiskOverrideReason = source.RiskOverrideReason, RecommendationSummary = source.RecommendationSummary,
        RecommendationRationale = source.RecommendationRationale, CostsAndFees = source.CostsAndFees, TaxConsequences = source.TaxConsequences,
        LiquidityAndRestrictions = source.LiquidityAndRestrictions, MaterialRisks = source.MaterialRisks, IsReplacement = source.IsReplacement,
        ReplacementConsequences = source.ReplacementConsequences, ClientDeparture = source.ClientDeparture, WarningsGiven = source.WarningsGiven,
        FrozenSnapshotJson = source.FrozenSnapshotJson, FrozenSnapshotSha256 = source.FrozenSnapshotSha256,
        SubmittedAtUtc = source.SubmittedAtUtc, ApprovedAtUtc = source.ApprovedAtUtc, IssuedAtUtc = source.IssuedAtUtc, CompletedAtUtc = source.CompletedAtUtc,
        Participants = source.Participants.Select(value => new ClientAdviceParticipantPackage { Client = ClientRef(value.Client), Role = value.Role }).ToList(),
        RiskResponses = source.RiskResponses.Select(value => new ClientAdviceRiskResponsePackage { QuestionCode = value.QuestionCode, AnswerCode = value.AnswerCode, Score = value.Score, Explanation = value.Explanation }).ToList(),
        Products = source.Products.Select(value => new ClientAdviceProductPackage { ProductName = value.ProductName, Provider = value.Provider, ProductType = value.ProductType, IsRecommended = value.IsRecommended, Amount = value.Amount, AllocationPercent = value.AllocationPercent, Motivation = value.Motivation, SupportingDocumentPath = value.SupportingDocumentPath }).ToList(),
        FactSources = source.FactSources.Select(value => new ClientAdviceFactSourcePackage { FactName = value.FactName, SourceDate = value.SourceDate, DocumentPath = value.DocumentPath, Notes = value.Notes }).ToList(),
        InvestmentLinks = source.InvestmentLinks.Select(value => new ClientAdviceInvestmentPackage { Owner = ClientRef(value.InvestmentAccount.Client), LegacyInvestmentAccountId = value.InvestmentAccount.LegacyInvestmentAccountId, AccountNumber = value.InvestmentAccount.AccountNumber, Administrator = value.InvestmentAccount.Administrator, Role = value.Role }).ToList(),
        Findings = source.ReviewFindings.Select(value => new ClientAdviceFindingPackage { Severity = value.Severity, Category = value.Category, AffectedField = value.AffectedField, Finding = value.Finding, EvidenceReference = value.EvidenceReference, RecommendedCorrection = value.RecommendedCorrection, Status = value.Status, Resolution = value.Resolution, PerformedBy = value.PerformedBy, PerformedAtUtc = value.PerformedAtUtc, ResolvedBy = value.ResolvedBy, ResolvedAtUtc = value.ResolvedAtUtc }).ToList(),
        Approvals = source.Approvals.Select(value => new ClientAdviceApprovalPackage { Reviewer = value.Reviewer, Decision = value.Decision, Reason = value.Reason, DecidedAtUtc = value.DecidedAtUtc }).ToList(),
        Documents = source.Documents.Select(ToPackage).ToList()
    };

    private static ClientAdviceDocumentPackage ToPackage(ClientAdviceDocument value) => new() { DocumentType = value.DocumentType, FileName = value.FileName, SourcePath = value.SourcePath, FileSha256 = value.FileSha256, FileSizeBytes = value.FileSizeBytes, FileLastWriteTimeUtc = value.FileLastWriteTimeUtc, RecordedBy = value.RecordedBy, RecordedAtUtc = value.RecordedAtUtc };
    private static ClientAdviceClientRef ClientRef(Client value) => new() { LegacyClientId = value.LegacyClientId, KanaanId = value.KanaanId, DisplayName = value.DisplayName };

    private static void CopyCase(ClientAdviceCasePackage source, ClientAdviceCase target)
    {
        target.PreviousAdviceCaseId = null;
        target.TransferKey = source.TransferKey; target.AdviceType = source.AdviceType; target.RiskMethodologyCode = source.RiskMethodologyCode;
        target.Status = source.Status; target.Revision = source.Revision; target.AdviceDate = source.AdviceDate; target.AdviserName = source.AdviserName;
        target.PreparedBy = source.PreparedBy; target.CreatedAtUtc = source.CreatedAtUtc; target.UpdatedAtUtc = source.UpdatedAtUtc;
        target.AdviceScope = source.AdviceScope; target.MeetingSummary = source.MeetingSummary; target.NeedsAndObjectives = source.NeedsAndObjectives;
        target.FinancialSituation = source.FinancialSituation; target.AdviceLimitations = source.AdviceLimitations; target.ProductKnowledgeSummary = source.ProductKnowledgeSummary;
        target.InvestmentAmount = source.InvestmentAmount; target.InvestmentPortfolioPercent = source.InvestmentPortfolioPercent;
        target.DomesticPreferencePercent = source.DomesticPreferencePercent; target.OffshorePreferencePercent = source.OffshorePreferencePercent;
        target.CalculatedRiskScore = source.CalculatedRiskScore; target.CalculatedRiskLevel = source.CalculatedRiskLevel; target.FinalRiskLevel = source.FinalRiskLevel;
        target.RiskOverrideReason = source.RiskOverrideReason; target.RecommendationSummary = source.RecommendationSummary;
        target.RecommendationRationale = source.RecommendationRationale; target.CostsAndFees = source.CostsAndFees; target.TaxConsequences = source.TaxConsequences;
        target.LiquidityAndRestrictions = source.LiquidityAndRestrictions; target.MaterialRisks = source.MaterialRisks; target.IsReplacement = source.IsReplacement;
        target.ReplacementConsequences = source.ReplacementConsequences; target.ClientDeparture = source.ClientDeparture; target.WarningsGiven = source.WarningsGiven;
        target.FrozenSnapshotJson = source.FrozenSnapshotJson; target.FrozenSnapshotSha256 = source.FrozenSnapshotSha256;
        target.SubmittedAtUtc = source.SubmittedAtUtc; target.ApprovedAtUtc = source.ApprovedAtUtc; target.IssuedAtUtc = source.IssuedAtUtc; target.CompletedAtUtc = source.CompletedAtUtc;
    }

    private void ClearChildren(ClientAdviceCase target)
    {
        db.ClientAdviceParticipants.RemoveRange(target.Participants); db.ClientAdviceRiskResponses.RemoveRange(target.RiskResponses);
        db.ClientAdviceProducts.RemoveRange(target.Products); db.ClientAdviceFactSources.RemoveRange(target.FactSources);
        db.ClientAdviceInvestmentLinks.RemoveRange(target.InvestmentLinks); db.ClientAdviceReviewFindings.RemoveRange(target.ReviewFindings);
        db.ClientAdviceApprovals.RemoveRange(target.Approvals); db.ClientAdviceDocuments.RemoveRange(target.Documents);
        target.Participants.Clear(); target.RiskResponses.Clear(); target.Products.Clear(); target.FactSources.Clear();
        target.InvestmentLinks.Clear(); target.ReviewFindings.Clear(); target.Approvals.Clear(); target.Documents.Clear();
    }

    private static ClientAdviceDocument ToEntity(ClientAdviceDocumentPackage source, int clientId, ClientAdviceTransferPackage package, string? targetFolder)
    {
        var target = new ClientAdviceDocument { ClientId = clientId };
        CopyDocument(source, target, MapPath(source.SourcePath, package, targetFolder));
        return target;
    }

    private static void CopyDocument(ClientAdviceDocumentPackage source, ClientAdviceDocument target, string? mappedPath)
    {
        target.DocumentType = source.DocumentType; target.FileName = source.FileName; target.SourcePath = mappedPath;
        target.FileSha256 = source.FileSha256; target.FileSizeBytes = source.FileSizeBytes; target.FileLastWriteTimeUtc = source.FileLastWriteTimeUtc;
        target.RecordedBy = source.RecordedBy; target.RecordedAtUtc = source.RecordedAtUtc;
    }

    private static string? MapPath(string? path, ClientAdviceTransferPackage package, string? targetFolder)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (!IsFilePathReference(path)) return path;
        if (string.IsNullOrWhiteSpace(targetFolder)) return path;
        var embedded = package.EmbeddedFiles.SingleOrDefault(value =>
            string.Equals(NormalizeWindowsPath(value.OriginalPath), NormalizeWindowsPath(path), StringComparison.OrdinalIgnoreCase));
        if (embedded is not null)
            return Path.Combine(targetFolder, "KCAS Advice Imports", package.PackageId, embedded.StoredFileName);
        if (string.IsNullOrWhiteSpace(package.SourceClientFolder)) return path;
        var source = NormalizeWindowsPath(package.SourceClientFolder).TrimEnd('\\');
        var value = NormalizeWindowsPath(path);
        if (value.Equals(source, StringComparison.OrdinalIgnoreCase)) return NormalizeWindowsPath(targetFolder);
        if (!value.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Advice document '{path}' is outside the source client folder and cannot be mapped safely.");
        return NormalizeWindowsPath(targetFolder).TrimEnd('\\') + value[source.Length..];
    }

    private static async Task<List<ClientAdviceEmbeddedFilePackage>> BuildEmbeddedFilesAsync(
        ClientAdviceTransferPackage package,
        CancellationToken cancellationToken)
    {
        var result = new List<ClientAdviceEmbeddedFilePackage>();
        var sourceRoot = string.IsNullOrWhiteSpace(package.SourceClientFolder)
            ? null : NormalizeWindowsPath(package.SourceClientFolder).TrimEnd('\\');
        foreach (var path in package.AllDocumentPaths().Where(IsFilePathReference)
                     .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = NormalizeWindowsPath(path);
            var underRoot = sourceRoot is not null &&
                (normalized.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(sourceRoot + "\\", StringComparison.OrdinalIgnoreCase));
            if (underRoot) continue;
            if (!File.Exists(path))
                throw new ValidationException($"External advice document '{path}' could not be found for secure packaging.");
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var hash = Sha256(bytes);
            result.Add(new ClientAdviceEmbeddedFilePackage
            {
                OriginalPath = path,
                StoredFileName = $"{hash[..12]}-{Path.GetFileName(path)}",
                Sha256 = hash,
                ContentBase64 = Convert.ToBase64String(bytes)
            });
        }
        return result;
    }

    private static async Task<string?> ExtractEmbeddedFilesAsync(
        ClientAdviceTransferPackage package,
        string? targetClientFolder,
        CancellationToken cancellationToken)
    {
        if (package.EmbeddedFiles.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(targetClientFolder))
            throw new ValidationException("The live client folder is required to extract packaged advice documents.");
        var directory = Path.Combine(targetClientFolder, "KCAS Advice Imports", package.PackageId);
        Directory.CreateDirectory(directory);
        foreach (var source in package.EmbeddedFiles)
        {
            var bytes = Convert.FromBase64String(source.ContentBase64);
            if (!string.Equals(Sha256(bytes), source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"Packaged advice file '{source.StoredFileName}' failed its integrity check.");
            await File.WriteAllBytesAsync(Path.Combine(directory, source.StoredFileName), bytes, cancellationToken);
        }
        return directory;
    }

    private static string NormalizeWindowsPath(string value) => value.Trim().Replace('/', '\\');
    internal static bool IsFilePathReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        return candidate.StartsWith("\\\\", StringComparison.Ordinal) ||
               candidate[0] == '/' ||
               (candidate.Length >= 3 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':' &&
                candidate[2] is '\\' or '/');
    }
    private static string ClientKey(ClientAdviceClientRef value) => value.LegacyClientId.HasValue ? $"L:{value.LegacyClientId}" : $"K:{value.KanaanId}|N:{value.DisplayName}";
    private static string InvestmentKey(ClientAdviceInvestmentPackage value) => $"{ClientKey(value.Owner)}|L:{value.LegacyInvestmentAccountId}|A:{ClientInvestmentStatusClassifier.NormalizeAccountNumber(value.AccountNumber)}|P:{value.Administrator?.Trim().ToUpperInvariant()}";

    private static List<string> ValidatePackage(ClientAdviceTransferPackage package)
    {
        var errors = new List<string>();
        if (package.FormatVersion != PackageVersion) errors.Add($"Package format {package.FormatVersion} is not supported by this KCAS version.");
        if (!Guid.TryParse(package.PackageId, out _)) errors.Add("The package identifier is invalid.");
        if (string.IsNullOrWhiteSpace(package.Client.DisplayName)) errors.Add("The package has no client identity.");
        if (package.Cases.Count == 0 && package.HistoricalDocuments.Count == 0) errors.Add("The package has no advice records.");
        if (package.Cases.Any(value => !Guid.TryParse(value.TransferKey, out _))) errors.Add("One or more advice cases have an invalid transfer identity.");
        var keys = package.Cases.Select(value => value.TransferKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count != package.Cases.Count) errors.Add("The package contains duplicate advice case identities.");
        if (package.Cases.Any(value => value.PreviousCaseTransferKey is not null && !keys.Contains(value.PreviousCaseTransferKey))) errors.Add("An advice revision refers to a previous case outside the package.");
        foreach (var file in package.EmbeddedFiles)
        {
            try
            {
                var bytes = Convert.FromBase64String(file.ContentBase64);
                if (!string.Equals(Sha256(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Embedded advice file '{file.StoredFileName}' failed its integrity check.");
            }
            catch (FormatException) { errors.Add($"Embedded advice file '{file.StoredFileName}' is invalid."); }
        }
        return errors;
    }

    private ClientAdviceTransferPackage DecryptPackage(byte[] encrypted, string passphrase, out string hash)
    {
        try
        {
            var plaintext = Decrypt(encrypted, passphrase); hash = Sha256(plaintext);
            return JsonSerializer.Deserialize<ClientAdviceTransferPackage>(plaintext, JsonOptions) ?? throw new ValidationException("The package payload is empty.");
        }
        catch (CryptographicException) { throw new ValidationException("The package could not be decrypted. Check the passphrase and package integrity."); }
        catch (JsonException) { throw new ValidationException("The decrypted package is not valid KCAS client advice transfer data."); }
        catch (EndOfStreamException) { throw new ValidationException("The encrypted package is truncated or invalid."); }
    }

    private static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(PackageMagic));
        CryptographicOperations.ZeroMemory(key);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(PackageMagic); writer.Write(Pbkdf2Iterations); writer.Write(salt.Length); writer.Write(salt);
        writer.Write(nonce.Length); writer.Write(nonce); writer.Write(tag.Length); writer.Write(tag); writer.Write(ciphertext.Length); writer.Write(ciphertext);
        return stream.ToArray();
    }

    private static byte[] Decrypt(byte[] encrypted, string passphrase)
    {
        if (encrypted.Length > MaximumPackageBytes + 1024) throw new ValidationException("The package exceeds the 25 MB safety limit.");
        using var stream = new MemoryStream(encrypted); using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadString() != PackageMagic) throw new ValidationException("This is not a KCAS client advice transfer package.");
        var iterations = reader.ReadInt32(); if (iterations is < 100_000 or > 1_000_000) throw new ValidationException("The package encryption parameters are invalid.");
        var salt = ReadExact(reader, 16, "salt"); var nonce = ReadExact(reader, 12, "nonce"); var tag = ReadExact(reader, 16, "authentication tag");
        var length = reader.ReadInt32(); if (length is < 1 or > MaximumPackageBytes || length > stream.Length - stream.Position) throw new ValidationException("The package payload length is invalid.");
        var ciphertext = reader.ReadBytes(length); var plaintext = new byte[length];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(PackageMagic));
        CryptographicOperations.ZeroMemory(key); return plaintext;
    }

    private static byte[] ReadExact(BinaryReader reader, int length, string label)
    {
        if (reader.ReadInt32() != length) throw new ValidationException($"The package {label} is invalid.");
        var result = reader.ReadBytes(length); return result.Length == length ? result : throw new ValidationException($"The package {label} is truncated.");
    }

    private string ResolveStorageRoot()
    {
        var configured = configuration["ClientAdviceTransfers:StorageRoot"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        const string productionSharedRoot = @"D:\Deploy\KCAS\shared";
        if (Directory.Exists(productionSharedRoot)) return Path.Combine(productionSharedRoot, "advice-transfer-packages");
        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "backups", "advice-transfer-packages"));
    }

    private static ClientAdviceTransferRecord TransferRecord(ClientAdviceTransferPackage package, int clientId, string direction, string status, string hash, string fileName, string path) => new() { PackageId = package.PackageId, Direction = direction, ContentSha256 = hash, ClientId = clientId, Status = status, FileName = fileName, StoragePath = path, CaseCount = package.Cases.Count, DocumentCount = package.DocumentCount, SummaryJson = JsonSerializer.Serialize(PackageSummary(package), JsonOptions) };
    private static object PackageSummary(ClientAdviceTransferPackage package) => new { package.PackageId, package.CreatedAtUtc, package.ExportedBy, package.SourceEnvironment, Client = package.Client.DisplayName, package.Client.KanaanId, Cases = package.Cases.Count, Documents = package.DocumentCount, EmbeddedFiles = package.EmbeddedFiles.Count };
    private void AddAudit(string type, int id, string action, string user, string reason, object value) => db.ComplianceAuditEvents.Add(new ComplianceAuditEvent { EntityType = type, EntityId = id, Action = action, UserName = user, Reason = reason, NewValueJson = JsonSerializer.Serialize(value, JsonOptions), TimestampUtc = DateTime.UtcNow });
    private static string BuildFileName(ClientAdviceTransferPackage package) => $"KCAS-advice-{SafeName(package.Client.DisplayName)}-{DateTime.UtcNow:yyyyMMdd}-{package.PackageId.Replace("-", "")[..12]}.kcas-advice";
    private static string SafeName(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character == '-').Take(48).ToArray());
    private static string Require(string? value, string message) => string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();
    private static void ValidatePassphrase(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length < 7) throw new ValidationException("Use a package passphrase of at least 7 characters."); }
    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

public sealed class ClientAdviceTransferPackage
{
    public int FormatVersion { get; set; }
    public string PackageId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string ExportedBy { get; set; } = "";
    public string ExportReason { get; set; } = "";
    public string SourceEnvironment { get; set; } = "";
    public ClientAdviceClientRef Client { get; set; } = new();
    public string? SourceClientFolder { get; set; }
    public List<ClientAdviceCasePackage> Cases { get; set; } = [];
    public List<ClientAdviceDocumentPackage> HistoricalDocuments { get; set; } = [];
    public List<ClientAdviceAuditPackage> AuditEvents { get; set; } = [];
    public List<ClientAdviceEmbeddedFilePackage> EmbeddedFiles { get; set; } = [];
    public int DocumentCount => HistoricalDocuments.Count + Cases.Sum(value => value.Documents.Count);
    public IEnumerable<ClientAdviceClientRef> AllClientReferences() => Cases.SelectMany(value => value.Participants.Select(item => item.Client).Concat(value.InvestmentLinks.Select(item => item.Owner))).Prepend(Client);
    public IEnumerable<string?> AllDocumentPaths() => HistoricalDocuments.Select(value => value.SourcePath).Concat(Cases.SelectMany(value => value.Documents.Select(item => item.SourcePath).Concat(value.FactSources.Select(item => item.DocumentPath)).Concat(value.Products.Select(item => item.SupportingDocumentPath))));
}

public sealed class ClientAdviceClientRef { public int? LegacyClientId { get; set; } public string? KanaanId { get; set; } public string DisplayName { get; set; } = ""; }
public sealed class ClientAdviceCasePackage
{
    public string TransferKey { get; set; } = ""; public string? PreviousCaseTransferKey { get; set; }
    public string AdviceType { get; set; } = ""; public string RiskMethodologyCode { get; set; } = ""; public string Status { get; set; } = ""; public int Revision { get; set; }
    public DateOnly AdviceDate { get; set; } public string AdviserName { get; set; } = ""; public string PreparedBy { get; set; } = ""; public DateTime CreatedAtUtc { get; set; } public DateTime UpdatedAtUtc { get; set; }
    public string AdviceScope { get; set; } = ""; public string MeetingSummary { get; set; } = ""; public string NeedsAndObjectives { get; set; } = ""; public string FinancialSituation { get; set; } = ""; public string AdviceLimitations { get; set; } = ""; public string ProductKnowledgeSummary { get; set; } = "";
    public decimal? InvestmentAmount { get; set; } public decimal? InvestmentPortfolioPercent { get; set; } public decimal? DomesticPreferencePercent { get; set; } public decimal? OffshorePreferencePercent { get; set; }
    public int? CalculatedRiskScore { get; set; } public string? CalculatedRiskLevel { get; set; } public string? FinalRiskLevel { get; set; } public string? RiskOverrideReason { get; set; }
    public string RecommendationSummary { get; set; } = ""; public string RecommendationRationale { get; set; } = ""; public string CostsAndFees { get; set; } = ""; public string TaxConsequences { get; set; } = ""; public string LiquidityAndRestrictions { get; set; } = ""; public string MaterialRisks { get; set; } = "";
    public bool IsReplacement { get; set; } public string ReplacementConsequences { get; set; } = ""; public string ClientDeparture { get; set; } = ""; public string WarningsGiven { get; set; } = "";
    public string? FrozenSnapshotJson { get; set; } public string? FrozenSnapshotSha256 { get; set; } public DateTime? SubmittedAtUtc { get; set; } public DateTime? ApprovedAtUtc { get; set; } public DateTime? IssuedAtUtc { get; set; } public DateTime? CompletedAtUtc { get; set; }
    public List<ClientAdviceParticipantPackage> Participants { get; set; } = []; public List<ClientAdviceRiskResponsePackage> RiskResponses { get; set; } = []; public List<ClientAdviceProductPackage> Products { get; set; } = []; public List<ClientAdviceFactSourcePackage> FactSources { get; set; } = []; public List<ClientAdviceInvestmentPackage> InvestmentLinks { get; set; } = []; public List<ClientAdviceFindingPackage> Findings { get; set; } = []; public List<ClientAdviceApprovalPackage> Approvals { get; set; } = []; public List<ClientAdviceDocumentPackage> Documents { get; set; } = [];
}
public sealed class ClientAdviceParticipantPackage { public ClientAdviceClientRef Client { get; set; } = new(); public string Role { get; set; } = ""; }
public sealed class ClientAdviceRiskResponsePackage { public string QuestionCode { get; set; } = ""; public string AnswerCode { get; set; } = ""; public int Score { get; set; } public string? Explanation { get; set; } }
public sealed class ClientAdviceProductPackage { public string ProductName { get; set; } = ""; public string? Provider { get; set; } public string? ProductType { get; set; } public bool IsRecommended { get; set; } public decimal? Amount { get; set; } public decimal? AllocationPercent { get; set; } public string? Motivation { get; set; } public string? SupportingDocumentPath { get; set; } }
public sealed class ClientAdviceFactSourcePackage { public string FactName { get; set; } = ""; public DateOnly? SourceDate { get; set; } public string DocumentPath { get; set; } = ""; public string? Notes { get; set; } }
public sealed class ClientAdviceInvestmentPackage { public ClientAdviceClientRef Owner { get; set; } = new(); public int? LegacyInvestmentAccountId { get; set; } public string? AccountNumber { get; set; } public string? Administrator { get; set; } public string Role { get; set; } = ""; }
public sealed class ClientAdviceFindingPackage { public string Severity { get; set; } = ""; public string Category { get; set; } = ""; public string? AffectedField { get; set; } public string Finding { get; set; } = ""; public string? EvidenceReference { get; set; } public string? RecommendedCorrection { get; set; } public string Status { get; set; } = ""; public string? Resolution { get; set; } public string PerformedBy { get; set; } = ""; public DateTime PerformedAtUtc { get; set; } public string? ResolvedBy { get; set; } public DateTime? ResolvedAtUtc { get; set; } }
public sealed class ClientAdviceApprovalPackage { public string Reviewer { get; set; } = ""; public string Decision { get; set; } = ""; public string Reason { get; set; } = ""; public DateTime DecidedAtUtc { get; set; } }
public sealed class ClientAdviceDocumentPackage { public string DocumentType { get; set; } = ""; public string FileName { get; set; } = ""; public string? SourcePath { get; set; } public string? FileSha256 { get; set; } public long? FileSizeBytes { get; set; } public DateTime? FileLastWriteTimeUtc { get; set; } public string? RecordedBy { get; set; } public DateTime RecordedAtUtc { get; set; } }
public sealed class ClientAdviceAuditPackage { public string CaseTransferKey { get; set; } = ""; public string Action { get; set; } = ""; public string UserName { get; set; } = ""; public DateTime TimestampUtc { get; set; } public string? Reason { get; set; } public string? OldValueJson { get; set; } public string? NewValueJson { get; set; } }
public sealed class ClientAdviceEmbeddedFilePackage { public string OriginalPath { get; set; } = ""; public string StoredFileName { get; set; } = ""; public string Sha256 { get; set; } = ""; public string ContentBase64 { get; set; } = ""; }

public sealed class ClientAdviceTransferPreview
{
    public ClientAdviceTransferPackage Package { get; init; } = new(); public string ContentSha256 { get; init; } = ""; public bool AlreadyApplied { get; init; }
    public List<string> Conflicts { get; init; } = []; public List<string> Warnings { get; init; } = []; public int? TargetClientId { get; init; } public string? TargetClientFolder { get; init; }
    public Dictionary<string, int> ClientMatches { get; init; } = []; public Dictionary<string, int> InvestmentMatches { get; init; } = [];
    public bool CanApply => !AlreadyApplied && Conflicts.Count == 0 && TargetClientId.HasValue;
}

public sealed record ClientAdviceTransferClientOption(int ClientId, string DisplayName, string? KanaanId, int CaseCount, int DocumentCount);
public sealed record ClientAdviceTransferExportResult(string PackageId, string FileName, string StoragePath, long SizeBytes, string ClientName, int CaseCount, int DocumentCount);
public sealed record ClientAdviceTransferImportResult(string PackageId, int ClientId, int CaseCount, int DocumentCount, string FileName, string StoragePath);
public sealed record ClientAdvicePackageFile(string Path, string FileName);
