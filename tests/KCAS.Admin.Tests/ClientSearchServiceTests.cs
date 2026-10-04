using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientSearchServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Client_register_hides_excluded_clients_unless_administrator_view_is_requested()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = new ClientSearchService(db);
        var client = new Client
        {
            DisplayName = $"Hidden Register Client {Guid.NewGuid():N}",
            SurnameOrEntityName = "Hidden",
            ExcludeFromComplianceLists = true
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        Assert.DoesNotContain(await service.SearchAsync(client.DisplayName), item => item.Id == client.Id);
        Assert.Contains(
            await service.SearchAsync(client.DisplayName, includeExcludedClients: true),
            item => item.Id == client.Id);
    }

    [Fact]
    public async Task Search_finds_imported_clients_by_legacy_identity_and_contact_details()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = new ClientSearchService(db);

        var client = new Client
        {
            LegacyClientId = await NextLegacyIdAsync(db.Clients.Select(item => item.LegacyClientId)),
            KanaanId = "123",
            SurnameOrEntityName = "Botha",
            DisplayName = "Botha, C",
            PersonalProfile = new ClientPersonalProfile { SouthAfricanIdNumber = "7901015009088" },
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "client@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0820000000", IsPrimary = true, SortOrder = 20 }
            }
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        Assert.Contains(await service.SearchAsync("123"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("Botha"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("7901015009088"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("client@example.test"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("0820000000"), result => result.Id == client.Id);
    }

    [Fact]
    public async Task Search_supports_column_filters_and_sorting()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = new ClientSearchService(db);
        var legacyId = await NextLegacyIdAsync(db.Clients.Select(item => item.LegacyClientId));
        var searchToken = Guid.NewGuid().ToString("N");

        var zulu = new Client
        {
            LegacyClientId = legacyId,
            KanaanId = "900",
            SurnameOrEntityName = "Zulu",
            DisplayName = $"Zulu, Z {searchToken}",
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "zulu@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0830000000", IsPrimary = true, SortOrder = 20 }
            }
        };
        var alpha = new Client
        {
            LegacyClientId = legacyId + 1,
            KanaanId = "100",
            SurnameOrEntityName = "Alpha",
            DisplayName = $"Alpha, A {searchToken}",
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "alpha@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0840000000", IsPrimary = true, SortOrder = 20 }
            }
        };
        db.Clients.AddRange(zulu, alpha);
        await db.SaveChangesAsync();

        var filtered = await service.SearchAsync(new ClientSearchRequest(Email: "zulu@example.test"));
        Assert.Contains(filtered, result => result.Id == zulu.Id);
        Assert.DoesNotContain(filtered, result => result.Id == alpha.Id);

        var sorted = await service.SearchAsync(new ClientSearchRequest(Name: searchToken, SortColumn: "kanaanId", SortDescending: true));
        Assert.Contains(sorted, result => result.Id == zulu.Id);
        Assert.Contains(sorted, result => result.Id == alpha.Id);
        Assert.True(sorted.FindIndex(result => result.Id == zulu.Id) < sorted.FindIndex(result => result.Id == alpha.Id));
    }

    [Fact]
    public async Task Search_keeps_lifecycle_separate_from_investment_position()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = new ClientSearchService(db);
        var legacyId = await NextLegacyIdAsync(db.Clients.Select(item => item.LegacyClientId));

        var current = new Client
        {
            LegacyClientId = legacyId,
            KanaanId = "LIFECYCLE-CURRENT",
            SurnameOrEntityName = "Current Holdings",
            DisplayName = "Current Holdings",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            IsActive = true,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "INV-95001",
                    Administrator = "Test Platform"
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = await NextLegacyIdAsync(db.ClientFundValuations.Select(item => (int?)item.LegacyFundId)),
                    InvestmentUniqueNumber = "INV-95001",
                    Administrator = "Test Platform",
                    FundName = "Test Fund",
                    AmountZar = 125_000m
                }
            }
        };
        var historical = new Client
        {
            LegacyClientId = legacyId + 1,
            KanaanId = "LIFECYCLE-HISTORICAL",
            SurnameOrEntityName = "Historical Holdings",
            DisplayName = "Historical Holdings",
            LifecycleStatus = ClientLifecycleStatuses.Closed,
            IsActive = false,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "INV-95002",
                    Administrator = "Test Platform",
                    SurrenderDate = new DateOnly(2025, 1, 1)
                }
            }
        };
        var correction = new Client
        {
            LegacyClientId = legacyId + 2,
            KanaanId = "LIFECYCLE-CORRECTION",
            SurnameOrEntityName = "Correction Holdings",
            DisplayName = "Correction Holdings",
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed,
            IsActive = true,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "INV-95003",
                    Administrator = "Test Platform"
                }
            }
        };
        var noInvestments = new Client
        {
            LegacyClientId = legacyId + 3,
            KanaanId = "LIFECYCLE-NONE",
            SurnameOrEntityName = "No Holdings",
            DisplayName = "No Holdings",
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed,
            IsActive = true
        };
        db.Clients.AddRange(current, historical, correction, noInvestments);
        await db.SaveChangesAsync();

        var results = await service.SearchAsync(new ClientSearchRequest(Name: "Holdings"));

        var currentResult = Assert.Single(results, item => item.Id == current.Id);
        Assert.Equal(ClientLifecycleStatuses.Current, currentResult.LifecycleStatus);
        Assert.Equal("Current investments", currentResult.InvestmentPosition);
        Assert.True(currentResult.HasCurrentInvestments);
        Assert.Equal(125_000m, currentResult.TotalCurrentValueZar);

        var historicalResult = Assert.Single(results, item => item.Id == historical.Id);
        Assert.Equal(ClientLifecycleStatuses.Closed, historicalResult.LifecycleStatus);
        Assert.Equal("Historical investments only", historicalResult.InvestmentPosition);
        Assert.False(historicalResult.HasCurrentInvestments);

        var correctionResult = Assert.Single(results, item => item.Id == correction.Id);
        Assert.Equal("No current investments · correction needed", correctionResult.InvestmentPosition);
        Assert.Equal(1, correctionResult.InvestmentStatusCorrectionCount);

        var noInvestmentsResult = Assert.Single(results, item => item.Id == noInvestments.Id);
        Assert.Equal("No current investments", noInvestmentsResult.InvestmentPosition);
        Assert.False(noInvestmentsResult.HasCurrentInvestments);

        var closed = await service.SearchAsync(new ClientSearchRequest(
            Name: "Holdings",
            Status: ClientLifecycleStatuses.Closed));
        Assert.Contains(closed, item => item.Id == historical.Id);
        Assert.DoesNotContain(closed, item => item.Id == current.Id);

        var noCurrent = await service.SearchAsync(new ClientSearchRequest(
            Name: "Holdings",
            InvestmentPosition: ClientInvestmentPositionFilters.NoCurrent));
        Assert.DoesNotContain(noCurrent, item => item.Id == current.Id);
        Assert.Contains(noCurrent, item => item.Id == historical.Id);
        Assert.Contains(noCurrent, item => item.Id == correction.Id);
        Assert.Contains(noCurrent, item => item.Id == noInvestments.Id);

        var needsCorrection = await service.SearchAsync(new ClientSearchRequest(
            Name: "Holdings",
            InvestmentPosition: ClientInvestmentPositionFilters.NeedsCorrection));
        Assert.Contains(needsCorrection, item => item.Id == correction.Id);
        Assert.DoesNotContain(needsCorrection, item => item.Id == historical.Id);
    }

    [Fact]
    public async Task Client_can_load_imported_notes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var noteId = await NextLegacyIdAsync(db.ClientNotes.Select(item => item.LegacyClientNoteId));

        var client = new Client
        {
            LegacyClientId = await NextLegacyIdAsync(db.Clients.Select(item => item.LegacyClientId)),
            KanaanId = "503",
            SurnameOrEntityName = "Notes",
            DisplayName = "Notes Client"
        };
        client.Notes.Add(new ClientNote
        {
            LegacyClientNoteId = noteId,
            NoteDate = new DateOnly(2026, 5, 31),
            Title = "Imported note",
            Details = "Imported details",
            IsFinal = true,
            IsDeleted = false,
            PayloadJson = "{}"
        });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var clientId = client.Id;

        var loaded = await db.Clients
            .Include(client => client.Notes)
            .SingleAsync(client => client.Id == clientId);

        Assert.Contains(loaded.Notes, note => note.LegacyClientNoteId == noteId);
    }

    [Fact]
    public async Task Client_can_load_imported_kyc_policies()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var policyId = await NextLegacyIdAsync(db.ClientKycPolicies.Select(item => item.LegacyKycId));

        var client = new Client
        {
            LegacyClientId = await NextLegacyIdAsync(db.Clients.Select(item => item.LegacyClientId)),
            KanaanId = "504",
            SurnameOrEntityName = "Kyc",
            DisplayName = "Kyc Client"
        };
        client.KycPolicies.Add(new ClientKycPolicy
        {
            LegacyKycId = policyId,
            LegacyClientId = client.LegacyClientId,
            LegacyMainClassId = 6,
            MainClassName = "Other",
            LegacySubClassId = 29,
            SubClassName = "Life and Disability Cover",
            Administrator = "Discovery",
            Product = "Life & Disability",
            PolicyNumber = "POL-1",
            Value = 100000m,
            LifeCover = 100000m,
            DisabilityCover = 50000m,
            IncludeInCalculations = true,
            PayloadJson = "{}"
        });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var clientId = client.Id;

        var loaded = await db.Clients
            .Include(client => client.KycPolicies)
            .SingleAsync(client => client.Id == clientId);

        Assert.Contains(loaded.KycPolicies, policy => policy.LegacyKycId == policyId && policy.SubClassName == "Life and Disability Cover");
    }

    [Fact]
    public async Task Client_can_load_imported_investment_accounts_and_transactions()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var databaseTransaction = await db.Database.BeginTransactionAsync();
        var accountId = await NextLegacyIdAsync(db.ClientInvestmentAccounts.Select(item => item.LegacyInvestmentAccountId));
        var historyId = await NextLegacyIdAsync(db.ClientInvestmentTransactions.Select(item => item.LegacyInvestmentHistoryId));

        var client = new Client
        {
            LegacyClientId = await NextLegacyIdAsync(db.Clients.Select(item => item.LegacyClientId)),
            KanaanId = "505",
            SurnameOrEntityName = "Investments",
            DisplayName = "Investments Client"
        };
        client.InvestmentAccounts.Add(new ClientInvestmentAccount
        {
            LegacyInvestmentAccountId = accountId,
            LegacyClientId = client.LegacyClientId,
            Administrator = "Glacier",
            AccountNumber = "ACC-505",
            ProductName = "Retirement Annuity",
            ProductType = "Compulsory",
            FundName = "Stable SA",
            PayloadJson = "{}",
            Transactions =
            {
                new ClientInvestmentTransaction
                {
                    LegacyInvestmentHistoryId = historyId,
                    LegacyInvestmentAccountId = accountId,
                    TransactionDate = new DateOnly(2026, 5, 31),
                    Description = "Imported transaction",
                    InvestmentAmountZar = 1000m,
                    BalanceZar = 25000m,
                    IsFinal = true,
                    PayloadJson = "{}"
                }
            }
        });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var clientId = client.Id;

        var loaded = await db.Clients
            .Include(client => client.InvestmentAccounts)
                .ThenInclude(account => account.Transactions)
            .SingleAsync(client => client.Id == clientId);

        var account = Assert.Single(loaded.InvestmentAccounts);
        Assert.Equal(accountId, account.LegacyInvestmentAccountId);
        Assert.Contains(account.Transactions, transaction => transaction.LegacyInvestmentHistoryId == historyId);
    }

    private static async Task<int> NextLegacyIdAsync(IQueryable<int?> ids) => (await ids.MaxAsync() ?? 0) + 1;
}
