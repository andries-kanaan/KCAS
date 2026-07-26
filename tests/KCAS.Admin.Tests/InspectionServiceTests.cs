using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InspectionServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Inspection_pack_requires_ready_evidence_and_actual_passing_readiness_checks_then_freezes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<InspectionService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var asAt = DateOnly.FromDateTime(DateTime.Today);
        var includedClient = new Client
        {
            DisplayName = $"Inspection included {Guid.NewGuid():N}",
            SurnameOrEntityName = "Included",
            CreatedAtUtc = asAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1)
        };
        var excludedClient = new Client
        {
            DisplayName = $"Inspection future {Guid.NewGuid():N}",
            SurnameOrEntityName = "Future",
            CreatedAtUtc = asAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1)
        };
        db.AddRange(includedClient, excludedClient);
        await db.SaveChangesAsync();

        var id = await service.CreateDraftAsync(CompleteCase(asAt), "coordinator@example.test", "Create mock inspection.");
        var created = await service.LoadAsync(id);
        Assert.Equal(InspectionReadinessCheckTypes.All.Count, created!.ReadinessChecks.Count);

        await service.SaveRequestItemAsync(id, new InspectionRequestItemEditModel
        {
            Category = InspectionEvidenceCategories.Clients,
            Title = "Current client index",
            Owner = "Administrator",
            DueDate = asAt.AddDays(10),
            Status = InspectionItemStatuses.Ready,
            EvidenceTitle = "KCAS client evidence index",
            EvidenceLocation = "Generated in frozen inspection pack",
            LinkedEntityType = nameof(Client),
            LinkedEntityId = includedClient.Id
        }, "coordinator@example.test", "Complete requested evidence.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.FreezeAsync(id, "coordinator@example.test", "Attempt before readiness tests."));
        var checks = (await service.LoadAsync(id))!.ReadinessChecks;
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RecordReadinessCheckAsync(id, checks[0].Id, InspectionCheckStatuses.Passed, "", "",
                "tester@example.test", "Attempt unsupported pass."));

        foreach (var check in checks)
        {
            await service.RecordReadinessCheckAsync(id, check.Id, InspectionCheckStatuses.Passed,
                $"Evidence/{check.CheckType}.pdf", $"Completed and reviewed {InspectionReadinessCheckTypes.Display(check.CheckType)}.",
                "tester@example.test", $"Record {check.CheckType} test.");
        }
        await service.FreezeAsync(id, "coordinator@example.test", "Freeze reproducible inspection pack.");

        var frozen = await db.InspectionCases.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(InspectionStatuses.Frozen, frozen.Status);
        Assert.False(string.IsNullOrWhiteSpace(frozen.SnapshotJson));
        using var snapshot = JsonDocument.Parse(frozen.SnapshotJson!);
        var clients = snapshot.RootElement.GetProperty("evidenceIndex").GetProperty("clients");
        Assert.Contains(clients.EnumerateArray(), item => item.GetProperty("id").GetInt32() == includedClient.Id);
        Assert.DoesNotContain(clients.EnumerateArray(), item => item.GetProperty("id").GetInt32() == excludedClient.Id);
        Assert.Equal(8, snapshot.RootElement.GetProperty("readinessChecks").GetArrayLength());
        Assert.Equal(frozen.SnapshotJson, (await service.LoadPrintableAsync(id)).SnapshotJson);

        var frozenEdit = CompleteCase(asAt);
        frozenEdit.Id = id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveDraftAsync(frozenEdit, "coordinator@example.test", "Try editing frozen pack."));
        await service.CloseAsync(id, "coordinator@example.test", "Inspection response completed.");
        Assert.Equal(InspectionStatuses.Closed,
            await db.InspectionCases.Where(item => item.Id == id).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task Ready_request_item_requires_evidence_title_and_location()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<InspectionService>();
        var asAt = DateOnly.FromDateTime(DateTime.Today);
        var id = await service.CreateDraftAsync(CompleteCase(asAt), "coordinator@example.test", "Create evidence validation case.");

        await Assert.ThrowsAsync<ValidationException>(() => service.SaveRequestItemAsync(id, new InspectionRequestItemEditModel
        {
            Category = InspectionEvidenceCategories.Rmcp,
            Title = "Approved RMCP",
            Owner = "Key Individuals",
            DueDate = asAt.AddDays(10),
            Status = InspectionItemStatuses.Ready
        }, "coordinator@example.test", "Attempt ready without evidence."));
    }

    [Fact]
    public async Task Inspection_dates_are_validated()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<InspectionService>();
        var model = CompleteCase(DateOnly.FromDateTime(DateTime.Today));
        model.DueDate = model.RequestDate.AddDays(-1);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateDraftAsync(model, "coordinator@example.test", "Create invalid date case."));
    }

    private static InspectionCaseEditModel CompleteCase(DateOnly asAt) => new()
    {
        Reference = $"MOCK-{Guid.NewGuid():N}",
        Title = "Mock FSCA inspection",
        RequestingAuthority = "FSCA",
        AsAtDate = asAt,
        RequestDate = asAt,
        DueDate = asAt.AddDays(30),
        Scope = "Kanaan proportional RMCP and client-risk controls.",
        Coordinator = "Compliance Administrator",
        Notes = "Synthetic automated test only."
    };
}
