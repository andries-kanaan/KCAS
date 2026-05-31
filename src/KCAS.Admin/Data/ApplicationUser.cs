using Microsoft.AspNetCore.Identity;

namespace KCAS.Admin.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public string? WindowsAccountName { get; set; }

    public bool IsApproved { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedAtUtc { get; set; }

    public string? ApprovedByUserId { get; set; }
}

