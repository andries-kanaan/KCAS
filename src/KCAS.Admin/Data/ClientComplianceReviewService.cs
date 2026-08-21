using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientComplianceReviewService(
    ApplicationDbContext db,
    ClientOperationalVerificationService verificationService,
    InvestmentReconciliationService investmentService,
    ClientEvidenceReadinessService evidenceService,
    ClientRiskAssessmentService riskService)
{
    public async Task<ClientComplianceFolderScanModel?> LoadLatestFolderScanAsync(
        int clientId,
        CancellationToken cancellationToken = default)
    {
        var clientFolder = await db.Clients.AsNoTracking()
            .Where(item => item.Id == clientId)
            .Select(item => item.ClientFolder)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientFolder))
        {
            return null;
        }

        return await db.ClientEvidenceScanRuns.AsNoTracking()
            .Where(item => item.RootPath == clientFolder)
            .OrderByDescending(item => item.StartedAtUtc)
            .Select(item => new ClientComplianceFolderScanModel(
                item.Id,
                item.Status,
                item.StartedAtUtc,
                item.FinishedAtUtc,
                item.TotalFiles,
                item.LinkedFiles,
                item.UnmatchedFiles,
                item.AmbiguousFiles,
                item.ErrorMessage))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClientComplianceReviewModel> LoadAsync(
        int clientId,
        CancellationToken cancellationToken = default)
    {
        var client = await db.Clients.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.FinancialProfile)
            .Include(item => item.InvestmentAccounts).ThenInclude(account => account.Transactions)
            .Include(item => item.FundValuations)
            .Include(item => item.Notes)
            .SingleOrDefaultAsync(item => item.Id == clientId, cancellationToken)
            ?? throw new KeyNotFoundException("Client not found.");

        var operational = await verificationService.LoadClientAsync(clientId);
        var investments = await investmentService.LoadClientReviewAsync(clientId, cancellationToken);
        var evidence = await evidenceService.LoadClientReadinessAsync(clientId);
        var risk = await riskService.LoadAsync(clientId);
        var activeScanRoot = await db.ClientEvidenceScanRoots
            .AsNoTracking()
            .Where(root => root.IsActive)
            .OrderByDescending(root => root.Id)
            .Select(root => root.RootPath)
            .FirstOrDefaultAsync(cancellationToken);
        var clientFolderExists = !string.IsNullOrWhiteSpace(client.ClientFolder) && Directory.Exists(client.ClientFolder);
        var folderRecommendations = clientFolderExists
            ? []
            : await ClientFolderRecommendations.BuildAsync(db, client, activeScanRoot, cancellationToken);
        var latestFolderScan = string.IsNullOrWhiteSpace(client.ClientFolder)
            ? null
            : await db.ClientEvidenceScanRuns.AsNoTracking()
                .Where(item => item.RootPath == client.ClientFolder)
                .OrderByDescending(item => item.StartedAtUtc)
                .Select(item => new ClientComplianceFolderScanModel(
                    item.Id,
                    item.Status,
                    item.StartedAtUtc,
                    item.FinishedAtUtc,
                    item.TotalFiles,
                    item.LinkedFiles,
                    item.UnmatchedFiles,
                    item.AmbiguousFiles,
                    item.ErrorMessage))
                .FirstOrDefaultAsync(cancellationToken);

        var linkedClients = await db.Clients.AsNoTracking()
            .Where(item => item.Id != clientId &&
                ((!string.IsNullOrWhiteSpace(client.KanaanId) && item.KanaanId == client.KanaanId) ||
                 (!string.IsNullOrWhiteSpace(client.ClientFolder) && item.ClientFolder == client.ClientFolder)))
            .OrderBy(item => item.DisplayName)
            .Select(item => new ClientComplianceLinkedClientModel(
                item.Id,
                item.DisplayName,
                item.KanaanId,
                item.LifecycleStatus,
                item.ClientFolder == client.ClientFolder))
            .ToListAsync(cancellationToken);

        var lifecycleProposal = BuildLifecycleProposal(client);
        var pendingFacts = operational.VerificationItems
            .Where(item => item.Status == ClientVerificationStatuses.Pending)
            .ToList();
        var screeningRequirements = evidence.Requirements
            .Where(item => string.Equals(item.RequirementGroup, "Screening", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var evidenceRequirements = evidence.Requirements
            .Where(item => !string.Equals(item.RequirementGroup, "Screening", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var assessmentComplete = risk.Assessment?.Status is
            ClientRiskAssessmentStatuses.Finalised or ClientRiskAssessmentStatuses.Approved;
        var lifecycleComplete = client.LifecycleStatus != ClientLifecycleStatuses.Unreviewed;
        var factsComplete = pendingFacts.All(item => !item.IsBlocking);
        var evidenceComplete = evidenceRequirements.All(item => !item.IsBlocked) && evidence.OwnershipBlockers.Count == 0;
        var screeningComplete = screeningRequirements.All(item => !item.IsBlocked);

        var sections = new List<ClientComplianceReviewSectionModel>
        {
            new("folder", "Client folder and scan", !string.IsNullOrWhiteSpace(client.ClientFolder) && latestFolderScan?.Status == ClientEvidenceScanStatuses.Completed,
                string.IsNullOrWhiteSpace(client.ClientFolder)
                    ? "Select the client's evidence folder."
                    : latestFolderScan is null
                        ? "Folder saved; scan required."
                        : latestFolderScan.Status == ClientEvidenceScanStatuses.Completed
                            ? $"Scan completed with {latestFolderScan.LinkedFiles} linked file(s)."
                            : $"Latest scan: {latestFolderScan.Status}."),
            new("context", "Client and relationship context", lifecycleComplete,
                lifecycleComplete ? $"Lifecycle classified as {client.LifecycleStatus}." : $"Proposed lifecycle: {lifecycleProposal.Status}."),
            new("facts", "Facts and conflicts", factsComplete,
                pendingFacts.Count == 0 ? "No facts require human verification." : $"{pendingFacts.Count} fact(s) require review; {pendingFacts.Count(item => item.IsBlocking)} blocking."),
            new("investments", "Investment reconciliation", investments.IsComplete,
                investments.IsComplete ? $"All {investments.Accounts.Count} investment account(s) verified." : $"{investments.Accounts.Count(item => !item.IsVerified) + investments.UnmatchedIssues.Count} investment item(s) require verification."),
            new("evidence", "Evidence readiness", evidenceComplete,
                evidenceComplete ? "Required non-screening evidence is ready." : $"{evidenceRequirements.Count(item => item.IsBlocked) + evidence.OwnershipBlockers.Count} evidence blocker(s)."),
            new("screening", "Screening", screeningComplete,
                screeningComplete ? "Required screening reviews are complete." : $"{screeningRequirements.Count(item => item.IsBlocked)} screening review(s) remain."),
            new("risk", "Risk assessment", assessmentComplete,
                assessmentComplete ? $"Assessment {risk.Assessment!.Status}: {risk.Assessment.FinalRating ?? risk.Assessment.CalculatedRating}." : risk.Assessment is null ? "Risk assessment not started." : $"Assessment status: {risk.Assessment.Status}.")
        };

        return new ClientComplianceReviewModel
        {
            ClientId = client.Id,
            DisplayName = ClientNameFormatter.FullNameAndSurname(client),
            KanaanId = client.KanaanId,
            ClientCategory = client.ClientCategory,
            ClientFolder = client.ClientFolder,
            ClientFolderExists = clientFolderExists,
            ActiveScanRoot = activeScanRoot,
            FolderRecommendations = folderRecommendations,
            LifecycleStatus = client.LifecycleStatus,
            LifecycleReason = client.LifecycleReason,
            LifecycleProposal = lifecycleProposal,
            RetirementAge = client.FinancialProfile?.RetirementAge,
            InvestmentAccountCount = investments.Accounts.Count,
            CurrentInvestmentCount = investments.Accounts.Count(item => item.IsCurrent),
            CurrentInvestmentValueZar = investments.Accounts.Sum(item => item.CurrentValueZar ?? 0m),
            LatestNoteDate = client.Notes.Max(item => item.NoteDate),
            LatestFolderScan = latestFolderScan,
            LinkedClients = linkedClients,
            PendingFacts = pendingFacts,
            Investments = investments,
            Evidence = evidence,
            Risk = risk,
            EvidenceRequirements = evidenceRequirements,
            ScreeningRequirements = screeningRequirements,
            Sections = sections,
            IsComplete = sections.All(item => item.IsComplete),
            NextAction = BuildNextAction(client.Id, sections, latestFolderScan)
        };
    }

    internal static ClientLifecycleProposal BuildLifecycleProposal(Client client)
    {
        if (client.LifecycleStatus != ClientLifecycleStatuses.Unreviewed)
        {
            return new ClientLifecycleProposal(
                client.LifecycleStatus,
                client.LifecycleReason ?? "Lifecycle classification already recorded.",
                false);
        }

        var currentAccounts = client.InvestmentAccounts
            .Where(account => ClientInvestmentStatusClassifier.Evaluate(account, client.FundValuations).IsCurrent)
            .ToList();
        if (currentAccounts.Count > 0)
        {
            return new ClientLifecycleProposal(
                ClientLifecycleStatuses.Current,
                $"KCAS found {currentAccounts.Count} investment account(s) with matching current valuations.",
                true);
        }

        if (client.InvestmentAccounts.Count > 0 && client.InvestmentAccounts.All(account => account.SurrenderDate.HasValue))
        {
            return new ClientLifecycleProposal(
                ClientLifecycleStatuses.Historical,
                "All recorded investment accounts have effective surrender or transfer dates and none has a current valuation.",
                true);
        }

        return new ClientLifecycleProposal(
            ClientLifecycleStatuses.Unreviewed,
            "KCAS cannot determine lifecycle reliably from the available investment records.",
            false);
    }

    private static ClientComplianceNextAction BuildNextAction(
        int clientId,
        IReadOnlyList<ClientComplianceReviewSectionModel> sections,
        ClientComplianceFolderScanModel? scan)
    {
        var first = sections.FirstOrDefault(item => !item.IsComplete);
        return first?.Code switch
        {
            "folder" when scan?.Status is ClientEvidenceScanStatuses.Running or ClientEvidenceScanStatuses.Cancelling =>
                new("Refresh scan status", $"/clients/{clientId}/compliance-review#folder"),
            "folder" => new("Select or scan client folder", $"/clients/{clientId}/compliance-review#folder"),
            "context" => new("Confirm lifecycle proposal", $"/clients/{clientId}/compliance-review#context"),
            "facts" => new("Resolve client fact conflicts", $"/clients/{clientId}/verification"),
            "investments" => new("Verify investments", $"/clients/{clientId}/investments/reconciliation"),
            "evidence" => new("Resolve evidence requirements", $"/clients/{clientId}/evidence"),
            "screening" => new("Complete screening reviews", $"/clients/{clientId}/evidence"),
            "risk" => new("Complete risk assessment", $"/clients/{clientId}/risk"),
            _ => new("Review complete", $"/clients/{clientId}/compliance-review")
        };
    }
}

public sealed class ClientComplianceReviewModel
{
    public int ClientId { get; init; }
    public string DisplayName { get; init; } = "";
    public string? KanaanId { get; init; }
    public string ClientCategory { get; init; } = "";
    public string? ClientFolder { get; init; }
    public bool ClientFolderExists { get; init; }
    public string? ActiveScanRoot { get; init; }
    public List<ClientFolderRecommendation> FolderRecommendations { get; init; } = [];
    public string LifecycleStatus { get; init; } = "";
    public string? LifecycleReason { get; init; }
    public required ClientLifecycleProposal LifecycleProposal { get; init; }
    public int? RetirementAge { get; init; }
    public int InvestmentAccountCount { get; init; }
    public int CurrentInvestmentCount { get; init; }
    public decimal CurrentInvestmentValueZar { get; init; }
    public DateOnly? LatestNoteDate { get; init; }
    public ClientComplianceFolderScanModel? LatestFolderScan { get; set; }
    public List<ClientComplianceLinkedClientModel> LinkedClients { get; init; } = [];
    public List<ClientVerificationItem> PendingFacts { get; init; } = [];
    public required ClientInvestmentReconciliationPageModel Investments { get; init; }
    public required ClientEvidenceReadinessModel Evidence { get; init; }
    public required ClientRiskAssessmentPageModel Risk { get; init; }
    public List<ClientEvidenceRequirementStatusModel> EvidenceRequirements { get; init; } = [];
    public List<ClientEvidenceRequirementStatusModel> ScreeningRequirements { get; init; } = [];
    public List<ClientComplianceReviewSectionModel> Sections { get; init; } = [];
    public required ClientComplianceNextAction NextAction { get; init; }
    public bool IsComplete { get; init; }
}

public sealed record ClientLifecycleProposal(string Status, string Reason, bool CanConfirm);
public sealed record ClientComplianceReviewSectionModel(string Code, string Title, bool IsComplete, string Summary);
public sealed record ClientComplianceNextAction(string Label, string Url);
public sealed record ClientComplianceFolderScanModel(
    int Id,
    string Status,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int TotalFiles,
    int LinkedFiles,
    int UnmatchedFiles,
    int AmbiguousFiles,
    string? ErrorMessage);
public sealed record ClientComplianceLinkedClientModel(
    int ClientId,
    string DisplayName,
    string? KanaanId,
    string LifecycleStatus,
    bool SharesFolder);
