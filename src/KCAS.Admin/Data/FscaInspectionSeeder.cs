using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public static class FscaInspectionSeeder
{
    public const string Reference = "FSCA FSP 528 - 20 July 2026";
    private static readonly DateOnly NoticeDate = new(2026, 7, 20);
    private static readonly DateOnly SubmissionDueDate = new(2026, 8, 3);
    private static readonly DateOnly InspectionDate = new(2026, 9, 22);

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await db.InspectionCases.AnyAsync(item => item.Reference == Reference ||
                (item.RequestingAuthority == "Financial Sector Conduct Authority" && item.DueDate == InspectionDate)))
        {
            return;
        }

        var evidenceRoot = ResolveEvidenceRoot();
        var inspection = new InspectionCase
        {
            Reference = Reference,
            Title = "2026 FIC Act onsite inspection",
            RequestingAuthority = "Financial Sector Conduct Authority",
            RequestDate = NoticeDate,
            DueDate = InspectionDate,
            AsAtDate = InspectionDate,
            Status = InspectionStatuses.Open,
            Scope = "Technical compliance and operating effectiveness under the FIC Act: ML/TF/PF business-risk understanding and mitigation; the RMCP; AML/CFT/CPF governance under section 42A; and FIC Act training under section 43.",
            Coordinator = "Andries van Tonder",
            Notes = "Onsite inspection starts at 09:00 on 22 September 2026 at Kanaan Trust's Salt Rock offices. Senior management and the section 42A responsible person must be available. Source: FSCA Notice of Inspection dated 20 July 2026.",
            CreatedBy = "System",
            UpdatedBy = "System",
            Items = BuildSubmittedItems(evidenceRoot),
            ReadinessChecks = InspectionReadinessCheckTypes.All.Select(type => new InspectionReadinessCheck
            {
                CheckType = type,
                Status = InspectionCheckStatuses.Pending,
                Notes = "Requires dated operating evidence before the onsite inspection."
            }).ToList()
        };

        db.InspectionCases.Add(inspection);
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(InspectionCase),
            EntityId = inspection.Id,
            Action = "FSCAInspectionSeeded",
            UserName = "System",
            Reason = "Created from the FSCA notice dated 20 July 2026 and the signed-final response pack submitted on 31 July 2026.",
            NewValueJson = JsonSerializer.Serialize(new { inspection.Reference, inspection.RequestDate, inspection.DueDate, RequestItems = inspection.Items.Count })
        });
        await db.SaveChangesAsync();
    }

    private static List<InspectionRequestItem> BuildSubmittedItems(string root)
    {
        var items = new[]
        {
            Item("Governance", "5.1 Services and financial-services business", "Detailed explanation of all services offered by Kanaan Trust.", "5.1 and 5.2 Business and Governance Factual Confirmation.pdf"),
            Item("Governance", "5.2 Board and senior-management structure", "Description of the Board, committees and senior-management structures.", "5.1 and 5.2 Business and Governance Factual Confirmation.pdf"),
            Item("Governance", "5.3 Company organogram", "Kanaan governance and organisational structure.", "5.3 Kanaan Governance and Organisational Structure 2026 - Revised.pdf"),
            Item("BRA", "5.4 Business Risk Assessment", "Signed 2026 Business Risk Assessment.", "5.4 Kanaan Business Risk Assessment 2026 signed.pdf"),
            Item("RMCP", "5.5 Current and previous RMCP", "Signed 2026 RMCP and the previous approved 2025 RMCP for comparative review.", "5.5a Kanaan RMCP 2026 signed.pdf", "5.5b Kanaan RMCP 2025 - approved 3 July 2025.pdf"),
            Item("Training", "5.6 FIC Act training records", "Training records, attendees, dates, providers, materials and evidence for 2023 to 2025.", "5.6a 2023 Training.zip", "5.6b 2024 Training.zip", "5.6c 2025 - 2026 Training.zip"),
            Item("Monitoring", "5.7 Internal audit position", "Latest internal-audit report or the signed explanatory position where no internal-audit function exists.", "5.7 Internal Audit Position Explanatory Letter.pdf"),
            Item("Monitoring", "5.8 External audit position", "Latest external-audit report or the signed explanatory position, with monitoring support.", "5.8a External Audit Position Explanatory Letter.pdf", "5.8b Monitoring Dashboard and supporting docs.zip")
        };

        return items.Select((item, index) => new InspectionRequestItem
        {
            Category = item.Category,
            Title = item.Title,
            Description = item.Description,
            Owner = "Andries van Tonder",
            DueDate = SubmissionDueDate,
            Status = InspectionItemStatuses.Ready,
            EvidenceTitle = item.Title,
            EvidenceLocation = string.Join(" | ", item.Files.Select(file => Path.Combine(root, file))),
            ReviewNotes = "Included in the signed-final response set uploaded to the FSCA on 31 July 2026.",
            CompletedAtUtc = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc),
            CompletedBy = "System - recorded from submission evidence",
            SortOrder = index + 1
        }).ToList();
    }

    private static (string Category, string Title, string Description, string[] Files) Item(
        string category, string title, string description, params string[] files) => (category, title, description, files);

    private static string ResolveEvidenceRoot()
    {
        const string relative = @"Kanaan Trust\Compliance\FSCA inspections\2026\RMCP and Policy Approval\06 Signed final";
        var candidates = new[]
        {
            @"C:\Download\_kanaan\Compliance\FSCA inspections\2026\RMCP and Policy Approval\06 Signed final",
            Path.Combine(@"E:\Userdata", relative),
            Path.Combine(@"Z:\", relative)
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }
}
