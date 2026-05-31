using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class Client
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string ClientCode { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(13)]
    public string? SouthAfricanIdNumber { get; set; }

    [MaxLength(254)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? MobileNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}
