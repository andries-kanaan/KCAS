using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientAdviceService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static readonly IReadOnlyList<ClientAdviceRiskQuestion> RiskQuestions =
    [
        Question("HORIZON", "What is the time horizon for this investment?", "Capacity", [("1-2 years", 1), ("3-4 years", 4), ("5-7 years", 7), ("8-10 years", 8), ("More than 10 years", 10)]),
        Question("RETIREMENT", "How close is the client to the planned retirement date?", "Capacity", [("Already retired", 1), ("1-5 years", 2), ("6-9 years", 5), ("10-15 years", 7), ("More than 15 years", 10)]),
        Question("REGULAR_INCOME", "Does the client need to draw regular income?", "Liquidity", [("Yes", 2), ("No", 5)]),
        Question("WITHDRAWAL", "How much may be withdrawn within the next three years?", "Liquidity", [("More than 50%", 1), ("25% to 50%", 2), ("Less than 25%", 4), ("No withdrawal expected", 6)]),
        Question("DEPENDANTS", "How many dependants does the client have?", "Capacity", [("3 or more", 1), ("2-3", 2), ("1", 3), ("0", 4)]),
        Question("AGE", "Which age range applies?", "Capacity", [("65 or older", 1), ("51-65", 2), ("41-50", 4), ("31-40", 7), ("30 or younger", 10)]),
        Question("INCOME_CHANGE", "During the investment period, is income expected to change?", "Capacity", [("Decrease", 1), ("Remain static", 2), ("Increase", 5)]),
        Question("EMERGENCY_FUNDS", "Are liquid emergency funds available?", "Liquidity", [("No funds available", 1), ("Small amount", 2), ("Adequate amount", 5)]),
        Question("OBJECTIVE", "What is the primary objective for this investment?", "Objective", [("Short-term capital preservation", 3), ("Income with capital preservation", 6), ("Income and capital growth", 9), ("Greater capital growth", 12), ("Long-term superior growth", 15)]),
        Question("ATTITUDE", "What is the client's attitude to investment risk?", "Tolerance", [("Avoid short-term capital loss", 3), ("Lower-risk balanced portfolio", 6), ("Growth-oriented balanced portfolio", 9), ("Aggressive diversified growth", 12), ("Maximum long-term growth", 15)]),
        Question("VOLATILITY", "How tolerant is the client of market volatility?", "Tolerance", [("Not tolerant", 3), ("Uncomfortable with a one-year loss", 6), ("Accepts volatility over three years", 9), ("Accepts loss with longer recovery", 12), ("Long-term volatility is acceptable", 15)])
    ];

    public static readonly IReadOnlyList<ClientAdviceRiskQuestion> PreviousFormRiskQuestions =
    [
        Question("HORIZON", "How long does the client plan to invest before potentially needing the funds?", "Capacity", [("0-2 years", 1), ("3-4 years", 2), ("5-7 years", 3), ("8-10 years", 4), ("10+ years", 5)]),
        Question("RETIREMENT", "How close is the client to the intended retirement date?", "Capacity", [("Already retired", 1), ("0-5 years", 2), ("6-9 years", 3), ("10-15 years", 4), ("15+ years", 5)]),
        Question("REGULAR_INCOME", "Does the client require regular income from this investment?", "Liquidity", [("Yes", 1), ("No", 5)]),
        Question("WITHDRAWAL", "How much may be withdrawn within the next three years?", "Liquidity", [("More than 50%", 1), ("25% to 50%", 2), ("Less than 25%", 4), ("No withdrawals", 5)]),
        Question("DEPENDANTS", "How many financial dependants does the client have?", "Capacity", [("5 or more", 1), ("3-4", 2), ("1-2", 4), ("0", 5)]),
        Question("AGE", "Which age range applies?", "Capacity", [("65 or older", 1), ("51-65", 2), ("41-50", 3), ("31-40", 4), ("Under 30", 5)]),
        Question("INCOME_CHANGE", "During the investment period, is main income expected to change?", "Capacity", [("Decrease or uncertain", 1), ("Remain the same", 3), ("Increase", 5)]),
        Question("EMERGENCY_FUNDS", "Are liquid emergency funds available?", "Liquidity", [("No emergency savings", 1), ("Small amount of savings", 6), ("Adequate savings", 15)]),
        Question("MAJOR_OBLIGATIONS", "Are significant financial obligations anticipated within the next three to five years?", "Capacity", [("Yes, likely", 1), ("Uncertain", 3), ("None", 5)]),
        Question("ATTITUDE", "What is the client's general preference regarding risk and return?", "Tolerance", [("Protect capital in the short term", 1), ("Lower-risk balanced approach", 6), ("Growth-oriented balanced approach", 9), ("Aggressive diversified growth", 12), ("Maximum long-term growth", 15)]),
        Question("VOLATILITY", "How tolerant is the client of short-term market fluctuations or losses?", "Tolerance", [("Not tolerant", 1), ("Uncomfortable with any annual loss", 6), ("Accepts volatility over about three years", 9), ("Accepts loss with recovery over five or more years", 12), ("Long-term volatility is acceptable", 15)])
    ];

    public static readonly IReadOnlyList<ClientAdviceMethodology> Methodologies =
    [
        new(ClientAdviceMethodologies.KcasEvidenced, "KCAS evidenced advice methodology", "18-30 Very low; 31-48 Low; 49-66 Medium; 67-84 Medium to high; 85+ High", RiskQuestions),
        new(ClientAdviceMethodologies.PreviousFormCorrectedBands, "Previous form - corrected non-overlapping bands", "11-<20 Very Conservative; 20-<40 Conservative; 40-<55 Moderate; 55-<75 Moderately Aggressive; 75-85 Aggressive", PreviousFormRiskQuestions)
    ];

    public static ClientAdviceMethodology Methodology(string code) => Methodologies.SingleOrDefault(value => value.Code == code)
        ?? throw new InvalidOperationException($"Advice risk methodology '{code}' is not supported.");

    public async Task<IReadOnlyList<ClientAdviceRegisterItem>> LoadRegisterAsync(string? search = null)
    {
        var query = db.ClientAdviceCases.AsNoTracking().Include(item => item.Client).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => item.Client.DisplayName.Contains(term) ||
                                        (item.Client.KanaanId != null && item.Client.KanaanId.Contains(term)) ||
                                        item.AdviserName.Contains(term));
        }
        return await query.OrderByDescending(item => item.AdviceDate).ThenByDescending(item => item.Id)
            .Select(item => new ClientAdviceRegisterItem(item.Id, item.ClientId, item.Client.DisplayName,
                item.Client.KanaanId, item.AdviceType, item.Status, item.AdviceDate, item.CalculatedRiskLevel,
                item.FinalRiskLevel, item.AdviserName, item.Revision)).ToListAsync();
    }

    public async Task<ClientAdviceClientModel> LoadClientAsync(int clientId)
    {
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        var cases = await db.ClientAdviceCases.AsNoTracking()
            .Where(item => item.ClientId == clientId)
            .OrderByDescending(item => item.AdviceDate).ThenByDescending(item => item.Id)
            .Select(item => new ClientAdviceRegisterItem(item.Id, item.ClientId, item.Client.DisplayName,
                item.Client.KanaanId, item.AdviceType, item.Status, item.AdviceDate, item.CalculatedRiskLevel,
                item.FinalRiskLevel, item.AdviserName, item.Revision)).ToListAsync();
        var historicalDocuments = await db.ClientAdviceDocuments.AsNoTracking()
            .Where(item => item.ClientId == clientId && item.ClientAdviceCaseId == null)
            .OrderByDescending(item => item.FileLastWriteTimeUtc).ThenBy(item => item.FileName)
            .Select(item => new ClientAdviceHistoricalDocument(item.Id, item.DocumentType, item.FileName,
                item.SourcePath, item.FileSha256, item.FileSizeBytes, item.FileLastWriteTimeUtc)).ToListAsync();
        return new(client.Id, ClientNameFormatter.FullNameAndSurname(client), client.KanaanId,
            cases, historicalDocuments);
    }

    public async Task<int> CreateDraftAsync(int clientId, string adviceType, string? userName)
    {
        if (!ClientAdviceTypes.All.Contains(adviceType)) throw new InvalidOperationException("Select a valid advice type.");
        var client = await db.Clients.Include(item => item.FinancialProfile).SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        var user = User(userName);
        var adviceCase = new ClientAdviceCase
        {
            ClientId = clientId,
            AdviceType = adviceType,
            RiskMethodologyCode = ClientAdviceMethodologies.KcasEvidenced,
            AdviserName = user,
            PreparedBy = user,
            FinancialSituation = FinancialSummary(client),
            ProductKnowledgeSummary = "Complete the product-knowledge questions and describe relevant investment experience."
        };
        adviceCase.Participants.Add(new ClientAdviceParticipant { ClientId = clientId });
        db.ClientAdviceCases.Add(adviceCase);
        await db.SaveChangesAsync();
        Audit(adviceCase.Id, "AdviceDraftCreated", user, new { adviceType, clientId });
        await db.SaveChangesAsync();
        return adviceCase.Id;
    }

    public async Task<ClientAdviceEditModel> LoadCaseAsync(int caseId)
    {
        var item = await QueryCase().AsNoTracking().SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new InvalidOperationException("Advice case not found.");
        var model = ClientAdviceEditModel.FromEntity(item, Methodology(item.RiskMethodologyCode).Questions);
        var participantIds = item.Participants.Select(value => value.ClientId).Distinct().ToList();
        var selectedInvestmentIds = item.InvestmentLinks.Select(value => value.ClientInvestmentAccountId).ToHashSet();
        model.AvailableInvestments = await db.ClientInvestmentAccounts.AsNoTracking()
            .Where(value => participantIds.Contains(value.ClientId))
            .OrderBy(value => value.Client.DisplayName).ThenBy(value => value.AccountNumber)
            .Select(value => new ClientAdviceInvestmentOption(value.Id, value.Client.DisplayName, value.AccountNumber,
                value.ProductName, value.Administrator, value.SurrenderDate, false))
            .ToListAsync();
        foreach (var investment in model.AvailableInvestments)
            investment.Selected = selectedInvestmentIds.Contains(investment.InvestmentAccountId);
        return model;
    }

    public async Task SaveDraftAsync(ClientAdviceEditModel model, string? userName)
    {
        var item = await QueryCase().SingleOrDefaultAsync(value => value.Id == model.Id)
            ?? throw new InvalidOperationException("Advice case not found.");
        EnsureEditable(item);
        item.AdviceType = model.AdviceType;
        item.RiskMethodologyCode = Methodology(model.RiskMethodologyCode).Code;
        item.AdviceDate = model.AdviceDate;
        item.AdviserName = model.AdviserName.Trim();
        item.AdviceScope = model.AdviceScope.Trim();
        item.MeetingSummary = model.MeetingSummary.Trim();
        item.NeedsAndObjectives = model.NeedsAndObjectives.Trim();
        item.FinancialSituation = model.FinancialSituation.Trim();
        item.AdviceLimitations = model.AdviceLimitations.Trim();
        item.ProductKnowledgeSummary = model.ProductKnowledgeSummary.Trim();
        item.InvestmentAmount = model.InvestmentAmount;
        item.InvestmentPortfolioPercent = model.InvestmentPortfolioPercent;
        item.DomesticPreferencePercent = model.DomesticPreferencePercent;
        item.OffshorePreferencePercent = model.OffshorePreferencePercent;
        item.FinalRiskLevel = string.IsNullOrWhiteSpace(model.FinalRiskLevel) ? null : model.FinalRiskLevel;
        item.RiskOverrideReason = model.RiskOverrideReason?.Trim();
        item.RecommendationSummary = model.RecommendationSummary.Trim();
        item.RecommendationRationale = model.RecommendationRationale.Trim();
        item.CostsAndFees = model.CostsAndFees.Trim();
        item.TaxConsequences = model.TaxConsequences.Trim();
        item.LiquidityAndRestrictions = model.LiquidityAndRestrictions.Trim();
        item.MaterialRisks = model.MaterialRisks.Trim();
        item.IsReplacement = model.IsReplacement;
        item.ReplacementConsequences = model.ReplacementConsequences.Trim();
        item.ClientDeparture = model.ClientDeparture.Trim();
        item.WarningsGiven = model.WarningsGiven.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        item.RiskResponses.Clear();
        foreach (var response in model.RiskResponses.Where(value => !string.IsNullOrWhiteSpace(value.AnswerCode)))
        {
            var option = FindOption(item.RiskMethodologyCode, response.QuestionCode, response.AnswerCode);
            item.RiskResponses.Add(new ClientAdviceRiskResponse
            {
                QuestionCode = response.QuestionCode,
                AnswerCode = response.AnswerCode,
                Score = option.Score,
                Explanation = response.Explanation?.Trim()
            });
        }
        var methodology = Methodology(item.RiskMethodologyCode);
        item.CalculatedRiskScore = item.RiskResponses.Count == methodology.Questions.Count ? item.RiskResponses.Sum(value => value.Score) : null;
        item.CalculatedRiskLevel = item.CalculatedRiskScore.HasValue ? RiskLevel(item.RiskMethodologyCode, item.CalculatedRiskScore.Value) : null;
        item.FinalRiskLevel = string.IsNullOrWhiteSpace(item.RiskOverrideReason) ? item.CalculatedRiskLevel : item.FinalRiskLevel;

        item.Products.Clear();
        foreach (var product in model.Products.Where(value => !string.IsNullOrWhiteSpace(value.ProductName)))
        {
            item.Products.Add(product.ToEntity());
        }
        item.FactSources.Clear();
        foreach (var source in model.FactSources.Where(value => !string.IsNullOrWhiteSpace(value.FactName) || !string.IsNullOrWhiteSpace(value.DocumentPath)))
            item.FactSources.Add(source.ToEntity());
        item.InvestmentLinks.Clear();
        foreach (var investment in model.AvailableInvestments.Where(value => value.Selected))
            item.InvestmentLinks.Add(new ClientAdviceInvestmentLink { ClientInvestmentAccountId = investment.InvestmentAccountId });
        InvalidateApproval(item);
        await db.SaveChangesAsync();
        Audit(item.Id, "AdviceDraftUpdated", User(userName), new { item.CalculatedRiskScore, item.CalculatedRiskLevel });
        await db.SaveChangesAsync();
    }

    public async Task SubmitForReviewAsync(int caseId, string? userName)
    {
        var item = await QueryCase().SingleAsync(value => value.Id == caseId);
        EnsureEditable(item);
        var errors = Validate(item);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        item.Status = ClientAdviceStatuses.ReadyForReview;
        item.SubmittedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;
        Audit(item.Id, "AdviceSubmittedForReview", User(userName), new { item.CalculatedRiskScore, item.FinalRiskLevel });
        await db.SaveChangesAsync();
    }

    public async Task ApproveAsync(int caseId, string? reviewer, string reason)
    {
        var item = await QueryCase().SingleAsync(value => value.Id == caseId);
        if (item.Status != ClientAdviceStatuses.ReadyForReview) throw new InvalidOperationException("Only a case ready for review can be approved.");
        var user = User(reviewer);
        if (string.Equals(item.PreparedBy, user, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The preparer cannot approve their own advice case.");
        if (item.ReviewFindings.Any(finding => finding.Status == ClientAdviceFindingStatuses.Open))
            throw new InvalidOperationException("Resolve or accept every review finding before approval.");
        var errors = Validate(item);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        var snapshot = BuildSnapshot(item);
        item.FrozenSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        item.FrozenSnapshotSha256 = Sha256(Encoding.UTF8.GetBytes(item.FrozenSnapshotJson));
        item.Status = ClientAdviceStatuses.ApprovedForIssue;
        item.ApprovedAtUtc = DateTime.UtcNow;
        item.Approvals.Add(new ClientAdviceApproval { Reviewer = user, Reason = reason.Trim() });
        if (item.PreviousAdviceCaseId.HasValue)
        {
            var previous = await db.ClientAdviceCases.SingleAsync(value => value.Id == item.PreviousAdviceCaseId.Value);
            previous.Status = ClientAdviceStatuses.Superseded;
            previous.UpdatedAtUtc = DateTime.UtcNow;
        }
        var pdf = BuildPdf(item, false, null);
        var generated = item.Documents.SingleOrDefault(value => value.DocumentType == ClientAdviceDocumentTypes.GeneratedAdviceRecord);
        if (generated is null)
        {
            generated = new ClientAdviceDocument
            {
                ClientId = item.ClientId,
                DocumentType = ClientAdviceDocumentTypes.GeneratedAdviceRecord
            };
            item.Documents.Add(generated);
        }
        generated.FileName = $"KCAS-advice-record-{item.Id}-r{item.Revision}.pdf";
        generated.FileSha256 = Sha256(pdf);
        generated.FileSizeBytes = pdf.LongLength;
        generated.RecordedBy = user;
        generated.RecordedAtUtc = DateTime.UtcNow;
        Audit(item.Id, "AdviceApprovedForIssue", user, new { item.FrozenSnapshotSha256, GeneratedPdfSha256 = generated.FileSha256 });
        await db.SaveChangesAsync();
    }

    public async Task<int> AddKanaanFamilyParticipantsAsync(int caseId, string? userName)
    {
        var item = await QueryCase().SingleAsync(value => value.Id == caseId);
        EnsureEditable(item);
        if (string.IsNullOrWhiteSpace(item.Client.KanaanId))
            throw new InvalidOperationException("The client has no Kanaan ID family to include.");
        var clientIds = await db.Clients.AsNoTracking()
            .Where(value => value.KanaanId == item.Client.KanaanId)
            .Select(value => value.Id).ToListAsync();
        var existing = item.Participants.Select(value => value.ClientId).ToHashSet();
        foreach (var clientId in clientIds.Where(value => !existing.Contains(value)))
            item.Participants.Add(new ClientAdviceParticipant { ClientId = clientId });
        var added = item.Participants.Count(value => !existing.Contains(value.ClientId));
        Audit(item.Id, "AdviceFamilyParticipantsAdded", User(userName), new { item.Client.KanaanId, Added = added });
        await db.SaveChangesAsync();
        return added;
    }

    public async Task<int> CreateRevisionAsync(int caseId, string? userName)
    {
        var source = await QueryCase().AsNoTracking().SingleAsync(value => value.Id == caseId);
        if (source.Status is not (ClientAdviceStatuses.ApprovedForIssue or ClientAdviceStatuses.Issued or ClientAdviceStatuses.Complete))
            throw new InvalidOperationException("Only approved or completed advice can be revised.");
        if (await db.ClientAdviceCases.AnyAsync(value => value.PreviousAdviceCaseId == source.Id && value.Status != ClientAdviceStatuses.Cancelled))
            throw new InvalidOperationException("A revision of this advice record already exists.");
        var user = User(userName);
        var revision = new ClientAdviceCase
        {
            ClientId = source.ClientId, PreviousAdviceCaseId = source.Id, AdviceType = source.AdviceType, RiskMethodologyCode = source.RiskMethodologyCode,
            Revision = source.Revision + 1, AdviceDate = DateOnly.FromDateTime(DateTime.Today), AdviserName = user, PreparedBy = user,
            AdviceScope = source.AdviceScope, MeetingSummary = source.MeetingSummary, NeedsAndObjectives = source.NeedsAndObjectives,
            FinancialSituation = source.FinancialSituation, AdviceLimitations = source.AdviceLimitations,
            ProductKnowledgeSummary = source.ProductKnowledgeSummary, InvestmentAmount = source.InvestmentAmount,
            InvestmentPortfolioPercent = source.InvestmentPortfolioPercent, DomesticPreferencePercent = source.DomesticPreferencePercent,
            OffshorePreferencePercent = source.OffshorePreferencePercent, RecommendationSummary = source.RecommendationSummary,
            RecommendationRationale = source.RecommendationRationale, CostsAndFees = source.CostsAndFees,
            TaxConsequences = source.TaxConsequences, LiquidityAndRestrictions = source.LiquidityAndRestrictions,
            MaterialRisks = source.MaterialRisks, IsReplacement = source.IsReplacement,
            ReplacementConsequences = source.ReplacementConsequences, ClientDeparture = source.ClientDeparture,
            WarningsGiven = source.WarningsGiven
        };
        foreach (var value in source.Participants)
            revision.Participants.Add(new ClientAdviceParticipant { ClientId = value.ClientId, Role = value.Role });
        foreach (var value in source.RiskResponses)
            revision.RiskResponses.Add(new ClientAdviceRiskResponse { QuestionCode = value.QuestionCode, AnswerCode = value.AnswerCode, Score = value.Score, Explanation = value.Explanation });
        foreach (var value in source.Products)
            revision.Products.Add(new ClientAdviceProduct { ProductName = value.ProductName, Provider = value.Provider, ProductType = value.ProductType, IsRecommended = value.IsRecommended, Amount = value.Amount, AllocationPercent = value.AllocationPercent, Motivation = value.Motivation, SupportingDocumentPath = value.SupportingDocumentPath });
        foreach (var value in source.FactSources)
            revision.FactSources.Add(new ClientAdviceFactSource { FactName = value.FactName, SourceDate = value.SourceDate, DocumentPath = value.DocumentPath, Notes = value.Notes });
        foreach (var value in source.InvestmentLinks)
            revision.InvestmentLinks.Add(new ClientAdviceInvestmentLink { ClientInvestmentAccountId = value.ClientInvestmentAccountId, Role = value.Role });
        db.ClientAdviceCases.Add(revision);
        await db.SaveChangesAsync();
        Audit(revision.Id, "AdviceRevisionCreated", user, new { PreviousAdviceCaseId = source.Id, revision.Revision });
        await db.SaveChangesAsync();
        return revision.Id;
    }

    public async Task ReturnAsync(int caseId, string? reviewer, string reason)
    {
        var item = await db.ClientAdviceCases.SingleAsync(value => value.Id == caseId);
        if (item.Status != ClientAdviceStatuses.ReadyForReview) throw new InvalidOperationException("Only a case ready for review can be returned.");
        item.Status = ClientAdviceStatuses.Returned;
        item.UpdatedAtUtc = DateTime.UtcNow;
        Audit(item.Id, "AdviceReturned", User(reviewer), new { reason });
        await db.SaveChangesAsync();
    }

    public async Task MarkIssuedAsync(int caseId, string? userName)
    {
        var item = await db.ClientAdviceCases.SingleAsync(value => value.Id == caseId);
        if (item.Status != ClientAdviceStatuses.ApprovedForIssue) throw new InvalidOperationException("Approve the case before issuing it.");
        item.Status = ClientAdviceStatuses.Issued;
        item.IssuedAtUtc = DateTime.UtcNow;
        Audit(item.Id, "AdviceIssued", User(userName), new { item.FrozenSnapshotSha256 });
        await db.SaveChangesAsync();
    }

    public async Task RecordSignedDocumentAsync(int caseId, string path, string? userName)
    {
        var item = await db.ClientAdviceCases.Include(value => value.Documents).SingleAsync(value => value.Id == caseId);
        if (item.Status != ClientAdviceStatuses.Issued) throw new InvalidOperationException("Mark the approved record as issued before recording the signed copy.");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidOperationException("The signed PDF could not be found.");
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The signed advice record must be a PDF.");
        var info = new FileInfo(path);
        var bytes = await File.ReadAllBytesAsync(path);
        item.Documents.Add(new ClientAdviceDocument
        {
            ClientId = item.ClientId,
            DocumentType = ClientAdviceDocumentTypes.SignedAdviceRecord,
            FileName = info.Name,
            SourcePath = info.FullName,
            FileSha256 = Sha256(bytes),
            FileSizeBytes = info.Length,
            FileLastWriteTimeUtc = info.LastWriteTimeUtc,
            RecordedBy = User(userName)
        });
        item.Status = ClientAdviceStatuses.Complete;
        item.CompletedAtUtc = DateTime.UtcNow;
        Audit(item.Id, "SignedAdviceRecorded", User(userName), new { info.Name, Hash = Sha256(bytes) });
        await db.SaveChangesAsync();
    }

    public async Task AddFindingAsync(int caseId, ClientAdviceFindingEditModel model, string? userName)
    {
        var item = await db.ClientAdviceCases.SingleAsync(value => value.Id == caseId);
        if (item.Status is ClientAdviceStatuses.ApprovedForIssue or ClientAdviceStatuses.Issued or ClientAdviceStatuses.Complete)
            throw new InvalidOperationException("Approved advice cannot be changed.");
        db.ClientAdviceReviewFindings.Add(new ClientAdviceReviewFinding
        {
            ClientAdviceCaseId = caseId,
            Severity = model.Severity,
            Category = model.Category.Trim(),
            AffectedField = model.AffectedField?.Trim(),
            Finding = model.Finding.Trim(),
            EvidenceReference = model.EvidenceReference?.Trim(),
            RecommendedCorrection = model.RecommendedCorrection?.Trim(),
            PerformedBy = User(userName)
        });
        await db.SaveChangesAsync();
    }

    public async Task<int> ImportFindingsAsync(int caseId, byte[] json, string? userName)
    {
        ClientAdviceReviewImportModel import;
        try
        {
            import = JsonSerializer.Deserialize<ClientAdviceReviewImportModel>(json, JsonOptions)
                ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The review findings file is not valid JSON: {ex.Message}");
        }
        if (import.Findings.Count == 0) throw new InvalidOperationException("The review file contains no findings.");
        foreach (var finding in import.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Category) || string.IsNullOrWhiteSpace(finding.Finding))
                throw new InvalidOperationException("Every imported finding requires a category and finding description.");
            if (!ClientAdviceFindingSeverities.All.Contains(finding.Severity))
                throw new InvalidOperationException($"Finding severity '{finding.Severity}' is invalid.");
            await AddFindingAsync(caseId, finding, userName);
        }
        Audit(caseId, "AdviceReviewFindingsImported", User(userName), new { Count = import.Findings.Count });
        await db.SaveChangesAsync();
        return import.Findings.Count;
    }

    public async Task<byte[]> ExportReviewManifestAsync(int caseId)
    {
        var item = await QueryCase().AsNoTracking().SingleAsync(value => value.Id == caseId);
        var participantIds = item.Participants.Select(value => value.ClientId).Distinct().ToList();
        var evidence = await db.ClientEvidenceItems.AsNoTracking()
            .Where(value => participantIds.Contains(value.ClientId))
            .OrderBy(value => value.ClientId).ThenBy(value => value.EvidenceType).ThenBy(value => value.FileName)
            .Select(value => new { value.ClientId, value.EvidenceType, value.Title, value.SourcePath, value.RelativePath,
                value.FileName, value.FileSha256, value.FileLastWriteTimeUtc, value.VerifiedDate, value.Status, value.Notes })
            .ToListAsync();
        var investments = await db.ClientInvestmentAccounts.AsNoTracking()
            .Where(value => participantIds.Contains(value.ClientId))
            .OrderBy(value => value.ClientId).ThenBy(value => value.AccountNumber)
            .Select(value => new { value.ClientId, value.AccountNumber, value.Administrator, value.ProductName,
                value.ProductType, value.FundName, value.InvestmentDate, value.SurrenderDate })
            .ToListAsync();
        var manifest = new
        {
            Schema = "kcas-client-advice-review-v1",
            Purpose = "Review the structured Risk Analyser and Client Advice Record against the listed evidence. Do not infer unsupported facts.",
            AdviceCase = BuildSnapshot(item),
            Participants = item.Participants.OrderBy(value => value.ClientId).Select(value => new
            {
                value.ClientId, ClientName = value.Client.DisplayName, value.Client.KanaanId, value.Role
            }),
            Evidence = evidence,
            Investments = investments,
            Findings = Array.Empty<ClientAdviceFindingEditModel>()
        };
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
    }

    public async Task<int> IndexHistoricalDocumentsAsync(int clientId, string? userName)
    {
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(value => value.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        var candidates = await db.ClientEvidenceScanFiles.AsNoTracking()
            .Where(value => value.ClientId == clientId && value.MatchStatus == ClientEvidenceScanFileStatuses.Linked)
            .OrderByDescending(value => value.FileLastWriteTimeUtc).ToListAsync();
        var existing = await db.ClientAdviceDocuments
            .Where(value => value.ClientId == clientId && value.ClientAdviceCaseId == null)
            .Select(value => new { value.DocumentType, value.FileSha256, value.SourcePath }).ToListAsync();
        var known = existing.Select(value => $"{value.DocumentType}|{value.FileSha256}|{value.SourcePath}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var candidate in candidates)
        {
            var type = HistoricalDocumentType(candidate.FileName, candidate.RelativePath);
            if (type is null) continue;
            if (known.Any(value => value.StartsWith($"{type}|{candidate.FileSha256}|", StringComparison.OrdinalIgnoreCase) ||
                                   value.EndsWith($"|{candidate.FullPath}", StringComparison.OrdinalIgnoreCase))) continue;
            db.ClientAdviceDocuments.Add(new ClientAdviceDocument
            {
                ClientId = clientId,
                DocumentType = type,
                FileName = candidate.FileName,
                SourcePath = candidate.FullPath,
                FileSha256 = candidate.FileSha256,
                FileSizeBytes = candidate.FileSizeBytes,
                FileLastWriteTimeUtc = candidate.FileLastWriteTimeUtc,
                RecordedBy = User(userName)
            });
            known.Add($"{type}|{candidate.FileSha256}|{candidate.FullPath}");
            added++;
        }

        var activeRoot = await db.ClientEvidenceScanRoots.AsNoTracking().Where(value => value.IsActive)
            .OrderByDescending(value => value.Id).Select(value => value.RootPath).FirstOrDefaultAsync();
        var mappedFolder = string.IsNullOrWhiteSpace(activeRoot)
            ? null
            : ClientReviewTransferService.MapClientFolderToLiveRoot(client.ClientFolder, activeRoot);
        var folder = new[] { client.ClientFolder, mappedFolder }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && Directory.Exists(value));
        if (folder is not null)
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                         .Where(path => HistoricalAdviceExtensions.Contains(Path.GetExtension(path))))
            {
                var relativePath = Path.GetRelativePath(folder, path);
                var type = HistoricalDocumentType(Path.GetFileName(path), relativePath);
                if (type is null) continue;
                var info = new FileInfo(path);
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
                if (known.Any(value => value.StartsWith($"{type}|{hash}|", StringComparison.OrdinalIgnoreCase) ||
                                       value.EndsWith($"|{info.FullName}", StringComparison.OrdinalIgnoreCase))) continue;
                db.ClientAdviceDocuments.Add(new ClientAdviceDocument
                {
                    ClientId = clientId,
                    DocumentType = type,
                    FileName = info.Name,
                    SourcePath = info.FullName,
                    FileSha256 = hash,
                    FileSizeBytes = info.Length,
                    FileLastWriteTimeUtc = info.LastWriteTimeUtc,
                    RecordedBy = User(userName)
                });
                known.Add($"{type}|{hash}|{info.FullName}");
                added++;
            }
        }
        Audit(clientId, "HistoricalAdviceDocumentsIndexed", User(userName), new { ClientId = clientId, Added = added });
        await db.SaveChangesAsync();
        return added;
    }

    public async Task ResolveFindingAsync(int findingId, bool accept, string resolution, string? userName)
    {
        var finding = await db.ClientAdviceReviewFindings.Include(item => item.AdviceCase).SingleAsync(item => item.Id == findingId);
        if (finding.AdviceCase.Status is ClientAdviceStatuses.ApprovedForIssue or ClientAdviceStatuses.Issued or ClientAdviceStatuses.Complete)
            throw new InvalidOperationException("Approved advice cannot be changed.");
        finding.Status = accept ? ClientAdviceFindingStatuses.Accepted : ClientAdviceFindingStatuses.Resolved;
        finding.Resolution = resolution.Trim();
        finding.ResolvedBy = User(userName);
        finding.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<byte[]> ExportPdfAsync(int caseId)
    {
        var item = await QueryCase().AsNoTracking().SingleAsync(value => value.Id == caseId);
        if (item.Status is not (ClientAdviceStatuses.ApprovedForIssue or ClientAdviceStatuses.Issued or ClientAdviceStatuses.Complete or ClientAdviceStatuses.Superseded))
            throw new InvalidOperationException("Approve the advice case before generating the final record.");
        return BuildPdf(item, false, null);
    }

    public async Task<byte[]> ExportDraftPreviewPdfAsync(int caseId, string? userName)
    {
        var item = await QueryCase().SingleAsync(value => value.Id == caseId);
        if (item.Status is not (ClientAdviceStatuses.Draft or ClientAdviceStatuses.Returned or ClientAdviceStatuses.ReadyForReview))
            throw new InvalidOperationException("Draft preview is available only before advice approval.");
        var user = User(userName);
        var pdf = BuildPdf(item, true, user);
        Audit(item.Id, "AdviceDraftPreviewExported", user, new
        {
            OpenFindings = item.ReviewFindings.Count(value => value.Status == ClientAdviceFindingStatuses.Open),
            ValidationBlockers = Validate(item).Count,
            Sha256 = Sha256(pdf)
        });
        await db.SaveChangesAsync();
        return pdf;
    }

    private static byte[] BuildPdf(ClientAdviceCase item, bool draftPreview, string? previewedBy)
    {
        var methodology = Methodology(item.RiskMethodologyCode);
        var writer = new InvestmentSummaryService.SimplePdfWriter(
            draftPreview ? "DRAFT - KCAS Investment Risk Analyser and Client Advice Record" : "KCAS Investment Risk Analyser and Client Advice Record",
            item.Client.DisplayName,
            draftPreview ? "DRAFT - NOT FOR CLIENT ISSUE" : null);
        writer.WriteReportHeader("Investment Risk Analyser and Client Advice Record", item.Client.DisplayName,
        [
            ("Advice date", item.AdviceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Adviser", item.AdviserName),
            ("Revision", item.Revision.ToString(CultureInfo.InvariantCulture)),
            ("Record", draftPreview ? "DRAFT PREVIEW - NOT APPROVED" : "Approved advice")
        ]);
        writer.WriteSection("Advice subjects");
        foreach (var participant in item.Participants.OrderBy(value => value.Client.DisplayName))
            writer.WriteParagraph($"{participant.Client.DisplayName} (Kanaan ID {participant.Client.KanaanId ?? "-"}) - {participant.Role}");
        writer.WriteSection("Material fact sources");
        writer.WriteTable(["Fact", "Source date", "Document", "Notes"], [150, 85, 280, 215], item.FactSources.Select(value => new[]
        {
            value.FactName, value.SourceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-", value.DocumentPath, value.Notes ?? "-"
        }));
        if (item.InvestmentLinks.Count > 0)
        {
            writer.WriteSection("Existing investments considered");
            writer.WriteTable(["Client", "Account", "Product", "Administrator", "Status"], [170, 130, 180, 150, 90], item.InvestmentLinks.Select(value => new[]
            {
                value.InvestmentAccount.Client.DisplayName, value.InvestmentAccount.AccountNumber ?? "-", value.InvestmentAccount.ProductName ?? "-",
                value.InvestmentAccount.Administrator ?? "-", value.InvestmentAccount.SurrenderDate.HasValue ? "Historical" : "Current"
            }));
        }
        Section(writer, "Advice scope and client objectives", item.AdviceScope, item.MeetingSummary, item.NeedsAndObjectives);
        Section(writer, "Financial situation and limitations", item.FinancialSituation, item.AdviceLimitations, item.ProductKnowledgeSummary);
        writer.WriteSection("Investment Risk Analyser");
        writer.WriteParagraph($"Methodology: {methodology.Name}. Bands: {methodology.BandDescription}.");
        writer.WriteTable(["Factor", "Answer", "Score"], [260, 400, 70], item.RiskResponses.OrderBy(value => QuestionIndex(item.RiskMethodologyCode, value.QuestionCode)).Select(value => new[]
        {
            methodology.Questions.Single(question => question.Code == value.QuestionCode).Text,
            FindOption(item.RiskMethodologyCode, value.QuestionCode, value.AnswerCode).Label,
            value.Score.ToString(CultureInfo.InvariantCulture)
        }));
        writer.WriteNote($"Calculated score: {item.CalculatedRiskScore}. Calculated profile: {item.CalculatedRiskLevel}. Final profile: {item.FinalRiskLevel}." +
                         (string.IsNullOrWhiteSpace(item.RiskOverrideReason) ? "" : $" Override reason: {item.RiskOverrideReason}"));
        writer.WriteSection("Products considered and recommendation");
        writer.WriteTable(["Product", "Provider", "Type", "Recommended", "Allocation", "Motivation"], [180, 120, 100, 75, 75, 180],
            item.Products.Select(value => new[] { value.ProductName, value.Provider ?? "-", value.ProductType ?? "-", value.IsRecommended ? "Yes" : "No", value.AllocationPercent.HasValue ? $"{value.AllocationPercent:0.##}%" : "-", value.Motivation ?? "-" }));
        Section(writer, "Recommendation and suitability", item.RecommendationSummary, item.RecommendationRationale);
        Section(writer, "Costs, tax, access and risks", item.CostsAndFees, item.TaxConsequences, item.LiquidityAndRestrictions, item.MaterialRisks);
        if (item.IsReplacement) Section(writer, "Replacement advice", item.ReplacementConsequences);
        writer.EnsureBlockSpace(145);
        Section(writer, "Client decision", item.ClientDeparture, item.WarningsGiven);
        writer.EnsureBlockSpace(82);
        writer.WriteSection("Approval and acceptance");
        writer.WriteParagraph($"Prepared by: {item.PreparedBy}. Reviewed by: {item.Approvals.OrderByDescending(value => value.DecidedAtUtc).FirstOrDefault()?.Reviewer ?? "-"}.");
        writer.WriteParagraph("Client signature: ______________________________    Date: __________________");
        writer.WriteParagraph("Financial adviser signature: ____________________    Date: __________________");
        if (draftPreview) WriteDraftReviewReport(writer, item, previewedBy!);
        return writer.Build();
    }

    private static void WriteDraftReviewReport(InvestmentSummaryService.SimplePdfWriter writer, ClientAdviceCase item, string previewedBy)
    {
        var openFindings = item.ReviewFindings.Where(value => value.Status == ClientAdviceFindingStatuses.Open)
            .OrderBy(value => FindingSeverityOrder(value.Severity)).ThenBy(value => value.Category).ToList();
        var validationBlockers = Validate(item);
        writer.StartNewPage();
        writer.WriteReportHeader("Internal draft review report", item.Client.DisplayName,
        [
            ("Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Generated by", previewedBy),
            ("Case status", item.Status),
            ("Open blockers", (openFindings.Count + validationBlockers.Count).ToString(CultureInfo.InvariantCulture))
        ]);
        writer.WriteNote("INTERNAL REVIEW MATERIAL - This report and the blocker appendix are not part of the client-facing advice record and must not be sent to the client.");
        writer.WriteSection("Review conclusion");
        writer.WriteParagraph(openFindings.Count + validationBlockers.Count == 0
            ? "No recorded content blocker remains. Independent review and approval are still required before the final client PDF may be generated or issued."
            : $"The proposed Risk Analyser and Client Advice Record is not ready for issue. {openFindings.Count} open review finding(s) and {validationBlockers.Count} record-completeness blocker(s) require attention.");

        writer.WriteSection("Issue report");
        if (openFindings.Count == 0 && validationBlockers.Count == 0)
            writer.WriteParagraph("No unresolved issue is recorded.");
        foreach (var finding in openFindings)
        {
            writer.WriteSection($"{finding.Severity}: {finding.Category}");
            writer.WriteParagraph($"Issue: {finding.Finding}");
            writer.WriteParagraph($"Why it matters: {FindingImpact(finding)}");
            writer.WriteParagraph($"Evidence: {finding.EvidenceReference ?? "No evidence reference recorded."}");
            writer.WriteParagraph($"Required action: {finding.RecommendedCorrection ?? "Record and resolve the required correction before approval."}");
        }
        foreach (var blocker in validationBlockers)
        {
            writer.WriteSection("Record completeness");
            writer.WriteParagraph($"Issue: {blocker}");
            writer.WriteParagraph("Why it matters: The controlled advice record cannot pass submission and approval validation while this required information is incomplete.");
            writer.WriteParagraph("Required action: Complete the identified field or record the required supporting information, then save and regenerate the preview.");
        }

        writer.WriteSection("Blocker appendix");
        var rows = openFindings.Select(value => new[]
            {
                value.Severity, value.Category, value.AffectedField ?? "-", value.RecommendedCorrection ?? "Resolve before approval"
            })
            .Concat(validationBlockers.Select(value => new[] { "Blocking", "Record completeness", "Required field", value }))
            .ToList();
        if (rows.Count == 0)
            writer.WriteParagraph("No content blocker recorded. Independent approval remains outstanding.");
        else
            writer.WriteTable(["Severity", "Category", "Affected field", "Action required"], [80, 150, 150, 350], rows);
    }

    private static string FindingImpact(ClientAdviceReviewFinding finding) => finding.Category switch
    {
        "Suitability" => "The adviser must demonstrate that the recommendation and portfolio are appropriate for the client's needs, financial capacity, withdrawals and selected risk profile.",
        "Document quality" => "An inaccurate or uncontrolled document could misstate the advice, confuse the client or create an unreliable compliance record.",
        "Financial position" => "Advice based on an incorrect financial position may be unsuitable or unsustainable.",
        "Income sustainability" => "An unsupported withdrawal level may deplete capital and impair the client's future income.",
        "Tax" => "Unsupported tax statements may mislead the client and fall outside the evidence available to the adviser.",
        "Methodology version" => "The score and profile cannot be relied upon unless the governing methodology is identified and applied consistently.",
        _ => $"This {finding.Severity.ToLowerInvariant()} review finding must be addressed so the advice basis is complete, supportable and independently reviewable."
    };

    private static int FindingSeverityOrder(string severity) => severity switch
    {
        ClientAdviceFindingSeverities.Critical => 0,
        ClientAdviceFindingSeverities.High => 1,
        ClientAdviceFindingSeverities.Medium => 2,
        _ => 3
    };

    public static string RiskLevel(int score) => score switch
    {
        <= 30 => "Very low",
        <= 48 => "Low",
        <= 66 => "Medium",
        <= 84 => "Medium to high",
        _ => "High"
    };

    public static string RiskLevel(string methodologyCode, int score) => methodologyCode switch
    {
        ClientAdviceMethodologies.KcasEvidenced => RiskLevel(score),
        ClientAdviceMethodologies.PreviousFormCorrectedBands => score switch
        {
            >= 11 and < 20 => "Very Conservative",
            >= 20 and < 40 => "Conservative",
            >= 40 and < 55 => "Moderate",
            >= 55 and < 75 => "Moderately Aggressive",
            >= 75 and <= 85 => "Aggressive",
            _ => throw new InvalidOperationException($"Score {score} is outside the previous-form methodology range of 11 to 85.")
        },
        _ => throw new InvalidOperationException($"Advice risk methodology '{methodologyCode}' is not supported.")
    };

    private static List<string> Validate(ClientAdviceCase item)
    {
        var errors = new List<string>();
        var methodology = Methodology(item.RiskMethodologyCode);
        Required(item.AdviserName, "Adviser", errors);
        Required(item.AdviceScope, "Advice scope", errors);
        Required(item.MeetingSummary, "Meeting summary", errors);
        Required(item.NeedsAndObjectives, "Needs and objectives", errors);
        Required(item.FinancialSituation, "Financial situation", errors);
        Required(item.ProductKnowledgeSummary, "Product knowledge", errors);
        if (item.FactSources.Count == 0) errors.Add("Record at least one source for the material client facts.");
        foreach (var source in item.FactSources)
        {
            Required(source.FactName, "Material fact name", errors);
            Required(source.DocumentPath, "Material fact source document", errors);
        }
        if ((item.AdviceType is ClientAdviceTypes.NewInvestment or ClientAdviceTypes.TopUp or
            ClientAdviceTypes.Replacement or ClientAdviceTypes.RetirementDecision) &&
            item.InvestmentAmount is null or <= 0)
            errors.Add("Enter the investment amount.");
        if (item.RiskResponses.Count != methodology.Questions.Count) errors.Add("Answer every Risk Analyser question.");
        if (item.CalculatedRiskScore.HasValue)
        {
            try { _ = RiskLevel(item.RiskMethodologyCode, item.CalculatedRiskScore.Value); }
            catch (InvalidOperationException) { errors.Add("The Risk Analyser score is invalid for the selected methodology."); }
        }
        Required(item.FinalRiskLevel, "Final risk profile", errors);
        if (!string.Equals(item.CalculatedRiskLevel, item.FinalRiskLevel, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(item.RiskOverrideReason))
            errors.Add("Explain the adviser risk-profile override.");
        if (item.DomesticPreferencePercent.HasValue || item.OffshorePreferencePercent.HasValue)
        {
            if ((item.DomesticPreferencePercent ?? 0) + (item.OffshorePreferencePercent ?? 0) != 100) errors.Add("Domestic and offshore preferences must total 100%.");
        }
        if (item.Products.Count == 0) errors.Add("Record at least one product considered.");
        if (!item.Products.Any(value => value.IsRecommended)) errors.Add("Record at least one recommended product.");
        var allocations = item.Products.Where(value => value.IsRecommended && value.AllocationPercent.HasValue).ToList();
        if (allocations.Count > 0 && allocations.Sum(value => value.AllocationPercent) != 100) errors.Add("Recommended product allocations must total 100%.");
        Required(item.RecommendationSummary, "Recommendation", errors);
        Required(item.RecommendationRationale, "Recommendation rationale", errors);
        Required(item.CostsAndFees, "Costs and fees", errors);
        Required(item.TaxConsequences, "Tax consequences", errors);
        Required(item.LiquidityAndRestrictions, "Liquidity and restrictions", errors);
        Required(item.MaterialRisks, "Material risks", errors);
        if (item.IsReplacement) Required(item.ReplacementConsequences, "Replacement consequences", errors);
        if (!string.IsNullOrWhiteSpace(item.ClientDeparture)) Required(item.WarningsGiven, "Warnings given", errors);
        return errors;
    }

    private IQueryable<ClientAdviceCase> QueryCase() => db.ClientAdviceCases
        .Include(item => item.Client)
        .Include(item => item.Participants).ThenInclude(item => item.Client)
        .Include(item => item.RiskResponses)
        .Include(item => item.Products)
        .Include(item => item.FactSources)
        .Include(item => item.InvestmentLinks).ThenInclude(item => item.InvestmentAccount).ThenInclude(item => item.Client)
        .Include(item => item.ReviewFindings)
        .Include(item => item.Approvals)
        .Include(item => item.Documents);

    private static void EnsureEditable(ClientAdviceCase item)
    {
        if (item.Status is not (ClientAdviceStatuses.Draft or ClientAdviceStatuses.Returned))
            throw new InvalidOperationException("Only draft or returned advice can be edited.");
    }

    private static void InvalidateApproval(ClientAdviceCase item)
    {
        item.FrozenSnapshotJson = null;
        item.FrozenSnapshotSha256 = null;
        item.ApprovedAtUtc = null;
        item.Approvals.Clear();
        if (item.Status == ClientAdviceStatuses.Returned) item.Status = ClientAdviceStatuses.Draft;
    }

    private static object BuildSnapshot(ClientAdviceCase item) => new
    {
        item.Id, item.ClientId, item.AdviceType, item.RiskMethodologyCode, item.Revision, item.AdviceDate, item.AdviserName,
        item.AdviceScope, item.MeetingSummary, item.NeedsAndObjectives, item.FinancialSituation,
        item.AdviceLimitations, item.ProductKnowledgeSummary, item.InvestmentAmount,
        item.InvestmentPortfolioPercent, item.DomesticPreferencePercent, item.OffshorePreferencePercent,
        item.CalculatedRiskScore, item.CalculatedRiskLevel, item.FinalRiskLevel, item.RiskOverrideReason,
        RiskResponses = item.RiskResponses.OrderBy(value => value.QuestionCode).Select(value => new { value.QuestionCode, value.AnswerCode, value.Score, value.Explanation }),
        FactSources = item.FactSources.OrderBy(value => value.FactName).Select(value => new { value.FactName, value.SourceDate, value.DocumentPath, value.Notes }),
        Investments = item.InvestmentLinks.OrderBy(value => value.ClientInvestmentAccountId).Select(value => new { value.ClientInvestmentAccountId, value.Role }),
        Products = item.Products.OrderBy(value => value.Id).Select(value => new { value.ProductName, value.Provider, value.ProductType, value.IsRecommended, value.Amount, value.AllocationPercent, value.Motivation, value.SupportingDocumentPath }),
        item.RecommendationSummary, item.RecommendationRationale, item.CostsAndFees, item.TaxConsequences,
        item.LiquidityAndRestrictions, item.MaterialRisks, item.IsReplacement, item.ReplacementConsequences,
        item.ClientDeparture, item.WarningsGiven
    };

    private static void Section(InvestmentSummaryService.SimplePdfWriter writer, string title, params string?[] values)
    {
        writer.WriteSection(title);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value))) writer.WriteParagraph(value!);
    }

    private static ClientAdviceRiskQuestion Question(string code, string text, string dimension, (string Label, int Score)[] options) =>
        new(code, text, dimension, options.Select(option => new ClientAdviceRiskOption(AnswerCode(option.Label), option.Label, option.Score)).ToList());
    private static string AnswerCode(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).Take(40).ToArray());
    private static ClientAdviceRiskOption FindOption(string methodologyCode, string questionCode, string answerCode) => Methodology(methodologyCode).Questions.Single(value => value.Code == questionCode).Options.Single(value => value.Code == answerCode);
    private static int QuestionIndex(string methodologyCode, string questionCode) => Methodology(methodologyCode).Questions.ToList().FindIndex(value => value.Code == questionCode);
    private static string? HistoricalDocumentType(string fileName, string relativePath)
    {
        var value = $"{fileName} {relativePath}";
        if (Regex.IsMatch(value, @"risk[ _-]*(analy[sz]er|profile|questionnaire)", RegexOptions.IgnoreCase))
            return ClientAdviceDocumentTypes.HistoricalRiskAnalyser;
        if (Regex.IsMatch(value, @"client[ _-]*advice[ _-]*record|record[ _-]*of[ _-]*advice|investment[ _-]*advice|(^|[\\/ _-])car([ ._\\/-]|$)", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(value, @"car[ _-]*(pic|photo|licen)", RegexOptions.IgnoreCase))
            return ClientAdviceDocumentTypes.HistoricalAdviceRecord;
        if (Regex.IsMatch(relativePath, @"(^|[\\/])FAIS Documents([\\/]|$)", RegexOptions.IgnoreCase))
            return ClientAdviceDocumentTypes.HistoricalAdviceRecord;
        return null;
    }
    private static readonly HashSet<string> HistoricalAdviceExtensions =
        new([".pdf", ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm"], StringComparer.OrdinalIgnoreCase);
    private static string User(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown user" : value.Trim();
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Required(string? value, string label, ICollection<string> errors) { if (string.IsNullOrWhiteSpace(value)) errors.Add($"Complete {label.ToLowerInvariant()}."); }
    private static string FinancialSummary(Client client) => string.Join(" ", new[]
    {
        client.FinancialProfile?.Occupation is null ? null : $"Occupation: {client.FinancialProfile.Occupation}.",
        client.FinancialProfile?.GrossMonthlySalary is null ? null : $"Gross monthly income: R {client.FinancialProfile.GrossMonthlySalary:0.00}.",
        client.FinancialProfile?.MonthlyExpenses is null ? null : $"Monthly expenses: R {client.FinancialProfile.MonthlyExpenses:0.00}.",
        client.FinancialProfile?.RetirementAge is null ? null : $"Planned retirement age: {client.FinancialProfile.RetirementAge}."
    }.Where(value => value is not null));

    private void Audit(int id, string action, string user, object value) => db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
    {
        EntityType = nameof(ClientAdviceCase), EntityId = id, Action = action, UserName = user,
        Reason = action, NewValueJson = JsonSerializer.Serialize(value, JsonOptions)
    });
}

public sealed record ClientAdviceRiskQuestion(string Code, string Text, string Dimension, IReadOnlyList<ClientAdviceRiskOption> Options);
public sealed record ClientAdviceRiskOption(string Code, string Label, int Score);
public sealed record ClientAdviceMethodology(string Code, string Name, string BandDescription, IReadOnlyList<ClientAdviceRiskQuestion> Questions);
public sealed record ClientAdviceRegisterItem(int Id, int ClientId, string ClientName, string? KanaanId, string AdviceType, string Status, DateOnly AdviceDate, string? CalculatedRiskLevel, string? FinalRiskLevel, string AdviserName, int Revision);
public sealed record ClientAdviceClientModel(int ClientId, string ClientName, string? KanaanId,
    IReadOnlyList<ClientAdviceRegisterItem> Cases, IReadOnlyList<ClientAdviceHistoricalDocument> HistoricalDocuments);
public sealed record ClientAdviceHistoricalDocument(int Id, string DocumentType, string FileName, string? SourcePath,
    string? FileSha256, long? FileSizeBytes, DateTime? FileLastWriteTimeUtc);

public sealed class ClientAdviceEditModel
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public string? KanaanId { get; set; }
    public string AdviceType { get; set; } = ClientAdviceTypes.NewInvestment;
    public string RiskMethodologyCode { get; set; } = ClientAdviceMethodologies.KcasEvidenced;
    public string Status { get; set; } = ClientAdviceStatuses.Draft;
    public int Revision { get; set; }
    public DateOnly AdviceDate { get; set; }
    public string AdviserName { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    public string AdviceScope { get; set; } = "";
    public string MeetingSummary { get; set; } = "";
    public string NeedsAndObjectives { get; set; } = "";
    public string FinancialSituation { get; set; } = "";
    public string AdviceLimitations { get; set; } = "";
    public string ProductKnowledgeSummary { get; set; } = "";
    public decimal? InvestmentAmount { get; set; }
    public decimal? InvestmentPortfolioPercent { get; set; }
    public decimal? DomesticPreferencePercent { get; set; }
    public decimal? OffshorePreferencePercent { get; set; }
    public int? CalculatedRiskScore { get; set; }
    public string? CalculatedRiskLevel { get; set; }
    public string? FinalRiskLevel { get; set; }
    public string? RiskOverrideReason { get; set; }
    public string RecommendationSummary { get; set; } = "";
    public string RecommendationRationale { get; set; } = "";
    public string CostsAndFees { get; set; } = "";
    public string TaxConsequences { get; set; } = "";
    public string LiquidityAndRestrictions { get; set; } = "";
    public string MaterialRisks { get; set; } = "";
    public bool IsReplacement { get; set; }
    public string ReplacementConsequences { get; set; } = "";
    public string ClientDeparture { get; set; } = "";
    public string WarningsGiven { get; set; } = "";
    public List<ClientAdviceRiskResponseEditModel> RiskResponses { get; set; } = [];
    public List<ClientAdviceParticipantModel> Participants { get; set; } = [];
    public List<ClientAdviceFactSourceEditModel> FactSources { get; set; } = [];
    public List<ClientAdviceInvestmentOption> AvailableInvestments { get; set; } = [];
    public List<ClientAdviceProductEditModel> Products { get; set; } = [];
    public List<ClientAdviceReviewFinding> Findings { get; set; } = [];
    public List<ClientAdviceApproval> Approvals { get; set; } = [];
    public List<ClientAdviceDocument> Documents { get; set; } = [];

    public static ClientAdviceEditModel FromEntity(ClientAdviceCase item, IReadOnlyList<ClientAdviceRiskQuestion> questions) => new()
    {
        Id = item.Id, ClientId = item.ClientId, ClientName = item.Client.DisplayName, KanaanId = item.Client.KanaanId,
        AdviceType = item.AdviceType, RiskMethodologyCode = item.RiskMethodologyCode, Status = item.Status, Revision = item.Revision, AdviceDate = item.AdviceDate,
        AdviserName = item.AdviserName, PreparedBy = item.PreparedBy, AdviceScope = item.AdviceScope,
        MeetingSummary = item.MeetingSummary, NeedsAndObjectives = item.NeedsAndObjectives,
        FinancialSituation = item.FinancialSituation, AdviceLimitations = item.AdviceLimitations,
        ProductKnowledgeSummary = item.ProductKnowledgeSummary, InvestmentAmount = item.InvestmentAmount,
        InvestmentPortfolioPercent = item.InvestmentPortfolioPercent, DomesticPreferencePercent = item.DomesticPreferencePercent,
        OffshorePreferencePercent = item.OffshorePreferencePercent, CalculatedRiskScore = item.CalculatedRiskScore,
        CalculatedRiskLevel = item.CalculatedRiskLevel, FinalRiskLevel = item.FinalRiskLevel,
        RiskOverrideReason = item.RiskOverrideReason, RecommendationSummary = item.RecommendationSummary,
        RecommendationRationale = item.RecommendationRationale, CostsAndFees = item.CostsAndFees,
        TaxConsequences = item.TaxConsequences, LiquidityAndRestrictions = item.LiquidityAndRestrictions,
        MaterialRisks = item.MaterialRisks, IsReplacement = item.IsReplacement,
        ReplacementConsequences = item.ReplacementConsequences, ClientDeparture = item.ClientDeparture,
        WarningsGiven = item.WarningsGiven,
        Participants = item.Participants.OrderBy(value => value.Client.DisplayName)
            .Select(value => new ClientAdviceParticipantModel(value.ClientId, value.Client.DisplayName, value.Client.KanaanId, value.Role)).ToList(),
        FactSources = item.FactSources.OrderBy(value => value.FactName).Select(ClientAdviceFactSourceEditModel.FromEntity).ToList(),
        RiskResponses = questions.Select(question =>
        {
            var response = item.RiskResponses.SingleOrDefault(value => value.QuestionCode == question.Code);
            return new ClientAdviceRiskResponseEditModel { QuestionCode = question.Code, AnswerCode = response?.AnswerCode ?? "", Explanation = response?.Explanation };
        }).ToList(),
        Products = item.Products.Select(ClientAdviceProductEditModel.FromEntity).ToList(),
        Findings = item.ReviewFindings.OrderByDescending(value => value.PerformedAtUtc).ToList(),
        Approvals = item.Approvals.OrderByDescending(value => value.DecidedAtUtc).ToList(),
        Documents = item.Documents.OrderByDescending(value => value.RecordedAtUtc).ToList()
    };
}

public sealed class ClientAdviceRiskResponseEditModel { public string QuestionCode { get; set; } = ""; public string AnswerCode { get; set; } = ""; public string? Explanation { get; set; } }
public sealed class ClientAdviceProductEditModel
{
    public string ProductName { get; set; } = ""; public string? Provider { get; set; } public string? ProductType { get; set; }
    public bool IsRecommended { get; set; } public decimal? Amount { get; set; } public decimal? AllocationPercent { get; set; }
    public string? Motivation { get; set; } public string? SupportingDocumentPath { get; set; }
    public ClientAdviceProduct ToEntity() => new() { ProductName = ProductName.Trim(), Provider = Provider?.Trim(), ProductType = ProductType?.Trim(), IsRecommended = IsRecommended, Amount = Amount, AllocationPercent = AllocationPercent, Motivation = Motivation?.Trim(), SupportingDocumentPath = SupportingDocumentPath?.Trim() };
    public static ClientAdviceProductEditModel FromEntity(ClientAdviceProduct value) => new() { ProductName = value.ProductName, Provider = value.Provider, ProductType = value.ProductType, IsRecommended = value.IsRecommended, Amount = value.Amount, AllocationPercent = value.AllocationPercent, Motivation = value.Motivation, SupportingDocumentPath = value.SupportingDocumentPath };
}
public sealed class ClientAdviceFindingEditModel { public string Severity { get; set; } = ClientAdviceFindingSeverities.Medium; public string Category { get; set; } = ""; public string? AffectedField { get; set; } public string Finding { get; set; } = ""; public string? EvidenceReference { get; set; } public string? RecommendedCorrection { get; set; } }
public sealed record ClientAdviceParticipantModel(int ClientId, string ClientName, string? KanaanId, string Role);
public sealed class ClientAdviceFactSourceEditModel
{
    public string FactName { get; set; } = "";
    public DateOnly? SourceDate { get; set; }
    public string DocumentPath { get; set; } = "";
    public string? Notes { get; set; }
    public ClientAdviceFactSource ToEntity() => new() { FactName = FactName.Trim(), SourceDate = SourceDate, DocumentPath = DocumentPath.Trim(), Notes = Notes?.Trim() };
    public static ClientAdviceFactSourceEditModel FromEntity(ClientAdviceFactSource value) => new() { FactName = value.FactName, SourceDate = value.SourceDate, DocumentPath = value.DocumentPath, Notes = value.Notes };
}
public sealed class ClientAdviceInvestmentOption
{
    public ClientAdviceInvestmentOption(int investmentAccountId, string clientName, string? accountNumber,
        string? productName, string? administrator, DateOnly? surrenderDate, bool selected)
        => (InvestmentAccountId, ClientName, AccountNumber, ProductName, Administrator, SurrenderDate, Selected) =
            (investmentAccountId, clientName, accountNumber, productName, administrator, surrenderDate, selected);
    public int InvestmentAccountId { get; }
    public string ClientName { get; }
    public string? AccountNumber { get; }
    public string? ProductName { get; }
    public string? Administrator { get; }
    public DateOnly? SurrenderDate { get; }
    public bool Selected { get; set; }
}
public sealed class ClientAdviceReviewImportModel { public List<ClientAdviceFindingEditModel> Findings { get; set; } = []; }
