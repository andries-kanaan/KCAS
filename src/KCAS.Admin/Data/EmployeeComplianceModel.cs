using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

internal static class EmployeeComplianceModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<EmployeeProfile>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.TransferKey).IsUnique();
            e.HasIndex(x => new { x.EmploymentStatus, x.DisplayName });
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.EmploymentStart));
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.EmploymentEnd));
        });
        builder.Entity<EmployeeAccountLink>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EmployeeComplianceReview>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.NextReviewDate));
            e.HasIndex(x => new { x.EmployeeProfileId, x.Status });
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EmployeeComplianceCheck>(e =>
        {
            e.HasOne<EmployeeComplianceReview>().WithMany().HasForeignKey(x => x.EmployeeComplianceReviewId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EmployeeComplianceCheck>().WithMany().HasForeignKey(x => x.SupersedesCheckId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EmployeeTfsBatch>().WithMany().HasForeignKey(x => x.EmployeeTfsBatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmployeeComplianceReviewId, x.Kind });
        });
        builder.Entity<EmployeeAccessConfirmation>(e =>
        {
            e.HasOne<EmployeeComplianceReview>().WithMany().HasForeignKey(x => x.EmployeeComplianceReviewId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EmployeeEvidenceDocument>(e =>
        {
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmployeeProfileId, x.EvidenceSha256 });
        });
        builder.Entity<EmployeeReviewDecision>(e =>
        {
            e.HasIndex(x => x.EmployeeComplianceReviewId).IsUnique();
            e.HasOne<EmployeeComplianceReview>().WithMany().HasForeignKey(x => x.EmployeeComplianceReviewId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.ReviewerEmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EmployeeComplianceTask>(e =>
        {
            e.HasIndex(x => new { x.EmployeeProfileId, x.TriggerKey }).IsUnique();
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.DueDate));
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EmployeeTfsBatch>().WithMany().HasForeignKey(x => x.EmployeeTfsBatchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EmployeeTfsBatch>().HasIndex(x => x.SourceVersion).IsUnique();
        builder.Entity<EmployeeComplianceAuditEvent>(e =>
        {
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmployeeProfileId, x.TimestampUtc });
        });
        builder.Entity<EmployeeTransferRecord>(e =>
        {
            e.HasIndex(x => new { x.PackageId, x.Direction, x.EmployeeKey }).IsUnique();
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
