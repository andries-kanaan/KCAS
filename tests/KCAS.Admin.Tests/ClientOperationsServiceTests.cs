using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientOperationsServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task SaveClientAsync_creates_and_updates_normalized_client_details()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-1",
            FullName = "Operational",
            SurnameOrEntityName = "Client",
            DisplayName = "Operational Client",
            SouthAfricanIdNumber = "8001015009087",
            Employer = "Kanaan",
            ContactPoints =
            [
                new() { ContactType = "Email", Value = "ops@example.com" },
                new() { ContactType = "Mobile", Value = "0820000000" }
            ],
            Addresses =
            [
                new() { AddressType = "Physical", LinesRaw = "1 Main Road" }
            ],
            Relationships =
            [
                new()
                {
                    RelationshipType = "Spouse",
                    Name = "Spouse Client",
                    LegacyRelatedClientId = 43,
                    Email = "spouse@example.com",
                    GrossMonthlySalary = 20000m
                },
                new()
                {
                    RelationshipType = "FamilyContact",
                    Name = "Emergency Contact",
                    MobilePhone = "0830000000"
                },
                new()
                {
                    RelationshipType = "",
                    Name = " "
                }
            ]
        });

        var editModel = await service.LoadClientAsync(clientId);
        editModel.DisplayName = "Updated Operational Client";
        editModel.ContactPoints[0].Value = "updated@example.com";
        Assert.Contains(editModel.Relationships, relationship =>
            relationship.RelationshipType == "Spouse" &&
            relationship.LegacyRelatedClientId == 43 &&
            relationship.Name == "Spouse Client");

        var spouse = editModel.Relationships.Single(relationship => relationship.RelationshipType == "Spouse");
        spouse.Name = "Updated Spouse";
        spouse.Email = "updated-spouse@example.com";
        editModel.Relationships.RemoveAll(relationship => relationship.RelationshipType == "FamilyContact");
        editModel.Relationships.Add(new ClientRelationshipEditModel
        {
            RelationshipType = "Child",
            Name = "Child Client",
            BirthDate = new DateTime(2010, 1, 2),
            SouthAfricanIdNumber = "1001020000000"
        });
        await service.SaveClientAsync(editModel);

        var saved = await db.Clients
            .AsNoTracking()
            .Include(client => client.PersonalProfile)
            .Include(client => client.FinancialProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.Addresses)
            .Include(client => client.Relationships)
            .SingleAsync(client => client.Id == clientId);

        Assert.Equal("Updated Operational Client", saved.DisplayName);
        Assert.Equal("OPS-1", saved.KanaanId);
        Assert.Null(saved.LegacyClientId);
        Assert.Equal("8001015009087", saved.PersonalProfile?.SouthAfricanIdNumber);
        Assert.Equal("Kanaan", saved.FinancialProfile?.Employer);
        Assert.Contains(saved.ContactPoints, contact => contact.Value == "updated@example.com" && contact.IsPrimary);
        Assert.Contains(saved.Addresses, address => address.AddressType == "Physical");
        Assert.Contains(saved.Relationships, relationship =>
            relationship.RelationshipType == "Spouse" &&
            relationship.LegacyRelatedClientId == 43 &&
            relationship.Name == "Updated Spouse" &&
            relationship.Email == "updated-spouse@example.com");
        Assert.Contains(saved.Relationships, relationship =>
            relationship.RelationshipType == "Child" &&
            relationship.BirthDate == new DateTime(2010, 1, 2) &&
            relationship.SouthAfricanIdNumber == "1001020000000");
        Assert.DoesNotContain(saved.Relationships, relationship => relationship.RelationshipType == "FamilyContact");
        Assert.Equal(2, saved.Relationships.Count);
    }

    [Fact]
    public async Task SaveClientAsync_generates_kanaan_id_for_native_clients()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstClientId = await service.SaveClientAsync(new ClientEditModel
        {
            SurnameOrEntityName = "Generated",
            DisplayName = "Generated Client"
        });

        var secondClientId = await service.SaveClientAsync(new ClientEditModel
        {
            SurnameOrEntityName = "Generated Two",
            DisplayName = "Generated Client Two"
        });

        var saved = await db.Clients
            .AsNoTracking()
            .Where(client => client.Id == firstClientId || client.Id == secondClientId)
            .OrderBy(client => client.Id)
            .ToListAsync();

        Assert.Equal(2, saved.Count);
        Assert.All(saved, client =>
        {
            Assert.StartsWith("KCAS-", client.KanaanId);
            Assert.Null(client.LegacyClientId);
        });
        Assert.NotEqual(saved[0].KanaanId, saved[1].KanaanId);
    }

    [Fact]
    public async Task SaveClientAsync_allows_shared_kanaan_id_for_family_units()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstClientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "FAMILY-001",
            SurnameOrEntityName = "Original",
            DisplayName = "Original Client"
        });

        var secondClientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "FAMILY-001",
            SurnameOrEntityName = "Spouse",
            DisplayName = "Spouse Client"
        });

        var saved = await db.Clients
            .AsNoTracking()
            .Where(client => client.Id == firstClientId || client.Id == secondClientId)
            .ToListAsync();

        Assert.Equal(2, saved.Count);
        Assert.All(saved, client => Assert.Equal("FAMILY-001", client.KanaanId));
    }

    [Fact]
    public async Task Notes_can_be_created_edited_finalized_and_soft_deleted_without_legacy_id()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-2",
            SurnameOrEntityName = "Notes",
            DisplayName = "Notes Client"
        });

        var noteId = await service.SaveNoteAsync(new ClientNoteEditModel
        {
            ClientId = clientId,
            NoteDate = new DateOnly(2026, 5, 31),
            Title = "Draft note",
            Details = "Initial details"
        }, "tester");

        var noteModel = await service.LoadNoteAsync(clientId, noteId);
        noteModel.Details = "Updated details";
        await service.SaveNoteAsync(noteModel, "tester");
        await service.FinalizeNoteAsync(clientId, noteId, "tester");
        await service.DeleteNoteAsync(clientId, noteId, "tester");

        var saved = await db.ClientNotes.AsNoTracking().SingleAsync(note => note.Id == noteId);
        Assert.Null(saved.LegacyClientNoteId);
        Assert.Equal("Updated details", saved.Details);
        Assert.True(saved.IsFinal);
        Assert.True(saved.IsDeleted);
        Assert.Equal("tester", saved.UpdatedBy);
    }

    [Fact]
    public async Task Kyc_policies_can_be_created_edited_and_deleted_without_legacy_id()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-KYC-1",
            SurnameOrEntityName = "Kyc",
            DisplayName = "Kyc Client"
        });

        var policyId = await service.SaveKycPolicyAsync(new ClientKycPolicyEditModel
        {
            ClientId = clientId,
            MainClassName = "Risk",
            SubClassName = "Life and Disability Cover",
            Administrator = "KCAS Provider",
            Product = "Life Plan",
            PolicyNumber = "KYC-001",
            LifeCover = 500000m,
            DisabilityCover = 250000m,
            MonthlyPremium = 1250m,
            IncludeInCalculations = true
        }, "tester");

        var model = await service.LoadKycPolicyAsync(clientId, policyId);
        model.MonthlyPremium = 1300m;
        model.IsQuote = true;
        await service.SaveKycPolicyAsync(model, "tester");

        var saved = await db.ClientKycPolicies.AsNoTracking().SingleAsync(policy => policy.Id == policyId);
        Assert.Null(saved.LegacyKycId);
        Assert.Equal("OPS-KYC-1", saved.KanaanId);
        Assert.Equal("Life and Disability Cover", saved.SubClassName);
        Assert.Equal(1300m, saved.MonthlyPremium);
        Assert.True(saved.IncludeInCalculations);
        Assert.True(saved.IsQuote);
        Assert.Equal("tester", saved.OpenedBy);
        Assert.Equal("tester", saved.UpdatedBy);

        await service.DeleteKycPolicyAsync(clientId, policyId);

        Assert.False(await db.ClientKycPolicies.AnyAsync(policy => policy.Id == policyId));
    }

    [Fact]
    public async Task Imported_kyc_policies_can_be_edited_and_deleted_during_development()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-KYC-2",
            SurnameOrEntityName = "Imported Kyc",
            DisplayName = "Imported Kyc Client"
        });

        var imported = new ClientKycPolicy
        {
            ClientId = clientId,
            LegacyKycId = 99001,
            MainClassName = "Risk",
            SubClassName = "Imported policy",
            Product = "Legacy product",
            PayloadJson = "{}",
            ImportedAtUtc = DateTime.UtcNow
        };
        db.ClientKycPolicies.Add(imported);
        await db.SaveChangesAsync();

        var model = await service.LoadKycPolicyAsync(clientId, imported.Id);
        model.Product = "Updated imported product";
        model.Value = 150000m;
        await service.SaveKycPolicyAsync(model, "tester");

        var saved = await db.ClientKycPolicies.AsNoTracking().SingleAsync(policy => policy.Id == imported.Id);
        Assert.Equal(99001, saved.LegacyKycId);
        Assert.Equal("Updated imported product", saved.Product);
        Assert.Equal(150000m, saved.Value);
        Assert.Equal("tester", saved.UpdatedBy);

        await service.DeleteKycPolicyAsync(clientId, imported.Id);

        Assert.False(await db.ClientKycPolicies.AnyAsync(policy => policy.Id == imported.Id));
    }

    [Fact]
    public async Task Investment_accounts_and_transactions_can_be_managed_without_legacy_ids()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-INV-1",
            SurnameOrEntityName = "Investments",
            DisplayName = "Investments Client"
        });

        var accountId = await service.SaveInvestmentAccountAsync(new ClientInvestmentAccountEditModel
        {
            ClientId = clientId,
            Administrator = "Admin Co",
            AccountNumber = "ACC-001",
            ProductName = "Platform",
            ProductType = "Unit trust",
            FundName = "Balanced Fund",
            InvestmentDate = new DateOnly(2026, 1, 1)
        }, "tester");

        var transactionId = await service.SaveInvestmentTransactionAsync(new ClientInvestmentTransactionEditModel
        {
            ClientId = clientId,
            ClientInvestmentAccountId = accountId,
            TransactionDate = new DateOnly(2026, 2, 1),
            Description = "Initial contribution",
            InvestmentAmountZar = 1000m,
            BalanceZar = 1000m
        }, "tester");

        var transactionModel = await service.LoadInvestmentTransactionAsync(clientId, accountId, transactionId);
        transactionModel.BalanceZar = 1250m;
        await service.SaveInvestmentTransactionAsync(transactionModel, "tester");
        await service.FinalizeInvestmentTransactionAsync(clientId, accountId, transactionId, "tester");
        await service.DeleteInvestmentTransactionAsync(clientId, accountId, transactionId, "tester");

        var account = await db.ClientInvestmentAccounts.AsNoTracking().SingleAsync(account => account.Id == accountId);
        var transaction = await db.ClientInvestmentTransactions.AsNoTracking().SingleAsync(transaction => transaction.Id == transactionId);

        Assert.Null(account.LegacyInvestmentAccountId);
        Assert.Equal("ACC-001", account.AccountNumber);
        Assert.Null(transaction.LegacyInvestmentHistoryId);
        Assert.Equal(1250m, transaction.BalanceZar);
        Assert.True(transaction.IsFinal);
        Assert.True(transaction.IsDeleted);
        Assert.Equal("tester", transaction.UpdatedBy);
    }

    [Fact]
    public async Task Fund_summary_groups_matched_history_fallback_and_unmatched_values()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-FUND-1",
            SurnameOrEntityName = "Funds",
            DisplayName = "Funds Client"
        });

        var matchedAccount = new ClientInvestmentAccount
        {
            ClientId = clientId,
            AccountNumber = "MATCH-1",
            Administrator = "Admin",
            ProductName = "Product",
            PayloadJson = "{}"
        };
        var fallbackAccount = new ClientInvestmentAccount
        {
            ClientId = clientId,
            AccountNumber = "FALLBACK-1",
            Administrator = "Admin",
            ProductName = "Product",
            PayloadJson = "{}",
            Transactions =
            {
                new ClientInvestmentTransaction
                {
                    TransactionDate = new DateOnly(2026, 3, 1),
                    BalanceZar = 200m,
                    PayloadJson = "{}"
                }
            }
        };
        db.ClientInvestmentAccounts.AddRange(matchedAccount, fallbackAccount);
        db.ClientFundValuations.AddRange(
            new ClientFundValuation
            {
                ClientId = clientId,
                LegacyFundId = 900001,
                InvestmentUniqueNumber = "MATCH-1",
                Administrator = "Admin",
                FundName = "Alpha Fund",
                AmountZar = 300m,
                ValuationDate = new DateOnly(2026, 4, 1)
            },
            new ClientFundValuation
            {
                ClientId = clientId,
                LegacyFundId = 900002,
                InvestmentUniqueNumber = "UNMATCHED-1",
                Administrator = "Other",
                FundName = "Unmatched Fund",
                AmountZar = 400m,
                ValuationDate = new DateOnly(2026, 4, 2)
            });
        await db.SaveChangesAsync();

        var summary = await service.LoadFundSummaryAsync(clientId);

        Assert.Equal(3, summary.Rows.Count);
        Assert.Equal(900m, summary.TotalCurrentValueZar);
        Assert.Equal(1, summary.MatchedValuationCount);
        Assert.Equal(1, summary.HistoryFallbackCount);
        Assert.Equal(1, summary.UnmatchedValuationCount);
        Assert.Contains(summary.Rows, row => row.Source == "Fund valuation" && row.CurrentValueZar == 300m);
        Assert.Contains(summary.Rows, row => row.Source == "History balance" && row.CurrentValueZar == 200m);
        Assert.Contains(summary.Rows, row => row.Source == "Unmatched fund valuation" && row.CurrentValueZar == 400m);

        var filtered = await service.LoadFundSummaryAsync(clientId, "Alpha");
        Assert.Single(filtered.Rows);
    }

    [Fact]
    public async Task Kyc_recommendations_can_be_managed_and_copied_or_transferred()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sourceClientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "FAMILY-KYC-1",
            SurnameOrEntityName = "Source",
            DisplayName = "Source Client"
        });
        var targetClientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "FAMILY-KYC-1",
            SurnameOrEntityName = "Target",
            DisplayName = "Target Client"
        });

        var policyId = await service.SaveKycPolicyAsync(new ClientKycPolicyEditModel
        {
            ClientId = sourceClientId,
            MainClassName = "Risk",
            SubClassName = "Cover",
            PolicyNumber = "COPY-1",
            LifeCover = 100m
        }, "tester");
        var recommendationId = await service.SaveKycRecommendationAsync(new ClientKycRecommendationEditModel
        {
            ClientId = sourceClientId,
            ClientKycPolicyId = policyId,
            RecommendationType = "Review",
            Status = "Open",
            Details = "Review cover"
        }, "tester");

        await service.CopyOrTransferKycAsync(new KycTransferModel
        {
            SourceClientId = sourceClientId,
            TargetClientId = targetClientId,
            Operation = KycTransferOperation.Copy,
            PolicyIds = [policyId],
            RecommendationIds = [recommendationId]
        }, "tester");

        Assert.Equal(2, await db.ClientKycPolicies.CountAsync(policy => policy.PolicyNumber == "COPY-1"));
        Assert.Equal(2, await db.ClientKycRecommendations.CountAsync(recommendation => recommendation.RecommendationType == "Review"));
        Assert.True(await db.ClientKycPolicies.AnyAsync(policy => policy.ClientId == sourceClientId && policy.Id == policyId));
        Assert.True(await db.ClientKycPolicies.AnyAsync(policy => policy.ClientId == targetClientId && policy.PolicyNumber == "COPY-1"));

        await service.CopyOrTransferKycAsync(new KycTransferModel
        {
            SourceClientId = sourceClientId,
            TargetClientId = targetClientId,
            Operation = KycTransferOperation.Transfer,
            PolicyIds = [policyId],
            RecommendationIds = [recommendationId]
        }, "tester");

        Assert.False(await db.ClientKycPolicies.AnyAsync(policy => policy.ClientId == sourceClientId && policy.Id == policyId));
        Assert.True(await db.ClientKycPolicies.AnyAsync(policy => policy.ClientId == targetClientId && policy.Id == policyId && policy.KanaanId == "FAMILY-KYC-1"));
        Assert.True(await db.ClientKycRecommendations.AnyAsync(recommendation => recommendation.ClientId == targetClientId && recommendation.Id == recommendationId));
    }
}
