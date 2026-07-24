using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KCAS.Admin.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientPersonalProfile> ClientPersonalProfiles => Set<ClientPersonalProfile>();
    public DbSet<ClientFinancialProfile> ClientFinancialProfiles => Set<ClientFinancialProfile>();
    public DbSet<ClientContactPoint> ClientContactPoints => Set<ClientContactPoint>();
    public DbSet<ClientAddress> ClientAddresses => Set<ClientAddress>();
    public DbSet<ClientRelationship> ClientRelationships => Set<ClientRelationship>();
    public DbSet<ClientLegacySnapshot> ClientLegacySnapshots => Set<ClientLegacySnapshot>();
    public DbSet<ClientNote> ClientNotes => Set<ClientNote>();
    public DbSet<ClientKycPolicy> ClientKycPolicies => Set<ClientKycPolicy>();
    public DbSet<ClientKycRecommendation> ClientKycRecommendations => Set<ClientKycRecommendation>();
    public DbSet<ClientInvestmentAccount> ClientInvestmentAccounts => Set<ClientInvestmentAccount>();
    public DbSet<ClientInvestmentTransaction> ClientInvestmentTransactions => Set<ClientInvestmentTransaction>();
    public DbSet<ClientFundValuation> ClientFundValuations => Set<ClientFundValuation>();
    public DbSet<InvestmentAdministratorReference> InvestmentAdministratorReferences => Set<InvestmentAdministratorReference>();
    public DbSet<InvestmentFundReference> InvestmentFundReferences => Set<InvestmentFundReference>();
    public DbSet<InvestmentProductTypeReference> InvestmentProductTypeReferences => Set<InvestmentProductTypeReference>();
    public DbSet<KycMainClassReference> KycMainClassReferences => Set<KycMainClassReference>();
    public DbSet<KycSubClassReference> KycSubClassReferences => Set<KycSubClassReference>();
    public DbSet<MarketReferenceValue> MarketReferenceValues => Set<MarketReferenceValue>();
    public DbSet<LegacyImportRun> LegacyImportRuns => Set<LegacyImportRun>();
    public DbSet<LegacyImportRowState> LegacyImportRowStates => Set<LegacyImportRowState>();
    public DbSet<LegacyImportDifference> LegacyImportDifferences => Set<LegacyImportDifference>();
    public DbSet<LegacySourceSnapshot> LegacySourceSnapshots => Set<LegacySourceSnapshot>();
    public DbSet<ComplianceProfile> ComplianceProfiles => Set<ComplianceProfile>();
    public DbSet<GovernanceRoleAssignment> GovernanceRoleAssignments => Set<GovernanceRoleAssignment>();
    public DbSet<ControlledDocument> ControlledDocuments => Set<ControlledDocument>();
    public DbSet<ComplianceReferenceValue> ComplianceReferenceValues => Set<ComplianceReferenceValue>();
    public DbSet<RiskMethodologyVersion> RiskMethodologyVersions => Set<RiskMethodologyVersion>();
    public DbSet<RiskFactorDefinition> RiskFactorDefinitions => Set<RiskFactorDefinition>();
    public DbSet<RiskFactorOption> RiskFactorOptions => Set<RiskFactorOption>();
    public DbSet<RiskBand> RiskBands => Set<RiskBand>();
    public DbSet<ComplianceTask> ComplianceTasks => Set<ComplianceTask>();
    public DbSet<ComplianceEvidence> ComplianceEvidence => Set<ComplianceEvidence>();
    public DbSet<ComplianceApproval> ComplianceApprovals => Set<ComplianceApproval>();
    public DbSet<ComplianceAuditEvent> ComplianceAuditEvents => Set<ComplianceAuditEvent>();
    public DbSet<ClientEvidenceRequirement> ClientEvidenceRequirements => Set<ClientEvidenceRequirement>();
    public DbSet<ClientEvidenceItem> ClientEvidenceItems => Set<ClientEvidenceItem>();
    public DbSet<ClientEvidenceException> ClientEvidenceExceptions => Set<ClientEvidenceException>();
    public DbSet<ClientEvidenceScanRoot> ClientEvidenceScanRoots => Set<ClientEvidenceScanRoot>();
    public DbSet<ClientEvidenceScanRun> ClientEvidenceScanRuns => Set<ClientEvidenceScanRun>();
    public DbSet<ClientEvidenceScanFile> ClientEvidenceScanFiles => Set<ClientEvidenceScanFile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.Id).HasMaxLength(64);
            entity.Property(user => user.UserName).HasMaxLength(191);
            entity.Property(user => user.NormalizedUserName).HasMaxLength(191);
            entity.Property(user => user.Email).HasMaxLength(191);
            entity.Property(user => user.NormalizedEmail).HasMaxLength(191);
            entity.Property(user => user.DisplayName).HasMaxLength(191);
            entity.Property(user => user.WindowsAccountName).HasMaxLength(191);
            entity.Property(user => user.ApprovedByUserId).HasMaxLength(64);
            entity.Property(user => user.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasIndex(user => user.WindowsAccountName);
        });

        builder.Entity<IdentityRole>(entity =>
        {
            entity.Property(role => role.Id).HasMaxLength(64);
            entity.Property(role => role.Name).HasMaxLength(191);
            entity.Property(role => role.NormalizedName).HasMaxLength(191);
        });

        builder.Entity<IdentityRoleClaim<string>>(entity =>
            entity.Property(roleClaim => roleClaim.RoleId).HasMaxLength(64));

        builder.Entity<IdentityUserClaim<string>>(entity =>
            entity.Property(userClaim => userClaim.UserId).HasMaxLength(64));

        builder.Entity<IdentityUserLogin<string>>(entity =>
        {
            entity.Property(userLogin => userLogin.LoginProvider).HasMaxLength(64);
            entity.Property(userLogin => userLogin.ProviderKey).HasMaxLength(64);
            entity.Property(userLogin => userLogin.UserId).HasMaxLength(64);
        });

        builder.Entity<IdentityUserRole<string>>(entity =>
        {
            entity.Property(userRole => userRole.UserId).HasMaxLength(64);
            entity.Property(userRole => userRole.RoleId).HasMaxLength(64);
        });

        builder.Entity<IdentityUserToken<string>>(entity =>
        {
            entity.Property(userToken => userToken.UserId).HasMaxLength(64);
            entity.Property(userToken => userToken.LoginProvider).HasMaxLength(64);
            entity.Property(userToken => userToken.Name).HasMaxLength(64);
        });

        builder.Entity<Client>(entity =>
        {
            entity.Property(client => client.SurnameOrEntityName).HasMaxLength(200);
            entity.Property(client => client.DisplayName).HasMaxLength(220);
            entity.Property(client => client.ClientCategory)
                .HasMaxLength(96)
                .HasDefaultValue(ClientCategories.NaturalPerson);
            entity.Property(client => client.ClientCategorySource)
                .HasMaxLength(32)
                .HasDefaultValue(ClientCategorySources.Unknown);
            entity.Property(client => client.ClientCategoryReason).HasMaxLength(512);
            entity.Property(client => client.ClientCategoryUpdatedBy).HasMaxLength(191);
            entity.Property(client => client.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(client => client.LegacyReconciliationStatus)
                .HasMaxLength(32)
                .HasDefaultValue(LegacyReconciliationStatuses.Unscanned);
            entity.HasIndex(client => client.LegacyClientId).IsUnique();
            entity.HasIndex(client => client.KanaanId);
            entity.HasIndex(client => client.ClientCategory);
            entity.HasIndex(client => client.DisplayName);
        });

        builder.Entity<ClientPersonalProfile>(entity =>
        {
            entity.HasKey(profile => profile.ClientId);
            entity.HasOne(profile => profile.Client)
                .WithOne(client => client.PersonalProfile)
                .HasForeignKey<ClientPersonalProfile>(profile => profile.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(profile => profile.WorkdayTravelPercent).HasPrecision(9, 4);
            entity.HasIndex(profile => profile.SouthAfricanIdNumber);
        });

        builder.Entity<ClientFinancialProfile>(entity =>
        {
            entity.HasKey(profile => profile.ClientId);
            entity.HasOne(profile => profile.Client)
                .WithOne(client => client.FinancialProfile)
                .HasForeignKey<ClientFinancialProfile>(profile => profile.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(profile => profile.GrossMonthlySalary).HasPrecision(18, 2);
            entity.Property(profile => profile.GrossAnnualSalary).HasPrecision(18, 2);
            entity.Property(profile => profile.MonthlyExpenses).HasPrecision(18, 2);
            entity.Property(profile => profile.YearlyBonus).HasPrecision(18, 2);
            entity.Property(profile => profile.OtherIncome).HasPrecision(18, 2);
            entity.Property(profile => profile.EmployerPensionContributionAmount).HasPrecision(18, 2);
            entity.Property(profile => profile.EmployerPensionContributionPercent).HasPrecision(9, 4);
            entity.Property(profile => profile.CapitalRequirementPercent).HasPrecision(9, 4);
            entity.Property(profile => profile.MinimumRetirementIncomePercent).HasPrecision(9, 4);
            entity.Property(profile => profile.ExpectedRetirementIncomePercent).HasPrecision(9, 4);
            entity.Property(profile => profile.PreservationFundLumpSumPercent).HasPrecision(9, 4);
            entity.Property(profile => profile.RetirementProvisionTax).HasPrecision(18, 2);
            entity.Property(profile => profile.PensionFundTax).HasPrecision(18, 2);
            entity.Property(profile => profile.RetirementAnnuityTax).HasPrecision(18, 2);
            entity.Property(profile => profile.RepresentativeEquitiesPercent).HasPrecision(9, 4);
            entity.Property(profile => profile.RepresentativeAlternativeInvestmentsPercent).HasPrecision(9, 4);
            entity.Property(profile => profile.RepresentativeFixedPropertyPercent).HasPrecision(9, 4);
            entity.Property(profile => profile.RepresentativeOffshorePercent).HasPrecision(9, 4);
        });

        builder.Entity<ClientContactPoint>(entity =>
        {
            entity.HasOne(contact => contact.Client)
                .WithMany(client => client.ContactPoints)
                .HasForeignKey(contact => contact.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(contact => new { contact.ClientId, contact.ContactType, contact.IsPrimary });
            entity.HasIndex(contact => contact.Value);
        });

        builder.Entity<ClientAddress>(entity =>
        {
            entity.HasOne(address => address.Client)
                .WithMany(client => client.Addresses)
                .HasForeignKey(address => address.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(address => new { address.ClientId, address.AddressType });
        });

        builder.Entity<ClientRelationship>(entity =>
        {
            entity.HasOne(relationship => relationship.Client)
                .WithMany(client => client.Relationships)
                .HasForeignKey(relationship => relationship.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(relationship => relationship.GrossMonthlySalary).HasPrecision(18, 2);
            entity.Property(relationship => relationship.GrossAnnualSalary).HasPrecision(18, 2);
            entity.Property(relationship => relationship.YearlyBonus).HasPrecision(18, 2);
            entity.Property(relationship => relationship.OtherIncome).HasPrecision(18, 2);
            entity.Property(relationship => relationship.EmployerPensionContributionAmount).HasPrecision(18, 2);
            entity.Property(relationship => relationship.EmployerPensionContributionPercent).HasPrecision(9, 4);
            entity.HasIndex(relationship => new { relationship.ClientId, relationship.RelationshipType });
        });

        builder.Entity<ClientLegacySnapshot>(entity =>
        {
            entity.HasOne(snapshot => snapshot.Client)
                .WithMany(client => client.LegacySnapshots)
                .HasForeignKey(snapshot => snapshot.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(snapshot => new { snapshot.SourceTable, snapshot.SourceId });
        });

        builder.Entity<ClientNote>(entity =>
        {
            entity.HasOne(note => note.Client)
                .WithMany(client => client.Notes)
                .HasForeignKey(note => note.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(note => note.NoteDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.HasIndex(note => note.LegacyClientNoteId).IsUnique();
            entity.HasIndex(note => new { note.ClientId, note.NoteDate });
            entity.HasIndex(note => note.Title);
        });

        builder.Entity<ClientKycPolicy>(entity =>
        {
            entity.HasOne(policy => policy.Client)
                .WithMany(client => client.KycPolicies)
                .HasForeignKey(policy => policy.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(policy => policy.Value).HasPrecision(18, 2);
            entity.Property(policy => policy.LifeCover).HasPrecision(18, 2);
            entity.Property(policy => policy.DisabilityCover).HasPrecision(18, 2);
            entity.Property(policy => policy.DreadDiseaseCover).HasPrecision(18, 2);
            entity.Property(policy => policy.CompulsoryContributionValue).HasPrecision(18, 2);
            entity.Property(policy => policy.VoluntaryContributionValue).HasPrecision(18, 2);
            entity.Property(policy => policy.Debt).HasPrecision(18, 2);
            entity.Property(policy => policy.MonthlyPremium).HasPrecision(18, 2);
            entity.Property(policy => policy.OnceOffPremium).HasPrecision(18, 2);
            entity.Property(policy => policy.MonthlyIncome).HasPrecision(18, 2);
            entity.Property(policy => policy.CapitalAdequacyRatioPercent).HasPrecision(9, 4);
            entity.Property(policy => policy.TaxPercent).HasPrecision(9, 4);
            entity.HasIndex(policy => policy.LegacyKycId);
            entity.HasIndex(policy => policy.ClientId);
            entity.HasIndex(policy => policy.PolicyNumber);
            entity.HasIndex(policy => new { policy.LegacyMainClassId, policy.LegacySubClassId });
            entity.HasIndex(policy => new { policy.IncludeInCalculations, policy.IsQuote });
        });

        builder.Entity<ClientKycRecommendation>(entity =>
        {
            entity.HasOne(recommendation => recommendation.Client)
                .WithMany(client => client.KycRecommendations)
                .HasForeignKey(recommendation => recommendation.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(recommendation => recommendation.KycPolicy)
                .WithMany()
                .HasForeignKey(recommendation => recommendation.ClientKycPolicyId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(recommendation => recommendation.RecommendationDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.HasIndex(recommendation => recommendation.LegacyRecommendationId);
            entity.HasIndex(recommendation => recommendation.ClientId);
            entity.HasIndex(recommendation => recommendation.ClientKycPolicyId);
            entity.HasIndex(recommendation => recommendation.KanaanId);
            entity.HasIndex(recommendation => recommendation.Status);
        });

        builder.Entity<ClientInvestmentAccount>(entity =>
        {
            entity.HasOne(account => account.Client)
                .WithMany(client => client.InvestmentAccounts)
                .HasForeignKey(account => account.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(account => account.InvestmentDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.Property(account => account.SurrenderDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.HasIndex(account => account.LegacyInvestmentAccountId).IsUnique();
            entity.HasIndex(account => account.ClientId);
            entity.HasIndex(account => account.LegacyClientId);
            entity.HasIndex(account => account.LegacyLinkedAccountId);
            entity.HasIndex(account => account.AccountNumber);
        });

        builder.Entity<ClientInvestmentTransaction>(entity =>
        {
            entity.HasOne(transaction => transaction.InvestmentAccount)
                .WithMany(account => account.Transactions)
                .HasForeignKey(transaction => transaction.ClientInvestmentAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(transaction => transaction.TransactionDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.Property(transaction => transaction.ExchangeRate).HasPrecision(18, 6);
            entity.Property(transaction => transaction.InvestmentAmountForeign).HasPrecision(18, 2);
            entity.Property(transaction => transaction.InvestmentAmountZar).HasPrecision(18, 2);
            entity.Property(transaction => transaction.WithdrawalAmountForeign).HasPrecision(18, 2);
            entity.Property(transaction => transaction.WithdrawalAmountZar).HasPrecision(18, 2);
            entity.Property(transaction => transaction.AnnualIncreasePercent).HasPrecision(9, 4);
            entity.Property(transaction => transaction.BalanceForeign).HasPrecision(18, 2);
            entity.Property(transaction => transaction.BalanceZar).HasPrecision(18, 2);
            entity.HasIndex(transaction => transaction.LegacyInvestmentHistoryId).IsUnique();
            entity.HasIndex(transaction => transaction.ClientInvestmentAccountId);
            entity.HasIndex(transaction => transaction.LegacyInvestmentAccountId);
            entity.HasIndex(transaction => transaction.TransactionDate);
        });

        builder.Entity<ClientFundValuation>(entity =>
        {
            entity.HasOne(valuation => valuation.Client)
                .WithMany(client => client.FundValuations)
                .HasForeignKey(valuation => valuation.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(valuation => valuation.ValuationDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.Property(valuation => valuation.AmountForeign).HasPrecision(18, 2);
            entity.Property(valuation => valuation.AmountZar).HasPrecision(18, 2);
            entity.HasIndex(valuation => valuation.LegacyFundId).IsUnique();
            entity.HasIndex(valuation => valuation.ClientId);
            entity.HasIndex(valuation => valuation.LegacyClientId);
            entity.HasIndex(valuation => valuation.KanaanId);
            entity.HasIndex(valuation => valuation.InvestmentUniqueNumber);
            entity.HasIndex(valuation => valuation.ValuationDate);
        });

        builder.Entity<InvestmentAdministratorReference>(entity =>
        {
            entity.HasIndex(reference => reference.LegacyLispId).IsUnique();
            entity.HasIndex(reference => reference.Name);
            entity.HasIndex(reference => new { reference.IsCurrent, reference.Name });
        });

        builder.Entity<InvestmentFundReference>(entity =>
        {
            entity.HasIndex(reference => reference.LegacyFundNameId).IsUnique();
            entity.HasIndex(reference => reference.Name);
            entity.HasIndex(reference => reference.ShortName);
            entity.HasIndex(reference => new { reference.IsCurrent, reference.Name });
            entity.HasIndex(reference => new { reference.LegacyAdministratorId, reference.LegacyMainClassId, reference.LegacySubClassId });
        });

        builder.Entity<InvestmentProductTypeReference>(entity =>
        {
            entity.HasIndex(reference => reference.LegacyCompanyProductId).IsUnique();
            entity.HasIndex(reference => reference.Name);
        });

        builder.Entity<KycMainClassReference>(entity =>
        {
            entity.HasIndex(reference => reference.LegacyMainClassId).IsUnique();
            entity.HasIndex(reference => reference.Name);
        });

        builder.Entity<KycSubClassReference>(entity =>
        {
            entity.HasOne(reference => reference.MainClass)
                .WithMany(mainClass => mainClass.SubClasses)
                .HasForeignKey(reference => reference.KycMainClassReferenceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(reference => reference.LegacySubClassId).IsUnique();
            entity.HasIndex(reference => reference.LegacyMainClassId);
            entity.HasIndex(reference => new { reference.KycMainClassReferenceId, reference.Name });
        });

        builder.Entity<MarketReferenceValue>(entity =>
        {
            entity.Property(reference => reference.PriceDate)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
            entity.Property(reference => reference.Value).HasPrecision(18, 4);
            entity.HasIndex(reference => reference.LegacyMiscInfoId).IsUnique();
            entity.HasIndex(reference => reference.Name);
            entity.HasIndex(reference => reference.PriceDate);
        });

        builder.Entity<LegacyImportRun>(entity =>
        {
            entity.Property(run => run.Mode).HasMaxLength(32);
            entity.Property(run => run.Status).HasMaxLength(32);
            entity.Property(run => run.SourceLabel).HasMaxLength(256);
            entity.Property(run => run.SourceSnapshotSha256).HasMaxLength(64);
            entity.Property(run => run.SourceSnapshotFileName).HasMaxLength(260);
            entity.HasIndex(run => run.StartedAtUtc);
            entity.HasIndex(run => run.Status);
            entity.HasIndex(run => run.SourceSnapshotSha256);
        });

        builder.Entity<LegacyImportRowState>(entity =>
        {
            entity.HasOne(row => row.LegacyImportRun)
                .WithMany(run => run.Rows)
                .HasForeignKey(row => row.LegacyImportRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(row => row.SourceTable).HasMaxLength(64);
            entity.Property(row => row.Classification).HasMaxLength(32);
            entity.Property(row => row.ApplyStatus).HasMaxLength(32);
            entity.Property(row => row.TargetEntityType).HasMaxLength(128);
            entity.Property(row => row.IncomingFingerprint).HasMaxLength(64);
            entity.Property(row => row.BaselineFingerprint).HasMaxLength(64);
            entity.HasIndex(row => new { row.LegacyImportRunId, row.SourceTable, row.SourceId }).IsUnique();
            entity.HasIndex(row => new { row.LegacyImportRunId, row.Classification });
        });

        builder.Entity<LegacyImportDifference>(entity =>
        {
            entity.HasOne(difference => difference.LegacyImportRowState)
                .WithMany(row => row.Differences)
                .HasForeignKey(difference => difference.LegacyImportRowStateId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(difference => difference.FieldName).HasMaxLength(191);
            entity.Property(difference => difference.Decision).HasMaxLength(32);
            entity.Property(difference => difference.ReviewedBy).HasMaxLength(191);
            entity.HasIndex(difference => new { difference.LegacyImportRowStateId, difference.FieldName }).IsUnique();
            entity.HasIndex(difference => difference.Decision);
        });

        builder.Entity<LegacySourceSnapshot>(entity =>
        {
            entity.Property(snapshot => snapshot.SourceTable).HasMaxLength(64);
            entity.Property(snapshot => snapshot.Fingerprint).HasMaxLength(64);
            entity.HasIndex(snapshot => new { snapshot.SourceTable, snapshot.SourceId }).IsUnique();
            entity.HasIndex(snapshot => snapshot.LastSeenAtUtc);
        });

        builder.Entity<ComplianceProfile>(entity =>
        {
            entity.Property(profile => profile.LegalName).HasMaxLength(200);
            entity.Property(profile => profile.TradingName).HasMaxLength(200);
            entity.Property(profile => profile.FspNumber).HasMaxLength(64);
            entity.Property(profile => profile.AccountableInstitutionNumber).HasMaxLength(64);
            entity.Property(profile => profile.PrimaryContactName).HasMaxLength(191);
            entity.Property(profile => profile.PrimaryContactEmail).HasMaxLength(191);
            entity.Property(profile => profile.PrimaryContactPhone).HasMaxLength(64);
            entity.Property(profile => profile.Status).HasMaxLength(32);
            entity.Property(profile => profile.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(profile => profile.EffectiveFrom));
            ConfigureDateOnly(entity.Property(profile => profile.EffectiveTo));
            entity.HasIndex(profile => profile.Status);
        });

        builder.Entity<GovernanceRoleAssignment>(entity =>
        {
            entity.Property(role => role.RoleType).HasMaxLength(96);
            entity.Property(role => role.PersonName).HasMaxLength(191);
            entity.Property(role => role.Email).HasMaxLength(191);
            entity.Property(role => role.Phone).HasMaxLength(64);
            entity.Property(role => role.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(role => role.StartDate));
            ConfigureDateOnly(entity.Property(role => role.EndDate));
            entity.HasIndex(role => new { role.RoleType, role.IsActive });
        });

        builder.Entity<ControlledDocument>(entity =>
        {
            entity.Property(document => document.DocumentType).HasMaxLength(96);
            entity.Property(document => document.Title).HasMaxLength(240);
            entity.Property(document => document.Owner).HasMaxLength(191);
            entity.Property(document => document.VersionReference).HasMaxLength(96);
            entity.Property(document => document.Status).HasMaxLength(32);
            entity.Property(document => document.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(document => document.EffectiveDate));
            ConfigureDateOnly(entity.Property(document => document.NextReviewDate));
            entity.HasIndex(document => new { document.DocumentType, document.Status });
            entity.HasIndex(document => document.NextReviewDate);
        });

        builder.Entity<ComplianceReferenceValue>(entity =>
        {
            entity.Property(reference => reference.Category).HasMaxLength(96);
            entity.Property(reference => reference.Code).HasMaxLength(96);
            entity.Property(reference => reference.Name).HasMaxLength(191);
            entity.Property(reference => reference.UpdatedBy).HasMaxLength(191);
            entity.HasIndex(reference => new { reference.Category, reference.Code, reference.IsActive }).IsUnique();
            entity.HasIndex(reference => new { reference.Category, reference.SortOrder });
        });

        builder.Entity<RiskMethodologyVersion>(entity =>
        {
            entity.Property(methodology => methodology.Name).HasMaxLength(191);
            entity.Property(methodology => methodology.VersionLabel).HasMaxLength(64);
            entity.Property(methodology => methodology.Status).HasMaxLength(32);
            entity.Property(methodology => methodology.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(methodology => methodology.EffectiveFrom));
            ConfigureDateOnly(entity.Property(methodology => methodology.EffectiveTo));
            entity.HasIndex(methodology => methodology.Status);
            entity.HasIndex(methodology => methodology.EffectiveFrom);
        });

        builder.Entity<RiskFactorDefinition>(entity =>
        {
            entity.HasOne(factor => factor.MethodologyVersion)
                .WithMany(methodology => methodology.Factors)
                .HasForeignKey(factor => factor.RiskMethodologyVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(factor => factor.Code).HasMaxLength(96);
            entity.Property(factor => factor.Name).HasMaxLength(191);
            entity.Property(factor => factor.Weight).HasPrecision(9, 4);
            entity.HasIndex(factor => new { factor.RiskMethodologyVersionId, factor.Code }).IsUnique();
        });

        builder.Entity<RiskFactorOption>(entity =>
        {
            entity.HasOne(option => option.FactorDefinition)
                .WithMany(factor => factor.Options)
                .HasForeignKey(option => option.RiskFactorDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(option => option.Code).HasMaxLength(96);
            entity.Property(option => option.Label).HasMaxLength(191);
            entity.HasIndex(option => new { option.RiskFactorDefinitionId, option.Code }).IsUnique();
        });

        builder.Entity<RiskBand>(entity =>
        {
            entity.HasOne(band => band.MethodologyVersion)
                .WithMany(methodology => methodology.Bands)
                .HasForeignKey(band => band.RiskMethodologyVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(band => band.Name).HasMaxLength(96);
            entity.Property(band => band.MinimumScore).HasPrecision(9, 4);
            entity.Property(band => band.MaximumScore).HasPrecision(9, 4);
            entity.HasIndex(band => new { band.RiskMethodologyVersionId, band.Name }).IsUnique();
        });

        builder.Entity<ComplianceTask>(entity =>
        {
            entity.Property(task => task.Title).HasMaxLength(240);
            entity.Property(task => task.Owner).HasMaxLength(191);
            entity.Property(task => task.Priority).HasMaxLength(32);
            entity.Property(task => task.Status).HasMaxLength(32);
            entity.Property(task => task.LinkedEntityType).HasMaxLength(128);
            entity.Property(task => task.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(task => task.DueDate));
            entity.HasIndex(task => new { task.Status, task.DueDate });
            entity.HasIndex(task => new { task.LinkedEntityType, task.LinkedEntityId });
        });

        builder.Entity<ComplianceEvidence>(entity =>
        {
            entity.Property(evidence => evidence.EvidenceType).HasMaxLength(96);
            entity.Property(evidence => evidence.Title).HasMaxLength(240);
            entity.Property(evidence => evidence.Source).HasMaxLength(191);
            entity.Property(evidence => evidence.Reviewer).HasMaxLength(191);
            entity.Property(evidence => evidence.LinkedEntityType).HasMaxLength(128);
            entity.Property(evidence => evidence.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(evidence => evidence.ReceivedDate));
            ConfigureDateOnly(entity.Property(evidence => evidence.VerifiedDate));
            ConfigureDateOnly(entity.Property(evidence => evidence.ExpiryDate));
            entity.HasIndex(evidence => new { evidence.EvidenceType, evidence.ExpiryDate });
            entity.HasIndex(evidence => new { evidence.LinkedEntityType, evidence.LinkedEntityId });
        });

        builder.Entity<ComplianceApproval>(entity =>
        {
            entity.Property(approval => approval.TargetEntityType).HasMaxLength(128);
            entity.Property(approval => approval.Decision).HasMaxLength(32);
            entity.Property(approval => approval.Approver).HasMaxLength(191);
            entity.HasIndex(approval => new { approval.TargetEntityType, approval.TargetEntityId });
        });

        builder.Entity<ComplianceAuditEvent>(entity =>
        {
            entity.Property(audit => audit.EntityType).HasMaxLength(128);
            entity.Property(audit => audit.Action).HasMaxLength(64);
            entity.Property(audit => audit.UserName).HasMaxLength(191);
            entity.HasIndex(audit => new { audit.EntityType, audit.EntityId });
            entity.HasIndex(audit => audit.TimestampUtc);
        });

        builder.Entity<ClientEvidenceRequirement>(entity =>
        {
            entity.Property(requirement => requirement.ClientCategory).HasMaxLength(96);
            entity.Property(requirement => requirement.RequirementGroup).HasMaxLength(96);
            entity.Property(requirement => requirement.EvidenceType).HasMaxLength(96);
            entity.Property(requirement => requirement.Title).HasMaxLength(240);
            entity.Property(requirement => requirement.Status).HasMaxLength(32);
            entity.Property(requirement => requirement.UpdatedBy).HasMaxLength(191);
            entity.HasIndex(requirement => new { requirement.ClientCategory, requirement.EvidenceType, requirement.Status });
            entity.HasIndex(requirement => new { requirement.RequirementGroup, requirement.SortOrder });
        });

        builder.Entity<ClientEvidenceItem>(entity =>
        {
            entity.HasOne(item => item.Client)
                .WithMany(client => client.EvidenceItems)
                .HasForeignKey(item => item.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Requirement)
                .WithMany()
                .HasForeignKey(item => item.ClientEvidenceRequirementId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.ScanFile)
                .WithOne(file => file.EvidenceItem)
                .HasForeignKey<ClientEvidenceItem>(item => item.ClientEvidenceScanFileId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SupersededByClientEvidenceItem)
                .WithMany()
                .HasForeignKey(item => item.SupersededByClientEvidenceItemId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(item => item.EvidenceType).HasMaxLength(96);
            entity.Property(item => item.Title).HasMaxLength(240);
            entity.Property(item => item.SourcePath).HasMaxLength(512);
            entity.Property(item => item.RelativePath).HasMaxLength(512);
            entity.Property(item => item.FileName).HasMaxLength(260);
            entity.Property(item => item.FileSha256).HasMaxLength(64);
            entity.Property(item => item.Reviewer).HasMaxLength(191);
            entity.Property(item => item.ScreeningSubjectType).HasMaxLength(96);
            entity.Property(item => item.ScreeningSubjectName).HasMaxLength(240);
            entity.Property(item => item.ScreeningOutcome).HasMaxLength(96);
            entity.Property(item => item.ScreeningRiskSignal).HasMaxLength(32);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.SelectionStatus)
                .HasMaxLength(32)
                .HasDefaultValue(ClientEvidenceSelectionStatuses.Candidate);
            entity.Property(item => item.SelectionReason).HasMaxLength(512);
            entity.Property(item => item.SelectedBy).HasMaxLength(191);
            entity.Property(item => item.VerificationPolicy)
                .HasMaxLength(32)
                .HasDefaultValue("ManualRequired");
            entity.Property(item => item.UpdatedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(item => item.ReceivedDate));
            ConfigureDateOnly(entity.Property(item => item.VerifiedDate));
            ConfigureDateOnly(entity.Property(item => item.ExpiryDate));
            ConfigureDateOnly(entity.Property(item => item.ScreeningReviewDate));
            entity.HasIndex(item => new { item.ClientId, item.EvidenceType, item.Status });
            entity.HasIndex(item => new { item.ClientId, item.EvidenceType, item.SelectionStatus });
            entity.HasIndex(item => new { item.ClientId, item.EvidenceType, item.ScreeningRiskSignal });
            entity.HasIndex(item => item.EscalationRequired);
            entity.HasIndex(item => item.FileSha256);
            entity.HasIndex(item => item.ExpiryDate);
            entity.HasIndex(item => item.SupersededByClientEvidenceItemId);
        });

        builder.Entity<ClientEvidenceException>(entity =>
        {
            entity.HasOne(exception => exception.Client)
                .WithMany(client => client.EvidenceExceptions)
                .HasForeignKey(exception => exception.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(exception => exception.Requirement)
                .WithMany()
                .HasForeignKey(exception => exception.ClientEvidenceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(exception => exception.ApprovedBy).HasMaxLength(191);
            ConfigureDateOnly(entity.Property(exception => exception.ReviewDate));
            entity.HasIndex(exception => new { exception.ClientId, exception.ClientEvidenceRequirementId, exception.IsActive });
            entity.HasIndex(exception => exception.ReviewDate);
        });

        builder.Entity<ClientEvidenceScanRoot>(entity =>
        {
            entity.Property(root => root.RootPath).HasMaxLength(512);
            entity.Property(root => root.UpdatedBy).HasMaxLength(191);
            entity.HasIndex(root => root.IsActive);
        });

        builder.Entity<ClientEvidenceScanRun>(entity =>
        {
            entity.Property(run => run.RootPath).HasMaxLength(512);
            entity.Property(run => run.Status).HasMaxLength(32);
            entity.Property(run => run.StartedBy).HasMaxLength(191);
            entity.HasIndex(run => run.StartedAtUtc);
            entity.HasIndex(run => run.Status);
        });

        builder.Entity<ClientEvidenceScanFile>(entity =>
        {
            entity.HasOne(file => file.ScanRun)
                .WithMany(run => run.Files)
                .HasForeignKey(file => file.ClientEvidenceScanRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(file => file.Client)
                .WithMany()
                .HasForeignKey(file => file.ClientId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(file => file.FullPath).HasMaxLength(512);
            entity.Property(file => file.RelativePath).HasMaxLength(512);
            entity.Property(file => file.FileName).HasMaxLength(260);
            entity.Property(file => file.FileSha256).HasMaxLength(64);
            entity.Property(file => file.MatchStatus).HasMaxLength(32);
            entity.Property(file => file.SuggestedEvidenceType).HasMaxLength(96);
            entity.Property(file => file.MatchReason).HasMaxLength(512);
            entity.HasIndex(file => new { file.ClientEvidenceScanRunId, file.MatchStatus });
            entity.HasIndex(file => file.FileSha256);
            entity.HasIndex(file => file.ClientId);
        });
    }

    private static void ConfigureDateOnly<TProperty>(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<TProperty> propertyBuilder)
    {
        if (typeof(TProperty) == typeof(DateOnly?))
        {
            ((Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateOnly?>)(object)propertyBuilder)
                .HasColumnType("date")
                .HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                    value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                    value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null));
        }
    }
}
