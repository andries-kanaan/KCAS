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
    }
}
