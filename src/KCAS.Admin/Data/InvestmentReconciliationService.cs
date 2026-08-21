using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentReconciliationService(ApplicationDbContext db)
{
    private static readonly string[] TransferTerms =
        ["transfer", "transferred", "switch administrator", "section 37", "section 14", "oorgedra"];
    private static readonly string[] SurrenderTerms =
        ["surrender", "repurchase", "redemption", "redeem", "afkoop", "opsegging"];

    public async Task<InvestmentReconciliationModel> LoadAsync(
        InvestmentReconciliationQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new InvestmentReconciliationQuery();
        var clients = await db.Clients
            .AsNoTracking()
            .Where(client => client.InvestmentAccounts.Any() || client.FundValuations.Any())
            .Include(client => client.InvestmentAccounts)
            .Include(client => client.FundValuations)
            .AsSplitQuery()
            .OrderBy(client => client.DisplayName)
            .ToListAsync(cancellationToken);

        var issues = clients.SelectMany(BuildIssues).ToList();
        IEnumerable<InvestmentReconciliationIssue> filtered = issues;

        if (!string.IsNullOrWhiteSpace(query.IssueType))
        {
            filtered = filtered.Where(issue => issue.IssueType == query.IssueType);
        }

        if (query.ClientId.HasValue)
        {
            filtered = filtered.Where(issue => issue.ClientId == query.ClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(issue =>
                Contains(issue.ClientDisplayName, search) ||
                Contains(issue.KanaanId, search) ||
                Contains(issue.AccountNumber, search) ||
                Contains(issue.FundName, search) ||
                Contains(issue.Administrator, search) ||
                Contains(issue.Details, search));
        }

        return new InvestmentReconciliationModel
        {
            Query = query,
            Issues = filtered
                .OrderByDescending(issue => issue.SeverityOrder)
                .ThenBy(issue => issue.ClientDisplayName)
                .ThenBy(issue => issue.AccountNumber)
                .ThenBy(issue => issue.FundName)
                .ToList(),
            ClientOptions = clients
                .Select(client => new InvestmentSummaryClientOption(
                    client.Id, client.KanaanId, client.DisplayName, client.LifecycleStatus))
                .ToList(),
            TotalIssueCount = issues.Count,
            DuplicateMatchCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.DuplicateAccountMatch),
            SurrenderConflictCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.CurrentValuationAfterSurrender),
            UnmatchedValuationCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.UnmatchedValuation),
            MissingCurrentValueCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.MissingCurrentValuation)
        };
    }

    public async Task<ClientInvestmentReconciliationPageModel> LoadClientReviewAsync(
        int clientId,
        CancellationToken cancellationToken = default)
    {
        var client = await db.Clients.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.InvestmentAccounts).ThenInclude(account => account.Transactions)
            .Include(item => item.InvestmentReconciliationReviews)
            .Include(item => item.FundValuations)
            .Include(item => item.EvidenceItems)
            .SingleOrDefaultAsync(item => item.Id == clientId, cancellationToken)
            ?? throw new KeyNotFoundException("Client not found.");

        var issues = BuildIssues(client);
        var duplicateGroups = client.InvestmentAccounts
            .Where(account => NormalizeAccount(account.AccountNumber) is not null)
            .GroupBy(account => NormalizeAccount(account.AccountNumber)!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.OrderBy(account => account.Id).ToList(), StringComparer.OrdinalIgnoreCase);

        var accountRows = client.InvestmentAccounts
            .OrderByDescending(account => account.InvestmentDate)
            .ThenBy(account => account.AccountNumber)
            .Select(account =>
            {
                var valuations = ClientInvestmentStatusClassifier.MatchingValuations(account, client.FundValuations);
                var status = ClientInvestmentStatusClassifier.Evaluate(account, client.FundValuations);
                var snapshot = CalculateSnapshot(account, valuations);
                var latestReview = client.InvestmentReconciliationReviews
                    .Where(review => review.ClientInvestmentAccountId == account.Id)
                    .OrderByDescending(review => review.ReviewedAtUtc)
                    .ThenByDescending(review => review.Id)
                    .FirstOrDefault();
                var currentReview = latestReview is not null &&
                    string.Equals(latestReview.SnapshotSha256, snapshot, StringComparison.OrdinalIgnoreCase)
                        ? latestReview
                        : null;
                var latestTransaction = account.Transactions
                    .Where(transaction => !transaction.IsDeleted)
                    .OrderByDescending(transaction => transaction.TransactionDate)
                    .ThenByDescending(transaction => transaction.Id)
                    .FirstOrDefault();
                duplicateGroups.TryGetValue(NormalizeAccount(account.AccountNumber) ?? "", out var duplicates);
                var evidence = FindEvidence(client.EvidenceItems, account, latestTransaction);
                var proposal = BuildProposal(account, status, valuations, latestTransaction, duplicates, evidence);

                return new ClientInvestmentReconciliationAccountModel
                {
                    AccountId = account.Id,
                    LegacyInvestmentAccountId = account.LegacyInvestmentAccountId,
                    AccountNumber = account.AccountNumber,
                    Administrator = account.Administrator,
                    ProductName = account.ProductName,
                    FundName = account.FundName,
                    InvestmentDate = account.InvestmentDate,
                    SurrenderDate = account.SurrenderDate,
                    IsCurrent = status.IsCurrent,
                    StatusReason = status.Reason,
                    CurrentValueZar = valuations.Where(value => value.AmountZar.HasValue).Sum(value => value.AmountZar),
                    CurrentValueForeign = valuations.Where(value => value.AmountForeign.HasValue).Sum(value => value.AmountForeign),
                    LatestValuationDate = valuations.Max(value => value.ValuationDate),
                    LatestTransactionDate = latestTransaction?.TransactionDate,
                    LatestTransactionDescription = latestTransaction?.Description,
                    IssueLabels = issues.Where(issue => issue.AccountIds.Contains(account.Id)).Select(issue => issue.IssueLabel).Distinct().ToList(),
                    Evidence = evidence,
                    ProposedOutcome = proposal.Outcome,
                    ProposedSurrenderDate = proposal.SurrenderDate,
                    ProposedRelatedAccountId = proposal.RelatedAccountId,
                    ProposalReason = proposal.Reason,
                    ProposalEvidenceReference = proposal.EvidenceReference,
                    ReviewId = currentReview?.Id,
                    ReviewOutcome = currentReview?.Outcome,
                    ReviewReason = currentReview?.Reason,
                    ReviewEvidenceReference = currentReview?.EvidenceReference,
                    ReviewedAtUtc = currentReview?.ReviewedAtUtc,
                    ReviewedBy = currentReview?.ReviewedBy,
                    IsVerified = currentReview is not null && currentReview.Outcome != ClientInvestmentReconciliationOutcomes.NeedsFollowUp,
                    NeedsFollowUp = currentReview?.Outcome == ClientInvestmentReconciliationOutcomes.NeedsFollowUp,
                    ReviewIsStale = latestReview is not null && currentReview is null
                };
            })
            .ToList();

        var linkedClients = await LoadLinkedClientsAsync(client, cancellationToken);
        var relatedAccountOptions = await LoadRelatedAccountOptionsAsync(client, cancellationToken);
        var unmatchedIssues = issues.Where(issue => issue.AccountIds.Count == 0).ToList();
        var requiresReview = accountRows.Count > 0 || client.FundValuations.Count > 0;
        var isComplete = !requiresReview ||
            (accountRows.All(row => row.IsVerified) && unmatchedIssues.Count == 0);

        return new ClientInvestmentReconciliationPageModel
        {
            ClientId = client.Id,
            DisplayName = client.DisplayName,
            KanaanId = client.KanaanId,
            ClientFolder = client.ClientFolder,
            RequiresReview = requiresReview,
            IsComplete = isComplete,
            VerifiedCount = accountRows.Count(row => row.IsVerified),
            NeedsFollowUpCount = accountRows.Count(row => row.NeedsFollowUp),
            Accounts = accountRows,
            UnmatchedIssues = unmatchedIssues,
            LinkedClients = linkedClients,
            RelatedAccountOptions = relatedAccountOptions
        };
    }

    public async Task<bool> IsClientReviewCompleteAsync(
        int clientId,
        CancellationToken cancellationToken = default) =>
        (await LoadClientReviewAsync(clientId, cancellationToken)).IsComplete;

    public async Task<int> ReviewAccountAsync(
        int clientId,
        int accountId,
        ClientInvestmentReconciliationReviewRequest request,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        var user = Require(userName, "A signed-in reviewer is required.");
        var reason = Require(request.Reason, "A verification reason is required.");
        var evidenceReference = Require(request.EvidenceReference, "An evidence or data reference is required.");
        var account = await db.ClientInvestmentAccounts
            .Include(item => item.Client)
            .Include(item => item.Transactions)
            .SingleOrDefaultAsync(item => item.Id == accountId && item.ClientId == clientId, cancellationToken)
            ?? throw new KeyNotFoundException("Investment account not found.");
        var valuations = await db.ClientFundValuations.AsNoTracking()
            .Where(item => item.ClientId == clientId)
            .ToListAsync(cancellationToken);
        var matchedValuations = ClientInvestmentStatusClassifier.MatchingValuations(account, valuations);

        ClientInvestmentAccount? relatedAccount = null;
        if (request.RelatedAccountId.HasValue)
        {
            if (request.RelatedAccountId.Value == account.Id)
            {
                throw new ValidationException("A related account must be a different investment account.");
            }
            relatedAccount = await db.ClientInvestmentAccounts
                .Include(item => item.Client)
                .SingleOrDefaultAsync(item => item.Id == request.RelatedAccountId.Value, cancellationToken)
                ?? throw new ValidationException("The related investment account was not found.");
            if (!CanRelateAccounts(account, relatedAccount))
            {
                throw new ValidationException("The related investment account must belong to this client or a linked household/shared-folder client.");
            }
        }

        var outcomeError = ValidateOutcome(
            request.Outcome,
            request.SurrenderDate,
            relatedAccount is not null,
            relatedAccount is not null && relatedAccount.ClientId != account.ClientId,
            matchedValuations);
        if (outcomeError is not null)
        {
            throw new ValidationException(outcomeError);
        }

        var oldValue = new
        {
            account.SurrenderDate,
            account.AccountNumber,
            account.Administrator
        };
        if (request.Outcome == ClientInvestmentReconciliationOutcomes.Current)
        {
            account.SurrenderDate = null;
        }
        else if ((request.Outcome is ClientInvestmentReconciliationOutcomes.HistoricalSurrendered or
                  ClientInvestmentReconciliationOutcomes.Transferred or
                  ClientInvestmentReconciliationOutcomes.DuplicateContinuation) &&
                 request.SurrenderDate.HasValue)
        {
            account.SurrenderDate = request.SurrenderDate;
        }
        account.UpdatedBy = user;
        await db.SaveChangesAsync(cancellationToken);

        // Calculate the persisted state, rather than the change-tracked instance. This keeps
        // the approval stable across database date/decimal normalization and later reloads.
        var persistedAccount = await db.ClientInvestmentAccounts.AsNoTracking()
            .Include(item => item.Transactions)
            .SingleAsync(item => item.Id == account.Id, cancellationToken);
        var persistedValuations = await db.ClientFundValuations.AsNoTracking()
            .Where(item => item.ClientId == clientId)
            .ToListAsync(cancellationToken);
        var persistedMatchedValuations = ClientInvestmentStatusClassifier.MatchingValuations(
            persistedAccount,
            persistedValuations);

        var review = new ClientInvestmentReconciliationReview
        {
            ClientId = clientId,
            ClientInvestmentAccountId = account.Id,
            Outcome = request.Outcome,
            RelatedClientInvestmentAccountId = relatedAccount?.Id,
            AppliedSurrenderDate = account.SurrenderDate,
            EvidenceReference = evidenceReference,
            Reason = reason,
            SnapshotSha256 = CalculateSnapshot(persistedAccount, persistedMatchedValuations),
            ReviewedBy = user,
            ReviewedAtUtc = DateTime.UtcNow
        };
        db.ClientInvestmentReconciliationReviews.Add(review);
        await db.SaveChangesAsync(cancellationToken);
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(ClientInvestmentAccount),
            EntityId = account.Id,
            Action = request.Outcome == ClientInvestmentReconciliationOutcomes.NeedsFollowUp
                ? "InvestmentReconciliationFollowUpRecorded"
                : "InvestmentReconciliationVerified",
            OldValueJson = JsonSerializer.Serialize(oldValue),
            NewValueJson = JsonSerializer.Serialize(new
            {
                review.Id,
                review.Outcome,
                review.AppliedSurrenderDate,
                review.RelatedClientInvestmentAccountId,
                review.EvidenceReference,
                review.SnapshotSha256
            }),
            UserName = user,
            Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);
        return review.Id;
    }

    public async Task<int> ReviewAccountsAsync(
        int clientId,
        IReadOnlyCollection<ClientInvestmentReconciliationBatchRequest> requests,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var request in requests)
            {
                await ReviewAccountAsync(
                    clientId,
                    request.AccountId,
                    request.Review,
                    userName,
                    cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return requests.Count;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    internal static IReadOnlyList<InvestmentReconciliationIssue> BuildIssues(Client client)
    {
        var issues = new List<InvestmentReconciliationIssue>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var valuationNumbers = client.FundValuations
            .Select(item => ClientInvestmentStatusClassifier.NormalizeAccountNumber(
                item.InvestmentUniqueNumber))
            .Where(item => item is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var duplicateGroup in client.InvestmentAccounts
            .Where(account => NormalizeAccount(account.AccountNumber) is not null)
            .GroupBy(account => NormalizeAccount(account.AccountNumber)!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && !valuationNumbers.Contains(group.Key)))
        {
            var accounts = duplicateGroup.OrderBy(account => account.Id).ToList();
            issues.Add(new InvestmentReconciliationIssue
            {
                IssueType = InvestmentReconciliationIssueTypes.DuplicateHistoricalAccount,
                IssueLabel = InvestmentReconciliationIssueTypes.Label(InvestmentReconciliationIssueTypes.DuplicateHistoricalAccount),
                ClientId = client.Id,
                KanaanId = client.KanaanId,
                ClientDisplayName = client.DisplayName,
                AccountNumber = accounts[0].AccountNumber,
                Administrator = string.Join(" / ", accounts.Select(account => account.Administrator).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                FundName = string.Join(" / ", accounts.Select(account => account.FundName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                Details = $"{accounts.Count} historical account records use the same account number.",
                RecommendedAction = "Verify whether these are duplicate or administrator-continuation records and link them without deleting source history.",
                AccountIds = accounts.Select(account => account.Id).ToList(),
                SeverityOrder = 2
            });
        }

        foreach (var valuation in client.FundValuations)
        {
            var candidates = InvestmentSummaryCalculator.MatchingAccounts(
                valuation, client.InvestmentAccounts);
            if (candidates.Count == 0)
            {
                issues.Add(FromValuation(
                    client,
                    valuation,
                    InvestmentReconciliationIssueTypes.UnmatchedValuation,
                    "No matching account",
                    "Create or correct an investment account so its account number and administrator match this valuation.",
                    [],
                    3));
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(FromValuation(
                    client,
                    valuation,
                    InvestmentReconciliationIssueTypes.DuplicateAccountMatch,
                    $"{candidates.Count} account records match this valuation",
                    "Review the matching account records and correct or remove duplicates. The valuation is counted once while this is unresolved.",
                    candidates.Select(account => account.Id).ToList(),
                    2));
            }

            if (candidates.All(account =>
                    account.SurrenderDate.HasValue && account.SurrenderDate.Value <= today))
            {
                issues.Add(FromValuation(
                    client,
                    valuation,
                    InvestmentReconciliationIssueTypes.CurrentValuationAfterSurrender,
                    "A current valuation is linked only to surrendered accounts",
                    "Confirm the valuation is current, then correct the account surrender date or account number. Do not delete the valuation merely to clear the warning.",
                    candidates.Select(account => account.Id).ToList(),
                    4));
            }
        }

        foreach (var account in client.InvestmentAccounts)
        {
            var accountNumber =
                ClientInvestmentStatusClassifier.NormalizeAccountNumber(account.AccountNumber);
            if (accountNumber is not null && valuationNumbers.Contains(accountNumber))
            {
                continue;
            }

            var isSurrendered =
                account.SurrenderDate.HasValue && account.SurrenderDate.Value <= today;
            if (!isSurrendered)
            {
                issues.Add(new InvestmentReconciliationIssue
                {
                    IssueType = InvestmentReconciliationIssueTypes.MissingCurrentValuation,
                    IssueLabel = "No current valuation",
                    ClientId = client.Id,
                    KanaanId = client.KanaanId,
                    ClientDisplayName = client.DisplayName,
                    AccountNumber = account.AccountNumber,
                    Administrator = account.Administrator,
                    FundName = account.FundName,
                    Details = "This account has no current valuation and no effective surrender date.",
                    RecommendedAction = "Confirm whether the investment remains current. Load/correct the valuation if current, or capture the effective surrender date if historical.",
                    AccountIds = [account.Id],
                    SeverityOrder = 1
                });
            }
        }

        return issues;
    }

    private static InvestmentReconciliationIssue FromValuation(
        Client client,
        ClientFundValuation valuation,
        string issueType,
        string details,
        string recommendedAction,
        IReadOnlyList<int> accountIds,
        int severityOrder) =>
        new()
        {
            IssueType = issueType,
            IssueLabel = InvestmentReconciliationIssueTypes.Label(issueType),
            ClientId = client.Id,
            KanaanId = client.KanaanId,
            ClientDisplayName = client.DisplayName,
            ValuationId = valuation.Id,
            LegacyFundId = valuation.LegacyFundId,
            AccountNumber = valuation.InvestmentUniqueNumber,
            Administrator = valuation.Administrator,
            FundName = valuation.FundName,
            ValuationDate = valuation.ValuationDate,
            AmountZar = valuation.AmountZar,
            Details = details,
            RecommendedAction = recommendedAction,
            AccountIds = accountIds,
            SeverityOrder = severityOrder
        };

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    internal static string CalculateSnapshot(
        ClientInvestmentAccount account,
        IEnumerable<ClientFundValuation> valuations)
    {
        var payload = JsonSerializer.Serialize(new
        {
            account.Id,
            account.InvestmentDate,
            account.SurrenderDate,
            account.Administrator,
            account.AccountNumber,
            account.ProductName,
            account.ProductType,
            account.FundName,
            Transactions = account.Transactions.Where(item => !item.IsDeleted)
                .OrderBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.TransactionDate,
                    item.Description,
                    item.InvestmentAmountZar,
                    item.WithdrawalAmountZar,
                    item.BalanceZar,
                    item.BalanceForeign
                }),
            Valuations = valuations.OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
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

    internal static string? ValidateOutcome(
        string outcome,
        DateOnly? surrenderDate,
        bool hasRelatedAccount,
        bool relatedAccountIsDifferentClient,
        IReadOnlyCollection<ClientFundValuation> matchedValuations)
    {
        if (!ClientInvestmentReconciliationOutcomes.All.Contains(outcome))
        {
            return "Select a valid reconciliation outcome.";
        }

        var hasCurrentValue = matchedValuations.Any(value =>
            value.AmountZar.HasValue || value.AmountForeign.HasValue);
        if (outcome == ClientInvestmentReconciliationOutcomes.Current && !hasCurrentValue)
        {
            return "A current account requires a matching current valuation.";
        }
        if (outcome is ClientInvestmentReconciliationOutcomes.HistoricalSurrendered or
            ClientInvestmentReconciliationOutcomes.Transferred)
        {
            if (surrenderDate is null)
            {
                return "A surrendered or transferred investment requires an effective date.";
            }
            if (hasCurrentValue)
            {
                return "A surrendered or transferred investment cannot be verified while a matching current valuation remains.";
            }
        }
        if (outcome == ClientInvestmentReconciliationOutcomes.DuplicateContinuation && !hasRelatedAccount)
        {
            return "A duplicate or continuation must identify the related account.";
        }
        if (outcome == ClientInvestmentReconciliationOutcomes.WrongClientDuplicate)
        {
            if (!hasRelatedAccount)
            {
                return "A wrong-client duplicate must identify the correct client's related account.";
            }
            if (!relatedAccountIsDifferentClient)
            {
                return "A wrong-client duplicate must link to an account on a different client.";
            }
        }
        return null;
    }

    private async Task<List<ClientInvestmentLinkedClientModel>> LoadLinkedClientsAsync(
        Client client,
        CancellationToken cancellationToken)
    {
        var normalizedFolder = string.IsNullOrWhiteSpace(client.ClientFolder) ? null : client.ClientFolder.Trim();
        return await db.Clients.AsNoTracking()
            .Where(item => item.Id != client.Id &&
                ((!string.IsNullOrWhiteSpace(client.KanaanId) && item.KanaanId == client.KanaanId) ||
                 (normalizedFolder != null && item.ClientFolder == normalizedFolder)))
            .OrderBy(item => item.DisplayName)
            .Select(item => new ClientInvestmentLinkedClientModel(
                item.Id,
                item.DisplayName,
                item.KanaanId,
                item.LifecycleStatus,
                item.InvestmentAccounts.Count,
                item.FundValuations.Where(value => value.AmountZar.HasValue).Sum(value => value.AmountZar) ?? 0))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<ClientInvestmentRelatedAccountOptionModel>> LoadRelatedAccountOptionsAsync(
        Client client,
        CancellationToken cancellationToken)
    {
        var normalizedFolder = string.IsNullOrWhiteSpace(client.ClientFolder) ? null : client.ClientFolder.Trim();
        return await db.ClientInvestmentAccounts.AsNoTracking()
            .Include(item => item.Client)
            .Where(account =>
                account.ClientId == client.Id ||
                (!string.IsNullOrWhiteSpace(client.KanaanId) && account.Client.KanaanId == client.KanaanId) ||
                (normalizedFolder != null && account.Client.ClientFolder == normalizedFolder))
            .OrderBy(account => account.Client.DisplayName)
            .ThenBy(account => account.AccountNumber)
            .ThenBy(account => account.Id)
            .Select(account => new ClientInvestmentRelatedAccountOptionModel(
                account.Id,
                account.ClientId,
                account.Client.DisplayName,
                account.Client.KanaanId,
                account.AccountNumber,
                account.Administrator,
                account.FundName))
            .ToListAsync(cancellationToken);
    }

    private static bool CanRelateAccounts(ClientInvestmentAccount account, ClientInvestmentAccount relatedAccount)
    {
        if (relatedAccount.ClientId == account.ClientId) return true;
        return (!string.IsNullOrWhiteSpace(account.Client.KanaanId) &&
                string.Equals(account.Client.KanaanId, relatedAccount.Client.KanaanId, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(account.Client.ClientFolder) &&
                string.Equals(account.Client.ClientFolder, relatedAccount.Client.ClientFolder, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ClientInvestmentEvidenceCandidateModel> FindEvidence(
        IEnumerable<ClientEvidenceItem> evidenceItems,
        ClientInvestmentAccount account,
        ClientInvestmentTransaction? latestTransaction)
    {
        var accountNumber = NormalizeAccount(account.AccountNumber);
        var transactionText = latestTransaction?.Description ?? "";
        return evidenceItems
            .Where(item => ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) && !string.IsNullOrWhiteSpace(item.SourcePath))
            .Select(item => new
            {
                Item = item,
                Text = $"{item.Title} {item.FileName} {item.RelativePath}",
                Score = (!string.IsNullOrWhiteSpace(accountNumber) && NormalizeAccount($"{item.Title}{item.FileName}{item.RelativePath}")?.Contains(accountNumber, StringComparison.OrdinalIgnoreCase) == true ? 100 : 0) +
                        (ContainsAny($"{item.Title} {item.FileName} {item.RelativePath}", TransferTerms.Concat(SurrenderTerms)) ? 30 : 0) +
                        (ContainsAny(transactionText, TransferTerms.Concat(SurrenderTerms)) && ContainsAny($"{item.Title} {item.FileName}", TransferTerms.Concat(SurrenderTerms)) ? 20 : 0)
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Item.FileLastWriteTimeUtc ?? entry.Item.CreatedAtUtc)
            .Take(5)
            .Select(entry => new ClientInvestmentEvidenceCandidateModel(
                entry.Item.Id,
                entry.Item.FileName ?? entry.Item.Title,
                entry.Item.RelativePath,
                $"/client-evidence/items/{entry.Item.Id}/file"))
            .ToList();
    }

    private static ClientInvestmentProposal BuildProposal(
        ClientInvestmentAccount account,
        ClientInvestmentStatus status,
        IReadOnlyList<ClientFundValuation> valuations,
        ClientInvestmentTransaction? latestTransaction,
        IReadOnlyList<ClientInvestmentAccount>? duplicates,
        IReadOnlyList<ClientInvestmentEvidenceCandidateModel> evidence)
    {
        var evidenceReference = evidence.FirstOrDefault()?.FileName;
        if (status.IsCurrent)
        {
            var latestDate = valuations.Max(value => value.ValuationDate);
            return new(ClientInvestmentReconciliationOutcomes.Current, null, null,
                $"Matching current valuation available{(latestDate.HasValue ? $" at {latestDate:yyyy-MM-dd}" : "")}.",
                evidenceReference ?? $"KCAS current valuation {latestDate:yyyy-MM-dd}");
        }

        if (duplicates?.Count > 1)
        {
            var related = duplicates.FirstOrDefault(item => item.Id != account.Id);
            return new(ClientInvestmentReconciliationOutcomes.DuplicateContinuation, account.SurrenderDate, related?.Id,
                "Another account record uses the same normalized account number; verify duplicate or administrator continuation.",
                evidenceReference ?? "KCAS duplicate account-number comparison");
        }

        if (account.SurrenderDate.HasValue)
        {
            return new(ClientInvestmentReconciliationOutcomes.HistoricalSurrendered, account.SurrenderDate, null,
                $"Recorded as surrendered on {account.SurrenderDate:yyyy-MM-dd} with no current valuation.",
                evidenceReference ?? $"KCAS recorded surrender date {account.SurrenderDate:yyyy-MM-dd}");
        }

        if (latestTransaction?.TransactionDate.HasValue == true)
        {
            var description = latestTransaction.Description ?? "";
            if (ContainsAny(description, TransferTerms))
            {
                return new(ClientInvestmentReconciliationOutcomes.Transferred, latestTransaction.TransactionDate, null,
                    "Latest transaction indicates a transfer or administrator continuation; verify the effective date and successor.",
                    evidenceReference ?? $"Transaction {latestTransaction.TransactionDate:yyyy-MM-dd}: {description}");
            }
            if (ContainsAny(description, SurrenderTerms) ||
                (latestTransaction.WithdrawalAmountZar ?? 0) > 0 ||
                (latestTransaction.WithdrawalAmountForeign ?? 0) > 0)
            {
                return new(ClientInvestmentReconciliationOutcomes.HistoricalSurrendered, latestTransaction.TransactionDate, null,
                    "Latest transaction indicates a surrender, redemption or final withdrawal; verify the effective date.",
                    evidenceReference ?? $"Transaction {latestTransaction.TransactionDate:yyyy-MM-dd}: {description}");
            }
        }

        return new(ClientInvestmentReconciliationOutcomes.NeedsFollowUp, null, null,
            "No current valuation and no evidence-backed effective surrender date were identified.",
            evidenceReference ?? "KCAS account, valuation and transaction comparison");
    }

    private static string? NormalizeAccount(string? value) =>
        ClientInvestmentStatusClassifier.NormalizeAccountNumber(value);

    private static bool ContainsAny(string value, IEnumerable<string> terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ValidationException(message) : value.Trim();
}

public sealed record InvestmentReconciliationQuery(
    string? IssueType = null,
    int? ClientId = null,
    string? Search = null);

public static class InvestmentReconciliationIssueTypes
{
    public const string DuplicateAccountMatch = "DuplicateAccountMatch";
    public const string CurrentValuationAfterSurrender = "CurrentValuationAfterSurrender";
    public const string UnmatchedValuation = "UnmatchedValuation";
    public const string MissingCurrentValuation = "MissingCurrentValuation";
    public const string DuplicateHistoricalAccount = "DuplicateHistoricalAccount";

    public static IReadOnlyList<string> All { get; } =
        [CurrentValuationAfterSurrender, UnmatchedValuation, DuplicateAccountMatch, DuplicateHistoricalAccount, MissingCurrentValuation];

    public static string Label(string issueType) => issueType switch
    {
        DuplicateAccountMatch => "Multiple account matches",
        CurrentValuationAfterSurrender => "Current valuation / surrender conflict",
        UnmatchedValuation => "No matching account",
        MissingCurrentValuation => "No current valuation",
        DuplicateHistoricalAccount => "Duplicate / continuation account",
        _ => issueType
    };
}

public sealed class InvestmentReconciliationModel
{
    public InvestmentReconciliationQuery Query { get; init; } = new();
    public List<InvestmentReconciliationIssue> Issues { get; init; } = [];
    public List<InvestmentSummaryClientOption> ClientOptions { get; init; } = [];
    public int TotalIssueCount { get; init; }
    public int DuplicateMatchCount { get; init; }
    public int SurrenderConflictCount { get; init; }
    public int UnmatchedValuationCount { get; init; }
    public int MissingCurrentValueCount { get; init; }
}

public sealed class InvestmentReconciliationIssue
{
    public string IssueType { get; init; } = "";
    public string IssueLabel { get; init; } = "";
    public int ClientId { get; init; }
    public string? KanaanId { get; init; }
    public string ClientDisplayName { get; init; } = "";
    public int? ValuationId { get; init; }
    public int? LegacyFundId { get; init; }
    public string? AccountNumber { get; init; }
    public string? Administrator { get; init; }
    public string? FundName { get; init; }
    public DateOnly? ValuationDate { get; init; }
    public decimal? AmountZar { get; init; }
    public string Details { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
    public IReadOnlyList<int> AccountIds { get; init; } = [];
    public int SeverityOrder { get; init; }
}

public sealed class ClientInvestmentReconciliationPageModel
{
    public int ClientId { get; init; }
    public string DisplayName { get; init; } = "";
    public string? KanaanId { get; init; }
    public string? ClientFolder { get; init; }
    public bool RequiresReview { get; init; }
    public bool IsComplete { get; init; }
    public int VerifiedCount { get; init; }
    public int NeedsFollowUpCount { get; init; }
    public List<ClientInvestmentReconciliationAccountModel> Accounts { get; init; } = [];
    public List<InvestmentReconciliationIssue> UnmatchedIssues { get; init; } = [];
    public List<ClientInvestmentLinkedClientModel> LinkedClients { get; init; } = [];
    public List<ClientInvestmentRelatedAccountOptionModel> RelatedAccountOptions { get; init; } = [];
}

public sealed class ClientInvestmentReconciliationAccountModel
{
    public int AccountId { get; init; }
    public int? LegacyInvestmentAccountId { get; init; }
    public string? AccountNumber { get; init; }
    public string? Administrator { get; init; }
    public string? ProductName { get; init; }
    public string? FundName { get; init; }
    public DateOnly? InvestmentDate { get; init; }
    public DateOnly? SurrenderDate { get; init; }
    public bool IsCurrent { get; init; }
    public string StatusReason { get; init; } = "";
    public decimal? CurrentValueZar { get; init; }
    public decimal? CurrentValueForeign { get; init; }
    public DateOnly? LatestValuationDate { get; init; }
    public DateOnly? LatestTransactionDate { get; init; }
    public string? LatestTransactionDescription { get; init; }
    public List<string> IssueLabels { get; init; } = [];
    public List<ClientInvestmentEvidenceCandidateModel> Evidence { get; init; } = [];
    public string ProposedOutcome { get; init; } = ClientInvestmentReconciliationOutcomes.NeedsFollowUp;
    public DateOnly? ProposedSurrenderDate { get; init; }
    public int? ProposedRelatedAccountId { get; init; }
    public string ProposalReason { get; init; } = "";
    public string ProposalEvidenceReference { get; init; } = "";
    public int? ReviewId { get; init; }
    public string? ReviewOutcome { get; init; }
    public string? ReviewReason { get; init; }
    public string? ReviewEvidenceReference { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public string? ReviewedBy { get; init; }
    public bool IsVerified { get; init; }
    public bool NeedsFollowUp { get; init; }
    public bool ReviewIsStale { get; init; }
}

public sealed record ClientInvestmentEvidenceCandidateModel(int Id, string FileName, string? RelativePath, string FileUrl);

public sealed record ClientInvestmentLinkedClientModel(
    int ClientId,
    string DisplayName,
    string? KanaanId,
    string LifecycleStatus,
    int InvestmentAccountCount,
    decimal CurrentValueZar);

public sealed record ClientInvestmentRelatedAccountOptionModel(
    int AccountId,
    int ClientId,
    string ClientDisplayName,
    string? KanaanId,
    string? AccountNumber,
    string? Administrator,
    string? FundName);

public sealed class ClientInvestmentReconciliationReviewRequest
{
    public string Outcome { get; set; } = ClientInvestmentReconciliationOutcomes.NeedsFollowUp;
    public DateOnly? SurrenderDate { get; set; }
    public int? RelatedAccountId { get; set; }
    public string? EvidenceReference { get; set; }
    public string? Reason { get; set; }
}

public sealed record ClientInvestmentReconciliationBatchRequest(
    int AccountId,
    ClientInvestmentReconciliationReviewRequest Review);

internal sealed record ClientInvestmentProposal(
    string Outcome,
    DateOnly? SurrenderDate,
    int? RelatedAccountId,
    string Reason,
    string EvidenceReference);
