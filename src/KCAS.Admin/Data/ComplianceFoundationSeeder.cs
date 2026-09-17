using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public static class ComplianceFoundationSeeder
{
    private static readonly DateOnly FactualConfirmationDate = new(2026, 7, 31);

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var changed = false;

        var profile = await db.ComplianceProfiles.OrderBy(item => item.Id).LastOrDefaultAsync();
        if (profile is null)
        {
            profile = new ComplianceProfile();
            db.ComplianceProfiles.Add(profile);
            AddAudit(db, nameof(ComplianceProfile), "FoundationSeeded", "Profile populated from the signed 2026 Business and Governance Factual Confirmation.");
        }
        if (profile.Id == 0 || profile.UpdatedBy == "System")
        {
            profile.LegalName = "Kanaan Trust (IT 676/97)";
            profile.TradingName = "Kanaan Asset Managers";
            profile.FspNumber = "528";
            profile.PrimaryContactName = "Gert Delport (CEO)";
            profile.PrimaryContactEmail = "secretary@kanaantrust.com";
            profile.PrimaryContactPhone = "+27 31 561 2208";
            profile.RegisteredAddress = "1 Peter Hulett Road, Salt Rock, 4420";
            profile.OperatingAddress = "1 Peter Hulett Road, Salt Rock, 4420";
            profile.EffectiveFrom = FactualConfirmationDate;
            profile.Status = ComplianceStatuses.Active;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            profile.UpdatedBy = "System";
            changed = true;
        }

        var desiredRoles = new[]
        {
            Role("Gert Delport", "Chief Executive Officer; Trustee; Key Individual; Representative; MLCO; section 42A responsible person", "Executive leadership; RMCP implementation; reporting decisions; CDD and screening oversight."),
            Role("Andre Delport", "Founder; Board Chair; Trustee; Key Individual; Representative", "Board leadership, governance oversight and Key Individual responsibility."),
            Role("The Corporate Counsel", "External FAIS Compliance Officer", "External FAIS compliance monitoring and advisory support.", "Administration@corporatecounsel.co.za", "073 627 3051"),
            Role("Andries van Tonder", "Internal Compliance Officer; Trustee; Representative; primary goAML user", "Internal compliance coordination, direct Board reporting, remediation follow-up and goAML administration.", "andries@kanaantrust.com"),
            Role("Johannes Delport", "Trustee; IT Innovation Manager", "Internal IT and KCAS operational oversight."),
            Role("Hannetjie Delport", "Trustee; Accountant", "Trustee governance and accounting responsibilities."),
            Role("Jennifer Delport", "PA; Receptionist", "Administrative support.")
        };
        var existingRoles = await db.GovernanceRoleAssignments.ToListAsync();
        foreach (var desired in desiredRoles)
        {
            var existing = existingRoles.FirstOrDefault(item => item.PersonName == desired.PersonName);
            if (existing is null)
            {
                db.GovernanceRoleAssignments.Add(desired);
                changed = true;
            }
            else if (existing.UpdatedBy == "System")
            {
                existing.RoleType = desired.RoleType;
                existing.Email = desired.Email;
                existing.Phone = desired.Phone;
                existing.ResponsibilitySummary = desired.ResponsibilitySummary;
                existing.IsActive = true;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                changed = true;
            }
        }
        if (existingRoles.Count == 0)
        {
            AddAudit(db, nameof(GovernanceRoleAssignment), "FoundationSeeded", "Governance roles populated from the signed 2026 factual confirmation and governance structure.");
        }

        if (!await db.ComplianceReferenceValues.AnyAsync())
        {
            db.ComplianceReferenceValues.AddRange(
                Reference("Regulatory authority", "FSCA", "Financial Sector Conduct Authority", "Supervisory authority for Kanaan's FSP and accountable-institution obligations.", 1),
                Reference("Regulatory authority", "FIC", "Financial Intelligence Centre", "Authority for FIC Act guidance, reporting and goAML communications.", 2),
                Reference("Regulatory source", "FIC_ACT", "Financial Intelligence Centre Act 38 of 2001", "Primary legislation identified in the approved RMCP.", 3),
                Reference("Regulatory source", "FIC_GN7A", "FIC Revised Guidance Note 7A (1 September 2025)", "Risk-based CDD guidance identified in the approved RMCP.", 4),
                Reference("Regulatory source", "GOAML", "FIC reporting guidance and goAML notices", "Operational reporting guidance identified in the approved RMCP.", 5),
                Reference("Regulatory source", "FSCA_MATERIAL", "Applicable FSCA supervisory material", "Supervisory material applicable to accountable institutions, as identified in the approved RMCP.", 6));
            AddAudit(db, nameof(ComplianceReferenceValue), "FoundationSeeded", "Regulatory references populated from the approved 2026 RMCP.");
            changed = true;
        }

        if (changed) await db.SaveChangesAsync();
    }

    private static GovernanceRoleAssignment Role(string person, string roles, string responsibility, string? email = null, string? phone = null) => new()
    {
        PersonName = person,
        RoleType = roles,
        ResponsibilitySummary = responsibility,
        Email = email,
        Phone = phone,
        IsActive = true,
        UpdatedAtUtc = DateTime.UtcNow,
        UpdatedBy = "System"
    };

    private static ComplianceReferenceValue Reference(string category, string code, string name, string description, int order) => new()
    {
        Category = category,
        Code = code,
        Name = name,
        Description = description,
        SortOrder = order,
        IsActive = true,
        UpdatedAtUtc = DateTime.UtcNow,
        UpdatedBy = "System"
    };

    private static void AddAudit(ApplicationDbContext db, string entityType, string action, string reason) =>
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = entityType,
            EntityId = 0,
            Action = action,
            UserName = "System",
            Reason = reason,
            NewValueJson = JsonSerializer.Serialize(new { Source = "Signed-final FSCA programme documents" })
        });
}
