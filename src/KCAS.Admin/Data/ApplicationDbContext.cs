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
    public DbSet<ClientInvestmentAccount> ClientInvestmentAccounts => Set<ClientInvestmentAccount>();
    public DbSet<ClientInvestmentTransaction> ClientInvestmentTransactions => Set<ClientInvestmentTransaction>();

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
            entity.Property(client => client.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasIndex(client => client.LegacyClientId).IsUnique();
            entity.HasIndex(client => client.KanaanId);
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
            entity.HasIndex(policy => policy.LegacyKycId).IsUnique();
            entity.HasIndex(policy => policy.ClientId);
            entity.HasIndex(policy => policy.PolicyNumber);
            entity.HasIndex(policy => new { policy.LegacyMainClassId, policy.LegacySubClassId });
            entity.HasIndex(policy => new { policy.IncludeInCalculations, policy.IsQuote });
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
    }
}
