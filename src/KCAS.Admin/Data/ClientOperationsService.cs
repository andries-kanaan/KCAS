using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientOperationsService(ApplicationDbContext db, ClientCodeGenerator clientCodeGenerator)
{
    public async Task<ClientEditModel> LoadClientAsync(int? clientId)
    {
        if (clientId is null)
        {
            return new ClientEditModel();
        }

        var client = await LoadClientAggregateAsync(clientId.Value);
        return ClientEditModel.FromClient(client);
    }

    public async Task<int> SaveClientAsync(ClientEditModel model)
    {
        var surname = Normalize(model.SurnameOrEntityName);
        var displayName = Normalize(model.DisplayName);
        var kanaanId = Normalize(model.KanaanId);

        if (string.IsNullOrWhiteSpace(surname))
        {
            throw new ValidationException("Surname or entity name is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Normalize(model.FullName) ?? surname;
        }

        Client client;
        if (model.Id is null)
        {
            client = new Client();
            db.Clients.Add(client);
        }
        else
        {
            client = await LoadClientAggregateAsync(model.Id.Value);
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        client.KanaanId = kanaanId ?? await clientCodeGenerator.GenerateAsync();
        client.Title = Normalize(model.Title);
        client.Initials = Normalize(model.Initials);
        client.FullName = Normalize(model.FullName);
        client.SurnameOrEntityName = surname;
        client.DisplayName = displayName;
        client.Language = Normalize(model.Language);
        client.ClientFolder = Normalize(model.ClientFolder);
        client.ClientCategory = Normalize(model.ClientCategory) ?? ClientCategories.NaturalPerson;
        client.ClientCategorySource = ClientCategorySources.Manual;
        client.ClientCategoryReason = "Category selected on the client edit page.";
        client.ClientCategoryUpdatedAtUtc = DateTime.UtcNow;
        client.ClientCategoryUpdatedBy = Normalize(model.UpdatedBy);
        client.IsActive = model.IsActive;

        client.PersonalProfile ??= new ClientPersonalProfile { Client = client };
        client.PersonalProfile.SouthAfricanIdNumber = Normalize(model.SouthAfricanIdNumber);
        client.PersonalProfile.Gender = Normalize(model.Gender);
        client.PersonalProfile.MaritalStatus = Normalize(model.MaritalStatus);
        client.PersonalProfile.TaxNumber = Normalize(model.TaxNumber);
        client.PersonalProfile.TaxOffice = Normalize(model.TaxOffice);
        client.PersonalProfile.HighestQualification = Normalize(model.HighestQualification);
        client.PersonalProfile.NumberOfDependents = model.NumberOfDependents;
        client.PersonalProfile.Smoker = model.Smoker;
        client.PersonalProfile.WorkdayTravelPercent = model.WorkdayTravelPercent;
        client.PersonalProfile.FamilyDetailRaw = Normalize(model.FamilyDetailRaw);

        client.FinancialProfile ??= new ClientFinancialProfile { Client = client };
        client.FinancialProfile.Employer = Normalize(model.Employer);
        client.FinancialProfile.Occupation = Normalize(model.Occupation);
        client.FinancialProfile.GrossMonthlySalary = model.GrossMonthlySalary;
        client.FinancialProfile.MonthlyExpenses = model.MonthlyExpenses;
        client.FinancialProfile.RetirementAge = model.RetirementAge;
        client.FinancialProfile.PensionFundName = Normalize(model.PensionFundName);
        client.FinancialProfile.RepresentativeName = Normalize(model.RepresentativeName);
        client.FinancialProfile.OtherGoalsRaw = Normalize(model.OtherGoalsRaw);
        client.FinancialProfile.BankDetailRaw = Normalize(model.BankDetailRaw);
        client.FinancialProfile.WillDetailRaw = Normalize(model.WillDetailRaw);
        client.FinancialProfile.OtherDetailsRaw = Normalize(model.OtherDetailsRaw);

        ReplaceContacts(client, model.ContactPoints);
        ReplaceAddresses(client, model.Addresses);
        ReplaceRelationships(client, model.Relationships);

        await db.SaveChangesAsync();
        return client.Id;
    }

    public async Task<ClientNoteEditModel> LoadNoteAsync(int clientId, int? noteId)
    {
        if (noteId is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == clientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            return new ClientNoteEditModel
            {
                ClientId = clientId,
                NoteDate = DateOnly.FromDateTime(DateTime.Today)
            };
        }

        var note = await db.ClientNotes
            .AsNoTracking()
            .SingleOrDefaultAsync(note => note.ClientId == clientId && note.Id == noteId.Value)
            ?? throw new InvalidOperationException("Note not found.");

        return ClientNoteEditModel.FromNote(note);
    }

    public async Task<int> SaveNoteAsync(ClientNoteEditModel model, string? userName)
    {
        var title = Normalize(model.Title);
        var details = Normalize(model.Details);

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(details))
        {
            throw new ValidationException("Enter a note title or details.");
        }

        ClientNote note;
        if (model.Id is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == model.ClientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            note = new ClientNote
            {
                ClientId = model.ClientId,
                IsFinal = false,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientNotes.Add(note);
        }
        else
        {
            note = await db.ClientNotes.SingleOrDefaultAsync(note => note.ClientId == model.ClientId && note.Id == model.Id.Value)
                ?? throw new InvalidOperationException("Note not found.");

            if (note.IsFinal)
            {
                throw new InvalidOperationException("Final notes cannot be edited.");
            }
        }

        note.NoteDate = model.NoteDate;
        note.Title = title;
        note.Details = details;
        note.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return note.Id;
    }

    public async Task FinalizeNoteAsync(int clientId, int noteId, string? userName)
    {
        var note = await db.ClientNotes.SingleOrDefaultAsync(note => note.ClientId == clientId && note.Id == noteId)
            ?? throw new InvalidOperationException("Note not found.");

        if (note.IsDeleted)
        {
            throw new InvalidOperationException("Deleted notes cannot be finalized.");
        }

        note.IsFinal = true;
        note.UpdatedBy = Normalize(userName);
        await db.SaveChangesAsync();
    }

    public async Task DeleteNoteAsync(int clientId, int noteId, string? userName)
    {
        var note = await db.ClientNotes.SingleOrDefaultAsync(note => note.ClientId == clientId && note.Id == noteId)
            ?? throw new InvalidOperationException("Note not found.");

        note.IsDeleted = true;
        note.UpdatedBy = Normalize(userName);
        await db.SaveChangesAsync();
    }

    public async Task<ClientKycPolicyEditModel> LoadKycPolicyAsync(int clientId, int? policyId)
    {
        if (policyId is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == clientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            return new ClientKycPolicyEditModel
            {
                ClientId = clientId,
                IncludeInCalculations = true
            };
        }

        var policy = await db.ClientKycPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.ClientId == clientId && policy.Id == policyId.Value)
            ?? throw new InvalidOperationException("KYC policy not found.");

        return ClientKycPolicyEditModel.FromPolicy(policy);
    }

    public async Task<KycReferenceOptionsModel> LoadKycReferenceOptionsAsync()
    {
        var mainClasses = await db.KycMainClassReferences
            .AsNoTracking()
            .OrderBy(reference => reference.Name)
            .Select(reference => new KycMainClassOption(reference.Name, reference.LegacyMainClassId))
            .ToListAsync();

        var subClasses = await db.KycSubClassReferences
            .AsNoTracking()
            .Include(reference => reference.MainClass)
            .OrderBy(reference => reference.MainClass.Name)
            .ThenBy(reference => reference.Name)
            .Select(reference => new KycSubClassOption(reference.Name, reference.MainClass.Name, reference.LegacySubClassId))
            .ToListAsync();

        var administrators = await db.InvestmentAdministratorReferences
            .AsNoTracking()
            .Where(reference => reference.IsCurrent)
            .OrderBy(reference => reference.Name)
            .Select(reference => reference.Name)
            .ToListAsync();

        var funds = await db.InvestmentFundReferences
            .AsNoTracking()
            .Where(reference => reference.IsCurrent)
            .OrderBy(reference => reference.Name)
            .Select(reference => reference.Name)
            .ToListAsync();

        return new KycReferenceOptionsModel(mainClasses, subClasses, administrators, funds);
    }

    public async Task<int> SaveKycPolicyAsync(ClientKycPolicyEditModel model, string? userName)
    {
        var mainClassName = Normalize(model.MainClassName);
        var subClassName = Normalize(model.SubClassName);
        var administrator = Normalize(model.Administrator);
        var product = Normalize(model.Product);
        var policyNumber = Normalize(model.PolicyNumber);
        var description = Normalize(model.Description);
        var fund = Normalize(model.Fund);

        if (string.IsNullOrWhiteSpace(mainClassName) && string.IsNullOrWhiteSpace(subClassName))
        {
            throw new ValidationException("Enter a KYC main class or sub class.");
        }

        if (string.IsNullOrWhiteSpace(administrator) &&
            string.IsNullOrWhiteSpace(product) &&
            string.IsNullOrWhiteSpace(policyNumber) &&
            string.IsNullOrWhiteSpace(description) &&
            string.IsNullOrWhiteSpace(fund) &&
            model.Value is null &&
            model.LifeCover is null &&
            model.DisabilityCover is null &&
            model.DreadDiseaseCover is null &&
            model.CompulsoryContributionValue is null &&
            model.VoluntaryContributionValue is null &&
            model.Debt is null &&
            model.MonthlyPremium is null &&
            model.OnceOffPremium is null &&
            model.MonthlyIncome is null)
        {
            throw new ValidationException("Enter policy details or at least one financial amount.");
        }

        ClientKycPolicy policy;
        if (model.Id is null)
        {
            var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == model.ClientId)
                ?? throw new InvalidOperationException("Client not found.");

            policy = new ClientKycPolicy
            {
                ClientId = model.ClientId,
                KanaanId = client.KanaanId,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientKycPolicies.Add(policy);
        }
        else
        {
            policy = await db.ClientKycPolicies.SingleOrDefaultAsync(policy => policy.ClientId == model.ClientId && policy.Id == model.Id.Value)
                ?? throw new InvalidOperationException("KYC policy not found.");

        }

        policy.MainClassName = mainClassName;
        policy.SubClassName = subClassName;
        policy.SubClassExtra = Normalize(model.SubClassExtra);
        policy.Administrator = administrator;
        policy.Product = product;
        policy.PolicyNumber = policyNumber;
        policy.Description = description;
        policy.Fund = fund;
        policy.Value = model.Value;
        policy.LifeCover = model.LifeCover;
        policy.DisabilityCover = model.DisabilityCover;
        policy.DreadDiseaseCover = model.DreadDiseaseCover;
        policy.CompulsoryContributionValue = model.CompulsoryContributionValue;
        policy.VoluntaryContributionValue = model.VoluntaryContributionValue;
        policy.Debt = model.Debt;
        policy.MonthlyPremium = model.MonthlyPremium;
        policy.OnceOffPremium = model.OnceOffPremium;
        policy.MonthlyIncome = model.MonthlyIncome;
        policy.CapitalAdequacyRatioPercent = model.CapitalAdequacyRatioPercent;
        policy.TaxPercent = model.TaxPercent;
        policy.IncludeInCalculations = model.IncludeInCalculations;
        policy.SurrenderOrLiquidate = model.SurrenderOrLiquidate;
        policy.IsRetirementAnnuity = model.IsRetirementAnnuity;
        policy.IsPreservationFund = model.IsPreservationFund;
        policy.IsRetrenchmentPackage = model.IsRetrenchmentPackage;
        policy.IsQuote = model.IsQuote;
        policy.ValuationDate = model.ValuationDate;
        policy.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return policy.Id;
    }

    public async Task DeleteKycPolicyAsync(int clientId, int policyId)
    {
        var policy = await db.ClientKycPolicies.SingleOrDefaultAsync(policy => policy.ClientId == clientId && policy.Id == policyId)
            ?? throw new InvalidOperationException("KYC policy not found.");

        db.ClientKycPolicies.Remove(policy);
        await db.SaveChangesAsync();
    }

    public async Task<ClientInvestmentAccountEditModel> LoadInvestmentAccountAsync(int clientId, int? accountId)
    {
        if (accountId is null)
        {
            if (!await db.Clients.AnyAsync(client => client.Id == clientId))
            {
                throw new InvalidOperationException("Client not found.");
            }

            return new ClientInvestmentAccountEditModel { ClientId = clientId };
        }

        var account = await db.ClientInvestmentAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.ClientId == clientId && account.Id == accountId.Value)
            ?? throw new InvalidOperationException("Investment account not found.");

        return ClientInvestmentAccountEditModel.FromAccount(account);
    }

    public async Task<InvestmentReferenceOptionsModel> LoadInvestmentReferenceOptionsAsync()
    {
        var administrators = await db.InvestmentAdministratorReferences
            .AsNoTracking()
            .Where(reference => reference.IsCurrent)
            .OrderBy(reference => reference.Name)
            .Select(reference => reference.Name)
            .ToListAsync();

        var productTypes = await db.InvestmentProductTypeReferences
            .AsNoTracking()
            .OrderBy(reference => reference.Name)
            .Select(reference => reference.Name)
            .ToListAsync();

        var funds = await db.InvestmentFundReferences
            .AsNoTracking()
            .Where(reference => reference.IsCurrent)
            .OrderBy(reference => reference.Name)
            .Select(reference => reference.Name)
            .ToListAsync();

        return new InvestmentReferenceOptionsModel(administrators, productTypes, funds);
    }

    public async Task<int> SaveInvestmentAccountAsync(ClientInvestmentAccountEditModel model, string? userName)
    {
        var administrator = Normalize(model.Administrator);
        var accountNumber = Normalize(model.AccountNumber);
        var productName = Normalize(model.ProductName);
        var productType = Normalize(model.ProductType);
        var fundName = Normalize(model.FundName);

        if (string.IsNullOrWhiteSpace(administrator) &&
            string.IsNullOrWhiteSpace(accountNumber) &&
            string.IsNullOrWhiteSpace(productName) &&
            string.IsNullOrWhiteSpace(productType) &&
            string.IsNullOrWhiteSpace(fundName) &&
            model.InvestmentDate is null &&
            model.SurrenderDate is null)
        {
            throw new ValidationException("Enter investment account details.");
        }

        ClientInvestmentAccount account;
        if (model.Id is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == model.ClientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            account = new ClientInvestmentAccount
            {
                ClientId = model.ClientId,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientInvestmentAccounts.Add(account);
        }
        else
        {
            account = await db.ClientInvestmentAccounts.SingleOrDefaultAsync(account => account.ClientId == model.ClientId && account.Id == model.Id.Value)
                ?? throw new InvalidOperationException("Investment account not found.");
        }

        account.InvestmentDate = model.InvestmentDate;
        account.SurrenderDate = model.SurrenderDate;
        account.Administrator = administrator;
        account.AccountNumber = accountNumber;
        account.ProductName = productName;
        account.ProductType = productType;
        account.FundName = fundName;
        account.IsLinkedHead = model.IsLinkedHead;
        account.IsFinal = model.IsFinal;
        account.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return account.Id;
    }

    public async Task DeleteInvestmentAccountAsync(int clientId, int accountId)
    {
        var account = await db.ClientInvestmentAccounts.SingleOrDefaultAsync(account => account.ClientId == clientId && account.Id == accountId)
            ?? throw new InvalidOperationException("Investment account not found.");

        db.ClientInvestmentAccounts.Remove(account);
        await db.SaveChangesAsync();
    }

    public async Task<ClientInvestmentTransactionEditModel> LoadInvestmentTransactionAsync(int clientId, int accountId, int? transactionId)
    {
        var accountExists = await db.ClientInvestmentAccounts.AnyAsync(account => account.ClientId == clientId && account.Id == accountId);
        if (!accountExists)
        {
            throw new InvalidOperationException("Investment account not found.");
        }

        if (transactionId is null)
        {
            return new ClientInvestmentTransactionEditModel
            {
                ClientId = clientId,
                ClientInvestmentAccountId = accountId,
                TransactionDate = DateOnly.FromDateTime(DateTime.Today)
            };
        }

        var transaction = await db.ClientInvestmentTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(transaction => transaction.ClientInvestmentAccountId == accountId && transaction.Id == transactionId.Value)
            ?? throw new InvalidOperationException("Investment transaction not found.");

        return ClientInvestmentTransactionEditModel.FromTransaction(clientId, transaction);
    }

    public async Task<int> SaveInvestmentTransactionAsync(ClientInvestmentTransactionEditModel model, string? userName)
    {
        var account = await db.ClientInvestmentAccounts.AsNoTracking().SingleOrDefaultAsync(account =>
                account.ClientId == model.ClientId && account.Id == model.ClientInvestmentAccountId)
            ?? throw new InvalidOperationException("Investment account not found.");

        if (string.IsNullOrWhiteSpace(Normalize(model.Description)) &&
            model.TransactionDate is null &&
            model.ExchangeRate is null &&
            model.InvestmentAmountForeign is null &&
            model.InvestmentAmountZar is null &&
            model.WithdrawalAmountForeign is null &&
            model.WithdrawalAmountZar is null &&
            model.BalanceForeign is null &&
            model.BalanceZar is null)
        {
            throw new ValidationException("Enter transaction details or at least one amount.");
        }

        ClientInvestmentTransaction transaction;
        if (model.Id is null)
        {
            transaction = new ClientInvestmentTransaction
            {
                ClientInvestmentAccountId = model.ClientInvestmentAccountId,
                LegacyInvestmentAccountId = account.LegacyInvestmentAccountId,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientInvestmentTransactions.Add(transaction);
        }
        else
        {
            transaction = await db.ClientInvestmentTransactions.SingleOrDefaultAsync(transaction =>
                    transaction.ClientInvestmentAccountId == model.ClientInvestmentAccountId && transaction.Id == model.Id.Value)
                ?? throw new InvalidOperationException("Investment transaction not found.");
        }

        transaction.TransactionDate = model.TransactionDate;
        transaction.Description = Normalize(model.Description);
        transaction.ExchangeRate = model.ExchangeRate;
        transaction.InvestmentAmountForeign = model.InvestmentAmountForeign;
        transaction.InvestmentAmountZar = model.InvestmentAmountZar;
        transaction.WithdrawalAmountForeign = model.WithdrawalAmountForeign;
        transaction.WithdrawalAmountZar = model.WithdrawalAmountZar;
        transaction.InvestmentFrequency = Normalize(model.InvestmentFrequency);
        transaction.AnnualIncreasePercent = model.AnnualIncreasePercent;
        transaction.BalanceForeign = model.BalanceForeign;
        transaction.BalanceZar = model.BalanceZar;
        transaction.IsFinal = model.IsFinal;
        transaction.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return transaction.Id;
    }

    public async Task FinalizeInvestmentTransactionAsync(int clientId, int accountId, int transactionId, string? userName)
    {
        var transaction = await LoadInvestmentTransactionForMutationAsync(clientId, accountId, transactionId);
        if (transaction.IsDeleted)
        {
            throw new InvalidOperationException("Deleted investment transactions cannot be finalized.");
        }

        transaction.IsFinal = true;
        transaction.UpdatedBy = Normalize(userName);
        await db.SaveChangesAsync();
    }

    public async Task DeleteInvestmentTransactionAsync(int clientId, int accountId, int transactionId, string? userName)
    {
        var transaction = await LoadInvestmentTransactionForMutationAsync(clientId, accountId, transactionId);
        transaction.IsDeleted = true;
        transaction.UpdatedBy = Normalize(userName);
        await db.SaveChangesAsync();
    }

    public async Task<ClientFundSummaryModel> LoadFundSummaryAsync(int clientId, string? filter = null)
    {
        var client = await db.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(client => client.InvestmentAccounts)
                .ThenInclude(account => account.Transactions)
            .Include(client => client.FundValuations)
            .SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");

        var rows = BuildFundSummaryRows(client.InvestmentAccounts, client.FundValuations);
        var normalizedFilter = Normalize(filter);
        if (!string.IsNullOrWhiteSpace(normalizedFilter))
        {
            rows = rows
                .Where(row =>
                    Contains(row.Administrator, normalizedFilter) ||
                    Contains(row.ProductName, normalizedFilter) ||
                    Contains(row.ProductType, normalizedFilter) ||
                    Contains(row.FundName, normalizedFilter) ||
                    Contains(row.AccountNumber, normalizedFilter))
                .ToList();
        }

        return new ClientFundSummaryModel
        {
            ClientId = client.Id,
            ClientDisplayName = client.DisplayName,
            Filter = normalizedFilter,
            Rows = rows
        };
    }

    public async Task<ClientKycRecommendationEditModel> LoadKycRecommendationAsync(int clientId, int? recommendationId)
    {
        if (recommendationId is null)
        {
            if (!await db.Clients.AnyAsync(client => client.Id == clientId))
            {
                throw new InvalidOperationException("Client not found.");
            }

            return new ClientKycRecommendationEditModel
            {
                ClientId = clientId,
                RecommendationDate = DateOnly.FromDateTime(DateTime.Today),
                Status = "Open"
            };
        }

        var recommendation = await db.ClientKycRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(recommendation => recommendation.ClientId == clientId && recommendation.Id == recommendationId.Value)
            ?? throw new InvalidOperationException("KYC recommendation not found.");

        return ClientKycRecommendationEditModel.FromRecommendation(recommendation);
    }

    public async Task<int> SaveKycRecommendationAsync(ClientKycRecommendationEditModel model, string? userName)
    {
        var recommendationType = Normalize(model.RecommendationType);
        var status = Normalize(model.Status) ?? "Open";
        var details = Normalize(model.Details);
        var outcome = Normalize(model.Outcome);

        if (string.IsNullOrWhiteSpace(recommendationType) && string.IsNullOrWhiteSpace(details))
        {
            throw new ValidationException("Enter a recommendation type or details.");
        }

        ClientKycRecommendation recommendation;
        if (model.Id is null)
        {
            var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == model.ClientId)
                ?? throw new InvalidOperationException("Client not found.");

            recommendation = new ClientKycRecommendation
            {
                ClientId = model.ClientId,
                KanaanId = client.KanaanId,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientKycRecommendations.Add(recommendation);
        }
        else
        {
            recommendation = await db.ClientKycRecommendations.SingleOrDefaultAsync(recommendation =>
                    recommendation.ClientId == model.ClientId && recommendation.Id == model.Id.Value)
                ?? throw new InvalidOperationException("KYC recommendation not found.");
        }

        recommendation.ClientKycPolicyId = model.ClientKycPolicyId;
        recommendation.RecommendationType = recommendationType;
        recommendation.Status = status;
        recommendation.RecommendationDate = model.RecommendationDate;
        recommendation.Details = details;
        recommendation.Outcome = outcome;
        recommendation.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return recommendation.Id;
    }

    public async Task DeleteKycRecommendationAsync(int clientId, int recommendationId)
    {
        var recommendation = await db.ClientKycRecommendations.SingleOrDefaultAsync(recommendation =>
                recommendation.ClientId == clientId && recommendation.Id == recommendationId)
            ?? throw new InvalidOperationException("KYC recommendation not found.");

        db.ClientKycRecommendations.Remove(recommendation);
        await db.SaveChangesAsync();
    }

    public async Task<List<ClientTransferTargetModel>> SearchKycTransferTargetsAsync(int sourceClientId, string? search)
    {
        var source = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == sourceClientId)
            ?? throw new InvalidOperationException("Client not found.");

        var query = db.Clients.AsNoTracking().Where(client => client.Id != sourceClientId);
        var normalizedSearch = Normalize(search);
        if (string.IsNullOrWhiteSpace(normalizedSearch) && !string.IsNullOrWhiteSpace(source.KanaanId))
        {
            query = query.Where(client => client.KanaanId == source.KanaanId);
        }
        else if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(client =>
                client.DisplayName.Contains(normalizedSearch) ||
                client.SurnameOrEntityName.Contains(normalizedSearch) ||
                (client.KanaanId != null && client.KanaanId.Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(client => client.DisplayName)
            .Take(25)
            .Select(client => new ClientTransferTargetModel
            {
                ClientId = client.Id,
                DisplayName = client.DisplayName,
                SurnameOrEntityName = client.SurnameOrEntityName,
                KanaanId = client.KanaanId
            })
            .ToListAsync();
    }

    public async Task CopyOrTransferKycAsync(KycTransferModel model, string? userName)
    {
        if (model.SourceClientId == model.TargetClientId)
        {
            throw new ValidationException("Choose a different target client.");
        }

        if (model.PolicyIds.Count == 0 && model.RecommendationIds.Count == 0)
        {
            throw new ValidationException("Select at least one KYC policy or recommendation.");
        }

        var target = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == model.TargetClientId)
            ?? throw new InvalidOperationException("Target client not found.");

        var policies = await db.ClientKycPolicies
            .Where(policy => policy.ClientId == model.SourceClientId && model.PolicyIds.Contains(policy.Id))
            .ToListAsync();
        var recommendations = await db.ClientKycRecommendations
            .Where(recommendation => recommendation.ClientId == model.SourceClientId && model.RecommendationIds.Contains(recommendation.Id))
            .ToListAsync();

        if (policies.Count != model.PolicyIds.Count || recommendations.Count != model.RecommendationIds.Count)
        {
            throw new InvalidOperationException("One or more selected KYC rows were not found.");
        }

        var updatedBy = Normalize(userName);
        if (model.Operation == KycTransferOperation.Transfer)
        {
            foreach (var policy in policies)
            {
                policy.ClientId = target.Id;
                policy.KanaanId = target.KanaanId;
                policy.UpdatedBy = updatedBy;
            }

            foreach (var recommendation in recommendations)
            {
                recommendation.ClientId = target.Id;
                recommendation.KanaanId = target.KanaanId;
                recommendation.UpdatedBy = updatedBy;
            }
        }
        else
        {
            var copiedPolicyIds = new Dictionary<int, int>();
            foreach (var policy in policies)
            {
                var copy = CopyPolicy(policy, target, updatedBy);
                db.ClientKycPolicies.Add(copy);
                await db.SaveChangesAsync();
                copiedPolicyIds[policy.Id] = copy.Id;
            }

            foreach (var recommendation in recommendations)
            {
                var copy = CopyRecommendation(recommendation, target, updatedBy);
                if (copy.ClientKycPolicyId.HasValue && copiedPolicyIds.TryGetValue(copy.ClientKycPolicyId.Value, out var copiedPolicyId))
                {
                    copy.ClientKycPolicyId = copiedPolicyId;
                }
                else
                {
                    copy.ClientKycPolicyId = null;
                }

                db.ClientKycRecommendations.Add(copy);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<ClientInvestmentTransaction> LoadInvestmentTransactionForMutationAsync(int clientId, int accountId, int transactionId)
    {
        return await db.ClientInvestmentTransactions
            .Include(transaction => transaction.InvestmentAccount)
            .SingleOrDefaultAsync(transaction =>
                transaction.Id == transactionId &&
                transaction.ClientInvestmentAccountId == accountId &&
                transaction.InvestmentAccount.ClientId == clientId)
            ?? throw new InvalidOperationException("Investment transaction not found.");
    }

    private static List<ClientFundSummaryRowModel> BuildFundSummaryRows(
        IEnumerable<ClientInvestmentAccount> accounts,
        IEnumerable<ClientFundValuation> valuations)
    {
        var accountList = accounts.ToList();
        var valuationList = valuations.ToList();
        var rows = new List<ClientFundSummaryRowModel>();

        foreach (var account in accountList)
        {
            var matchedValuations = CurrentValuations(account, valuationList).ToList();
            var latestBalance = LatestBalanceTransaction(account);
            if (matchedValuations.Count == 0)
            {
                rows.Add(new ClientFundSummaryRowModel
                {
                    AccountId = account.Id,
                    AccountNumber = account.AccountNumber,
                    Administrator = account.Administrator,
                    ProductName = account.ProductName,
                    ProductType = account.ProductType,
                    FundName = account.FundName,
                    CurrentValueZar = latestBalance?.BalanceZar,
                    CurrentValueForeign = latestBalance?.BalanceForeign,
                    CurrentValueDate = latestBalance?.TransactionDate,
                    Source = latestBalance is null ? "No current value" : "History balance",
                    TransactionCount = account.Transactions.Count(transaction => !transaction.IsDeleted)
                });
                continue;
            }

            foreach (var valuation in matchedValuations)
            {
                rows.Add(new ClientFundSummaryRowModel
                {
                    AccountId = account.Id,
                    AccountNumber = account.AccountNumber,
                    Administrator = valuation.Administrator ?? account.Administrator,
                    ProductName = valuation.ProductName ?? account.ProductName,
                    ProductType = valuation.ProductType ?? account.ProductType,
                    FundName = valuation.FundName,
                    CurrentValueZar = valuation.AmountZar,
                    CurrentValueForeign = valuation.AmountForeign,
                    CurrentValueDate = valuation.ValuationDate,
                    Source = "Fund valuation",
                    TransactionCount = account.Transactions.Count(transaction => !transaction.IsDeleted)
                });
            }
        }

        var accountNumbers = accountList
            .Select(account => NormalizeAccountNumber(account.AccountNumber))
            .Where(accountNumber => accountNumber is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var valuation in valuationList)
        {
            var valuationAccountNumber = NormalizeAccountNumber(valuation.InvestmentUniqueNumber);
            if (valuationAccountNumber is not null && accountNumbers.Contains(valuationAccountNumber))
            {
                continue;
            }

            rows.Add(new ClientFundSummaryRowModel
            {
                AccountNumber = valuation.InvestmentUniqueNumber,
                Administrator = valuation.Administrator,
                ProductName = valuation.ProductName,
                ProductType = valuation.ProductType,
                FundName = valuation.FundName,
                CurrentValueZar = valuation.AmountZar,
                CurrentValueForeign = valuation.AmountForeign,
                CurrentValueDate = valuation.ValuationDate,
                Source = "Unmatched fund valuation"
            });
        }

        return rows
            .OrderBy(row => row.Administrator)
            .ThenBy(row => row.ProductName)
            .ThenBy(row => row.AccountNumber)
            .ThenBy(row => row.FundName)
            .ToList();
    }

    private static ClientInvestmentTransaction? LatestBalanceTransaction(ClientInvestmentAccount account) =>
        account.Transactions
            .Where(transaction =>
                !transaction.IsDeleted &&
                ((transaction.BalanceZar.HasValue && transaction.BalanceZar.Value != 0) ||
                 (transaction.BalanceForeign.HasValue && transaction.BalanceForeign.Value != 0)))
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.LegacyInvestmentHistoryId)
            .FirstOrDefault();

    private static IEnumerable<ClientFundValuation> CurrentValuations(ClientInvestmentAccount account, IEnumerable<ClientFundValuation> valuations)
    {
        var accountNumber = NormalizeAccountNumber(account.AccountNumber);
        if (accountNumber is null)
        {
            return [];
        }

        var matches = valuations
            .Where(valuation => string.Equals(NormalizeAccountNumber(valuation.InvestmentUniqueNumber), accountNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var administrator = NormalizeLookup(account.Administrator);
        if (administrator is null)
        {
            return matches;
        }

        var administratorMatches = matches
            .Where(valuation => AdministratorsMatch(administrator, NormalizeLookup(valuation.Administrator)))
            .ToList();

        return administratorMatches.Count > 0 ? administratorMatches : matches;
    }

    private static ClientKycPolicy CopyPolicy(ClientKycPolicy source, Client target, string? userName) => new()
    {
        ClientId = target.Id,
        KanaanId = target.KanaanId,
        LegacyKycId = source.LegacyKycId,
        LegacyClientId = source.LegacyClientId,
        LegacyMainClassId = source.LegacyMainClassId,
        MainClassName = source.MainClassName,
        LegacySubClassId = source.LegacySubClassId,
        SubClassName = source.SubClassName,
        SubClassExtra = source.SubClassExtra,
        Administrator = source.Administrator,
        Product = source.Product,
        PolicyNumber = source.PolicyNumber,
        Description = source.Description,
        Fund = source.Fund,
        Value = source.Value,
        LifeCover = source.LifeCover,
        DisabilityCover = source.DisabilityCover,
        DreadDiseaseCover = source.DreadDiseaseCover,
        CompulsoryContributionValue = source.CompulsoryContributionValue,
        VoluntaryContributionValue = source.VoluntaryContributionValue,
        Debt = source.Debt,
        MonthlyPremium = source.MonthlyPremium,
        OnceOffPremium = source.OnceOffPremium,
        MonthlyIncome = source.MonthlyIncome,
        CapitalAdequacyRatioPercent = source.CapitalAdequacyRatioPercent,
        TaxPercent = source.TaxPercent,
        IncludeInCalculations = source.IncludeInCalculations,
        SurrenderOrLiquidate = source.SurrenderOrLiquidate,
        IsRetirementAnnuity = source.IsRetirementAnnuity,
        IsPreservationFund = source.IsPreservationFund,
        IsRetrenchmentPackage = source.IsRetrenchmentPackage,
        IsQuote = source.IsQuote,
        ValuationDate = source.ValuationDate,
        OpenedBy = userName,
        UpdatedBy = userName,
        PayloadJson = source.PayloadJson,
        ImportedAtUtc = DateTime.UtcNow
    };

    private static ClientKycRecommendation CopyRecommendation(ClientKycRecommendation source, Client target, string? userName) => new()
    {
        ClientId = target.Id,
        ClientKycPolicyId = source.ClientKycPolicyId,
        KanaanId = target.KanaanId,
        LegacyRecommendationId = source.LegacyRecommendationId,
        LegacyClientId = source.LegacyClientId,
        RecommendationType = source.RecommendationType,
        Status = source.Status,
        RecommendationDate = source.RecommendationDate,
        Details = source.Details,
        Outcome = source.Outcome,
        OpenedBy = userName,
        UpdatedBy = userName,
        PayloadJson = source.PayloadJson,
        ImportedAtUtc = DateTime.UtcNow
    };

    private async Task<Client> LoadClientAggregateAsync(int clientId)
    {
        return await db.Clients
            .Include(client => client.PersonalProfile)
            .Include(client => client.FinancialProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.Addresses)
            .Include(client => client.Relationships)
            .SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
    }

    private void ReplaceContacts(Client client, IEnumerable<ClientContactPointEditModel> contacts)
    {
        db.ClientContactPoints.RemoveRange(client.ContactPoints);
        client.ContactPoints.Clear();

        var rows = contacts
            .Select(contact => new ClientContactPointEditModel
            {
                ContactType = Normalize(contact.ContactType) ?? "Other",
                Label = Normalize(contact.Label),
                Value = Normalize(contact.Value) ?? "",
                IsPrimary = contact.IsPrimary
            })
            .Where(contact => !string.IsNullOrWhiteSpace(contact.Value))
            .ToList();

        foreach (var group in rows.GroupBy(contact => contact.ContactType, StringComparer.OrdinalIgnoreCase))
        {
            if (!group.Any(contact => contact.IsPrimary))
            {
                group.First().IsPrimary = true;
            }
        }

        var sortOrder = 0;
        foreach (var contact in rows)
        {
            client.ContactPoints.Add(new ClientContactPoint
            {
                ContactType = contact.ContactType,
                Label = contact.Label,
                Value = contact.Value ?? string.Empty,
                IsPrimary = contact.IsPrimary,
                SortOrder = sortOrder++
            });
        }
    }

    private void ReplaceAddresses(Client client, IEnumerable<ClientAddressEditModel> addresses)
    {
        db.ClientAddresses.RemoveRange(client.Addresses);
        client.Addresses.Clear();

        var sortOrder = 0;
        foreach (var address in addresses)
        {
            var lines = Normalize(address.LinesRaw);
            if (string.IsNullOrWhiteSpace(lines))
            {
                continue;
            }

            client.Addresses.Add(new ClientAddress
            {
                AddressType = Normalize(address.AddressType) ?? "Other",
                LinesRaw = lines,
                SortOrder = sortOrder++
            });
        }
    }

    private void ReplaceRelationships(Client client, IEnumerable<ClientRelationshipEditModel> relationships)
    {
        db.ClientRelationships.RemoveRange(client.Relationships);
        client.Relationships.Clear();

        foreach (var relationship in relationships)
        {
            var relationshipType = Normalize(relationship.RelationshipType);
            var name = Normalize(relationship.Name);
            var initials = Normalize(relationship.Initials);
            var gender = Normalize(relationship.Gender);
            var southAfricanIdNumber = Normalize(relationship.SouthAfricanIdNumber);
            var email = Normalize(relationship.Email);
            var homePhone = Normalize(relationship.HomePhone);
            var workPhone = Normalize(relationship.WorkPhone);
            var mobilePhone = Normalize(relationship.MobilePhone);
            var employer = Normalize(relationship.Employer);
            var occupation = Normalize(relationship.Occupation);
            var highestQualification = Normalize(relationship.HighestQualification);
            var pensionFundName = Normalize(relationship.PensionFundName);

            if (string.IsNullOrWhiteSpace(relationshipType) &&
                string.IsNullOrWhiteSpace(name) &&
                string.IsNullOrWhiteSpace(initials) &&
                string.IsNullOrWhiteSpace(gender) &&
                relationship.BirthDate is null &&
                string.IsNullOrWhiteSpace(southAfricanIdNumber) &&
                string.IsNullOrWhiteSpace(email) &&
                string.IsNullOrWhiteSpace(homePhone) &&
                string.IsNullOrWhiteSpace(workPhone) &&
                string.IsNullOrWhiteSpace(mobilePhone) &&
                string.IsNullOrWhiteSpace(employer) &&
                string.IsNullOrWhiteSpace(occupation) &&
                string.IsNullOrWhiteSpace(highestQualification) &&
                relationship.GrossMonthlySalary is null &&
                relationship.GrossAnnualSalary is null &&
                relationship.YearlyBonus is null &&
                relationship.OtherIncome is null &&
                string.IsNullOrWhiteSpace(pensionFundName) &&
                relationship.EmployerPensionContributionAmount is null &&
                relationship.EmployerPensionContributionPercent is null)
            {
                continue;
            }

            client.Relationships.Add(new ClientRelationship
            {
                RelationshipType = relationshipType ?? "Other",
                LegacyRelatedClientId = relationship.LegacyRelatedClientId,
                Name = name,
                Initials = initials,
                Gender = gender,
                BirthDate = relationship.BirthDate,
                SouthAfricanIdNumber = southAfricanIdNumber,
                Email = email,
                HomePhone = homePhone,
                WorkPhone = workPhone,
                MobilePhone = mobilePhone,
                Employer = employer,
                Occupation = occupation,
                HighestQualification = highestQualification,
                GrossMonthlySalary = relationship.GrossMonthlySalary,
                GrossAnnualSalary = relationship.GrossAnnualSalary,
                YearlyBonus = relationship.YearlyBonus,
                OtherIncome = relationship.OtherIncome,
                PensionFundName = pensionFundName,
                EmployerPensionContributionAmount = relationship.EmployerPensionContributionAmount,
                EmployerPensionContributionPercent = relationship.EmployerPensionContributionPercent
            });
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Contains(string? value, string filter) =>
        value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    private static bool AdministratorsMatch(string accountAdministrator, string? valuationAdministrator) =>
        valuationAdministrator is not null &&
        (string.Equals(accountAdministrator, valuationAdministrator, StringComparison.OrdinalIgnoreCase) ||
         accountAdministrator.Contains(valuationAdministrator, StringComparison.OrdinalIgnoreCase) ||
         valuationAdministrator.Contains(accountAdministrator, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeAccountNumber(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string? NormalizeLookup(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ClientEditModel
{
    public int? Id { get; set; }
    public string? KanaanId { get; set; }
    public string? Title { get; set; }
    public string? Initials { get; set; }
    public string? FullName { get; set; }
    public string? SurnameOrEntityName { get; set; }
    public string? DisplayName { get; set; }
    public string? Language { get; set; }
    public string? ClientFolder { get; set; }
    public string ClientCategory { get; set; } = ClientCategories.NaturalPerson;
    public string? ClientCategorySource { get; set; }
    public string? ClientCategoryReason { get; set; }
    public DateTime? ClientCategoryUpdatedAtUtc { get; set; }
    public string? ClientCategoryUpdatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SouthAfricanIdNumber { get; set; }
    public string? Gender { get; set; }
    public string? MaritalStatus { get; set; }
    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? HighestQualification { get; set; }
    public int? NumberOfDependents { get; set; }
    public bool? Smoker { get; set; }
    public decimal? WorkdayTravelPercent { get; set; }
    public string? FamilyDetailRaw { get; set; }
    public string? Employer { get; set; }
    public string? Occupation { get; set; }
    public decimal? GrossMonthlySalary { get; set; }
    public decimal? MonthlyExpenses { get; set; }
    public int? RetirementAge { get; set; }
    public string? PensionFundName { get; set; }
    public string? RepresentativeName { get; set; }
    public string? OtherGoalsRaw { get; set; }
    public string? BankDetailRaw { get; set; }
    public string? WillDetailRaw { get; set; }
    public string? OtherDetailsRaw { get; set; }
    public List<ClientContactPointEditModel> ContactPoints { get; set; } = [];
    public List<ClientAddressEditModel> Addresses { get; set; } = [];
    public List<ClientRelationshipEditModel> Relationships { get; set; } = [];

    public static ClientEditModel FromClient(Client client)
    {
        return new ClientEditModel
        {
            Id = client.Id,
            KanaanId = client.KanaanId,
            Title = client.Title,
            Initials = client.Initials,
            FullName = client.FullName,
            SurnameOrEntityName = client.SurnameOrEntityName,
            DisplayName = client.DisplayName,
            Language = client.Language,
            ClientFolder = client.ClientFolder,
            ClientCategory = client.ClientCategory,
            ClientCategorySource = client.ClientCategorySource,
            ClientCategoryReason = client.ClientCategoryReason,
            ClientCategoryUpdatedAtUtc = client.ClientCategoryUpdatedAtUtc,
            ClientCategoryUpdatedBy = client.ClientCategoryUpdatedBy,
            IsActive = client.IsActive,
            SouthAfricanIdNumber = client.PersonalProfile?.SouthAfricanIdNumber,
            Gender = client.PersonalProfile?.Gender,
            MaritalStatus = client.PersonalProfile?.MaritalStatus,
            TaxNumber = client.PersonalProfile?.TaxNumber,
            TaxOffice = client.PersonalProfile?.TaxOffice,
            HighestQualification = client.PersonalProfile?.HighestQualification,
            NumberOfDependents = client.PersonalProfile?.NumberOfDependents,
            Smoker = client.PersonalProfile?.Smoker,
            WorkdayTravelPercent = client.PersonalProfile?.WorkdayTravelPercent,
            FamilyDetailRaw = client.PersonalProfile?.FamilyDetailRaw,
            Employer = client.FinancialProfile?.Employer,
            Occupation = client.FinancialProfile?.Occupation,
            GrossMonthlySalary = client.FinancialProfile?.GrossMonthlySalary,
            MonthlyExpenses = client.FinancialProfile?.MonthlyExpenses,
            RetirementAge = client.FinancialProfile?.RetirementAge,
            PensionFundName = client.FinancialProfile?.PensionFundName,
            RepresentativeName = client.FinancialProfile?.RepresentativeName,
            OtherGoalsRaw = client.FinancialProfile?.OtherGoalsRaw,
            BankDetailRaw = client.FinancialProfile?.BankDetailRaw,
            WillDetailRaw = client.FinancialProfile?.WillDetailRaw,
            OtherDetailsRaw = client.FinancialProfile?.OtherDetailsRaw,
            ContactPoints = client.ContactPoints
                .OrderBy(contact => contact.SortOrder)
                .Select(contact => new ClientContactPointEditModel
                {
                    ContactType = contact.ContactType,
                    Label = contact.Label,
                    Value = contact.Value,
                    IsPrimary = contact.IsPrimary
                })
                .ToList(),
            Addresses = client.Addresses
                .OrderBy(address => address.SortOrder)
                .Select(address => new ClientAddressEditModel
                {
                    AddressType = address.AddressType,
                    LinesRaw = address.LinesRaw
                })
                .ToList(),
            Relationships = client.Relationships
                .OrderBy(relationship => relationship.RelationshipType)
                .ThenBy(relationship => relationship.Name)
                .Select(relationship => new ClientRelationshipEditModel
                {
                    RelationshipType = relationship.RelationshipType,
                    LegacyRelatedClientId = relationship.LegacyRelatedClientId,
                    Name = relationship.Name,
                    Initials = relationship.Initials,
                    Gender = relationship.Gender,
                    BirthDate = relationship.BirthDate,
                    SouthAfricanIdNumber = relationship.SouthAfricanIdNumber,
                    Email = relationship.Email,
                    HomePhone = relationship.HomePhone,
                    WorkPhone = relationship.WorkPhone,
                    MobilePhone = relationship.MobilePhone,
                    Employer = relationship.Employer,
                    Occupation = relationship.Occupation,
                    HighestQualification = relationship.HighestQualification,
                    GrossMonthlySalary = relationship.GrossMonthlySalary,
                    GrossAnnualSalary = relationship.GrossAnnualSalary,
                    YearlyBonus = relationship.YearlyBonus,
                    OtherIncome = relationship.OtherIncome,
                    PensionFundName = relationship.PensionFundName,
                    EmployerPensionContributionAmount = relationship.EmployerPensionContributionAmount,
                    EmployerPensionContributionPercent = relationship.EmployerPensionContributionPercent
                })
                .ToList()
        };
    }
}

public sealed class ClientContactPointEditModel
{
    public string ContactType { get; set; } = "Email";
    public string? Label { get; set; }
    public string? Value { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class ClientAddressEditModel
{
    public string AddressType { get; set; } = "Physical";
    public string? LinesRaw { get; set; }
}

public sealed class ClientRelationshipEditModel
{
    public string RelationshipType { get; set; } = "Spouse";
    public int? LegacyRelatedClientId { get; set; }
    public string? Name { get; set; }
    public string? Initials { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? SouthAfricanIdNumber { get; set; }
    public string? Email { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? Employer { get; set; }
    public string? Occupation { get; set; }
    public string? HighestQualification { get; set; }
    public decimal? GrossMonthlySalary { get; set; }
    public decimal? GrossAnnualSalary { get; set; }
    public decimal? YearlyBonus { get; set; }
    public decimal? OtherIncome { get; set; }
    public string? PensionFundName { get; set; }
    public decimal? EmployerPensionContributionAmount { get; set; }
    public decimal? EmployerPensionContributionPercent { get; set; }
}

public sealed class ClientNoteEditModel
{
    public int ClientId { get; set; }
    public int? Id { get; set; }
    public DateOnly? NoteDate { get; set; }
    public string? Title { get; set; }
    public string? Details { get; set; }

    public static ClientNoteEditModel FromNote(ClientNote note) => new()
    {
        ClientId = note.ClientId,
        Id = note.Id,
        NoteDate = note.NoteDate,
        Title = note.Title,
        Details = note.Details
    };
}

public sealed class ClientKycPolicyEditModel
{
    public int ClientId { get; set; }
    public int? Id { get; set; }
    public string? MainClassName { get; set; }
    public string? SubClassName { get; set; }
    public string? SubClassExtra { get; set; }
    public string? Administrator { get; set; }
    public string? Product { get; set; }
    public string? PolicyNumber { get; set; }
    public string? Description { get; set; }
    public string? Fund { get; set; }
    public decimal? Value { get; set; }
    public decimal? LifeCover { get; set; }
    public decimal? DisabilityCover { get; set; }
    public decimal? DreadDiseaseCover { get; set; }
    public decimal? CompulsoryContributionValue { get; set; }
    public decimal? VoluntaryContributionValue { get; set; }
    public decimal? Debt { get; set; }
    public decimal? MonthlyPremium { get; set; }
    public decimal? OnceOffPremium { get; set; }
    public decimal? MonthlyIncome { get; set; }
    public decimal? CapitalAdequacyRatioPercent { get; set; }
    public decimal? TaxPercent { get; set; }
    public bool IncludeInCalculations { get; set; }
    public bool SurrenderOrLiquidate { get; set; }
    public bool IsRetirementAnnuity { get; set; }
    public bool IsPreservationFund { get; set; }
    public bool IsRetrenchmentPackage { get; set; }
    public bool IsQuote { get; set; }
    public DateTime? ValuationDate { get; set; }

    public static ClientKycPolicyEditModel FromPolicy(ClientKycPolicy policy) => new()
    {
        ClientId = policy.ClientId,
        Id = policy.Id,
        MainClassName = policy.MainClassName,
        SubClassName = policy.SubClassName,
        SubClassExtra = policy.SubClassExtra,
        Administrator = policy.Administrator,
        Product = policy.Product,
        PolicyNumber = policy.PolicyNumber,
        Description = policy.Description,
        Fund = policy.Fund,
        Value = policy.Value,
        LifeCover = policy.LifeCover,
        DisabilityCover = policy.DisabilityCover,
        DreadDiseaseCover = policy.DreadDiseaseCover,
        CompulsoryContributionValue = policy.CompulsoryContributionValue,
        VoluntaryContributionValue = policy.VoluntaryContributionValue,
        Debt = policy.Debt,
        MonthlyPremium = policy.MonthlyPremium,
        OnceOffPremium = policy.OnceOffPremium,
        MonthlyIncome = policy.MonthlyIncome,
        CapitalAdequacyRatioPercent = policy.CapitalAdequacyRatioPercent,
        TaxPercent = policy.TaxPercent,
        IncludeInCalculations = policy.IncludeInCalculations,
        SurrenderOrLiquidate = policy.SurrenderOrLiquidate,
        IsRetirementAnnuity = policy.IsRetirementAnnuity,
        IsPreservationFund = policy.IsPreservationFund,
        IsRetrenchmentPackage = policy.IsRetrenchmentPackage,
        IsQuote = policy.IsQuote,
        ValuationDate = policy.ValuationDate
    };
}

public sealed record KycReferenceOptionsModel(
    IReadOnlyList<KycMainClassOption> MainClasses,
    IReadOnlyList<KycSubClassOption> SubClasses,
    IReadOnlyList<string> Administrators,
    IReadOnlyList<string> Funds);

public sealed record KycMainClassOption(string Name, int? LegacyMainClassId);

public sealed record KycSubClassOption(string Name, string MainClassName, int? LegacySubClassId);

public sealed class ClientInvestmentAccountEditModel
{
    public int ClientId { get; set; }
    public int? Id { get; set; }
    public DateOnly? InvestmentDate { get; set; }
    public DateOnly? SurrenderDate { get; set; }
    public string? Administrator { get; set; }
    public string? AccountNumber { get; set; }
    public string? ProductName { get; set; }
    public string? ProductType { get; set; }
    public string? FundName { get; set; }
    public bool IsLinkedHead { get; set; }
    public bool IsFinal { get; set; }

    public static ClientInvestmentAccountEditModel FromAccount(ClientInvestmentAccount account) => new()
    {
        ClientId = account.ClientId,
        Id = account.Id,
        InvestmentDate = account.InvestmentDate,
        SurrenderDate = account.SurrenderDate,
        Administrator = account.Administrator,
        AccountNumber = account.AccountNumber,
        ProductName = account.ProductName,
        ProductType = account.ProductType,
        FundName = account.FundName,
        IsLinkedHead = account.IsLinkedHead,
        IsFinal = account.IsFinal
    };
}

public sealed record InvestmentReferenceOptionsModel(
    IReadOnlyList<string> Administrators,
    IReadOnlyList<string> ProductTypes,
    IReadOnlyList<string> Funds);

public sealed class ClientInvestmentTransactionEditModel
{
    public int ClientId { get; set; }
    public int ClientInvestmentAccountId { get; set; }
    public int? Id { get; set; }
    public DateOnly? TransactionDate { get; set; }
    public string? Description { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? InvestmentAmountForeign { get; set; }
    public decimal? InvestmentAmountZar { get; set; }
    public decimal? WithdrawalAmountForeign { get; set; }
    public decimal? WithdrawalAmountZar { get; set; }
    public string? InvestmentFrequency { get; set; }
    public decimal? AnnualIncreasePercent { get; set; }
    public decimal? BalanceForeign { get; set; }
    public decimal? BalanceZar { get; set; }
    public bool IsFinal { get; set; }

    public static ClientInvestmentTransactionEditModel FromTransaction(int clientId, ClientInvestmentTransaction transaction) => new()
    {
        ClientId = clientId,
        ClientInvestmentAccountId = transaction.ClientInvestmentAccountId,
        Id = transaction.Id,
        TransactionDate = transaction.TransactionDate,
        Description = transaction.Description,
        ExchangeRate = transaction.ExchangeRate,
        InvestmentAmountForeign = transaction.InvestmentAmountForeign,
        InvestmentAmountZar = transaction.InvestmentAmountZar,
        WithdrawalAmountForeign = transaction.WithdrawalAmountForeign,
        WithdrawalAmountZar = transaction.WithdrawalAmountZar,
        InvestmentFrequency = transaction.InvestmentFrequency,
        AnnualIncreasePercent = transaction.AnnualIncreasePercent,
        BalanceForeign = transaction.BalanceForeign,
        BalanceZar = transaction.BalanceZar,
        IsFinal = transaction.IsFinal
    };
}

public sealed class ClientFundSummaryModel
{
    public int ClientId { get; set; }
    public string ClientDisplayName { get; set; } = string.Empty;
    public string? Filter { get; set; }
    public List<ClientFundSummaryRowModel> Rows { get; set; } = [];
    public decimal? TotalCurrentValueZar => Sum(Rows.Select(row => row.CurrentValueZar));
    public decimal? TotalCurrentValueForeign => Sum(Rows.Select(row => row.CurrentValueForeign));
    public int MatchedValuationCount => Rows.Count(row => row.Source == "Fund valuation");
    public int HistoryFallbackCount => Rows.Count(row => row.Source == "History balance");
    public int UnmatchedValuationCount => Rows.Count(row => row.Source == "Unmatched fund valuation");
    public DateOnly? LatestValueDate => Rows
        .Where(row => row.CurrentValueDate.HasValue)
        .OrderByDescending(row => row.CurrentValueDate)
        .Select(row => row.CurrentValueDate)
        .FirstOrDefault();

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var captured = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return captured.Count == 0 ? null : captured.Sum();
    }
}

public sealed class ClientFundSummaryRowModel
{
    public int? AccountId { get; set; }
    public string? AccountNumber { get; set; }
    public string? Administrator { get; set; }
    public string? ProductName { get; set; }
    public string? ProductType { get; set; }
    public string? FundName { get; set; }
    public decimal? CurrentValueZar { get; set; }
    public decimal? CurrentValueForeign { get; set; }
    public DateOnly? CurrentValueDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
}

public sealed class ClientKycRecommendationEditModel
{
    public int ClientId { get; set; }
    public int? Id { get; set; }
    public int? ClientKycPolicyId { get; set; }
    public string? RecommendationType { get; set; }
    public string? Status { get; set; }
    public DateOnly? RecommendationDate { get; set; }
    public string? Details { get; set; }
    public string? Outcome { get; set; }

    public static ClientKycRecommendationEditModel FromRecommendation(ClientKycRecommendation recommendation) => new()
    {
        ClientId = recommendation.ClientId,
        Id = recommendation.Id,
        ClientKycPolicyId = recommendation.ClientKycPolicyId,
        RecommendationType = recommendation.RecommendationType,
        Status = recommendation.Status,
        RecommendationDate = recommendation.RecommendationDate,
        Details = recommendation.Details,
        Outcome = recommendation.Outcome
    };
}

public sealed class ClientTransferTargetModel
{
    public int ClientId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string SurnameOrEntityName { get; set; } = string.Empty;
    public string? KanaanId { get; set; }
}

public sealed class KycTransferModel
{
    public int SourceClientId { get; set; }
    public int TargetClientId { get; set; }
    public KycTransferOperation Operation { get; set; } = KycTransferOperation.Copy;
    public List<int> PolicyIds { get; set; } = [];
    public List<int> RecommendationIds { get; set; } = [];
}

public enum KycTransferOperation
{
    Copy,
    Transfer
}
