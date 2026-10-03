using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

internal static class ComplianceWorkflowAccess
{
    public static async Task<ApplicationUser> ActorAsync(ApplicationDbContext db, ClaimsPrincipal principal, string permission)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
        if (principal.Identity?.IsAuthenticated != true || user?.IsApproved != true || !await PermissionAsync(db, user.Id, permission))
            throw new UnauthorizedAccessException("An approved account with current compliance permission is required.");
        return user;
    }
    public static Task<bool> PermissionAsync(ApplicationDbContext db, string id, string permission) =>
        (from link in db.UserRoles where link.UserId == id join claim in db.RoleClaims on link.RoleId equals claim.RoleId
         where claim.ClaimType == KcasClaimTypes.Permission && claim.ClaimValue == permission select link).AnyAsync();
    public static Task<bool> IsAdminAsync(ApplicationDbContext db, string id) =>
        (from link in db.UserRoles where link.UserId == id join role in db.Roles on link.RoleId equals role.Id
         where role.Name == KcasRoles.Administrator select link).AnyAsync();
    public static Task<bool> ReceivesReviewsAsync(ApplicationDbContext db, string id) =>
        (from link in db.UserRoles where link.UserId == id join role in db.Roles on link.RoleId equals role.Id
         where role.Name == KcasRoles.ComplianceAdministrator || role.Name == KcasRoles.ComplianceApprover select link).AnyAsync();
    public static async Task VisibleAsync(ApplicationDbContext db, Client client, string actorId)
    {
        if (client.ExcludeFromComplianceLists && !await IsAdminAsync(db, actorId))
            throw new UnauthorizedAccessException("This client is restricted to administrators.");
    }
    public static string Required(string? value, string label, int maximum = 20000) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim() :
        throw new ValidationException($"{label} is required (maximum {maximum} characters).");
    public static DateTime ActualTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        if (utc.Year < 2000 || utc > DateTime.UtcNow.AddMinutes(1)) throw new ValidationException("Use the actual date/time, not a missing or future date.");
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
    public static void Expect(string current, string? expected)
    {
        if (current != expected) throw new ValidationException("This record changed. Reload before saving.");
    }
    public static string NewVersion() => Guid.NewGuid().ToString("N");
    public static string Label(ApplicationUser actor) => actor.Email ?? actor.UserName ?? actor.Id;
    public static void Audit(ApplicationDbContext db, string type, int id, string action, ApplicationUser actor, string reason, object value) =>
        db.ComplianceAuditEvents.Add(new() { EntityType = type, EntityId = id, Action = action, UserName = Label(actor),
            Reason = Required(reason, "Reason"), NewValueJson = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
}
