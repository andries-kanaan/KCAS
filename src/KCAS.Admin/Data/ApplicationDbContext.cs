using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Client> Clients => Set<Client>();

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
    }
}
