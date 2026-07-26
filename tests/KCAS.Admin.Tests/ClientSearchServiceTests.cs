using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientSearchServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Search_finds_imported_clients_by_legacy_identity_and_contact_details()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new ClientSearchService(db);

        var client = new Client
        {
            LegacyClientId = 500,
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
        var service = new ClientSearchService(db);

        db.Clients.Add(new Client
        {
            LegacyClientId = 501,
            KanaanId = "900",
            SurnameOrEntityName = "Zulu",
            DisplayName = "Zulu, Z",
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "zulu@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0830000000", IsPrimary = true, SortOrder = 20 }
            }
        });
        db.Clients.Add(new Client
        {
            LegacyClientId = 502,
            KanaanId = "100",
            SurnameOrEntityName = "Alpha",
            DisplayName = "Alpha, A",
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "alpha@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0840000000", IsPrimary = true, SortOrder = 20 }
            }
        });
        await db.SaveChangesAsync();

        var filtered = await service.SearchAsync(new ClientSearchRequest(Email: "zulu@example.test"));
        Assert.Contains(filtered, result => result.KanaanId == "900");
        Assert.DoesNotContain(filtered, result => result.KanaanId == "100");

        var sorted = await service.SearchAsync(new ClientSearchRequest(SortColumn: "kanaanId", SortDescending: true));
        Assert.True(sorted.FindIndex(result => result.KanaanId == "900") < sorted.FindIndex(result => result.KanaanId == "100"));
    }

    [Fact]
    public async Task Search_keeps_lifecycle_separate_from_investment_position()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new ClientSearchService(db);

        var current = new Client
        {
            LegacyClientId = 95001,
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
                    LegacyFundId = 95001,
                    InvestmentUniqueNumber = "INV-95001",
                    Administrator = "Test Platform",
                    FundName = "Test Fund",
                    AmountZar = 125_000m
                }
            }
        };
        var historical = new Client
        {
            LegacyClientId = 95002,
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
            LegacyClientId = 95003,
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
            LegacyClientId = 95004,
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

        var client = new Client
        {
            LegacyClientId = 503,
            KanaanId = "503",
            SurnameOrEntityName = "Notes",
            DisplayName = "Notes Client"
        };
        client.Notes.Add(new ClientNote
        {
            LegacyClientNoteId = 9001,
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

        Assert.Contains(loaded.Notes, note => note.LegacyClientNoteId == 9001);
    }

    [Fact]
    public async Task Client_can_load_imported_kyc_policies()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var client = new Client
        {
            LegacyClientId = 504,
            KanaanId = "504",
            SurnameOrEntityName = "Kyc",
            DisplayName = "Kyc Client"
        };
        client.KycPolicies.Add(new ClientKycPolicy
        {
            LegacyKycId = 9101,
            LegacyClientId = 504,
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

        Assert.Contains(loaded.KycPolicies, policy => policy.LegacyKycId == 9101 && policy.SubClassName == "Life and Disability Cover");
    }

    [Fact]
    public async Task Client_can_load_imported_investment_accounts_and_transactions()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var client = new Client
        {
            LegacyClientId = 505,
            KanaanId = "505",
            SurnameOrEntityName = "Investments",
            DisplayName = "Investments Client"
        };
        client.InvestmentAccounts.Add(new ClientInvestmentAccount
        {
            LegacyInvestmentAccountId = 9201,
            LegacyClientId = 505,
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
                    LegacyInvestmentHistoryId = 9301,
                    LegacyInvestmentAccountId = 9201,
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
        Assert.Equal(9201, account.LegacyInvestmentAccountId);
        Assert.Contains(account.Transactions, transaction => transaction.LegacyInvestmentHistoryId == 9301);
    }
}
