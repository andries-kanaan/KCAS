using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientEvidenceReadinessServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Readiness_defaults_block_clients_until_required_evidence_is_verified()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Evidence Client", "EVID-001", @"z:\Kanaan Trust\Clients\Evidence Client");

        var readiness = await service.LoadClientReadinessAsync(clientId);

        Assert.True(readiness.RequiredCount >= 10);
        Assert.False(readiness.IsReadyForRiskAssessment);
        Assert.True(readiness.BlockedCount > 0);
    }

    [Fact]
    public async Task Requirement_matrix_varies_for_trust_clients()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var naturalPersonId = await CreateClientAsync(db, "Natural Matrix Client", "NAT-001", @"z:\Kanaan Trust\Clients\Natural Matrix Client");
        var trustId = await CreateClientAsync(db, "Trust Matrix Client", "TRUST-001", @"z:\Kanaan Trust\Clients\Trust Matrix Client", ClientCategories.Trust);

        var naturalPerson = await service.LoadClientReadinessAsync(naturalPersonId);
        var trust = await service.LoadClientReadinessAsync(trustId);

        Assert.True(trust.RequiredCount > naturalPerson.RequiredCount);
        Assert.Contains(trust.Requirements, requirement => requirement.EvidenceType == "TrustDeed");
        Assert.DoesNotContain(naturalPerson.Requirements, requirement => requirement.EvidenceType == "TrustDeed");
    }

    [Fact]
    public async Task Verified_evidence_and_exceptions_can_make_client_ready()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Ready Client", "READY-001", @"z:\Kanaan Trust\Clients\Ready Client");

        var initial = await service.LoadClientReadinessAsync(clientId);
        foreach (var requirement in initial.Requirements)
        {
            if (requirement.RequirementId == initial.Requirements[0].RequirementId)
            {
                db.ClientEvidenceItems.Add(new ClientEvidenceItem
                {
                    ClientId = clientId,
                    ClientEvidenceRequirementId = requirement.RequirementId,
                    EvidenceType = requirement.EvidenceType,
                    Title = requirement.Title,
                    Status = ClientEvidenceStatuses.Linked
                });
                await db.SaveChangesAsync();
                var itemId = await db.ClientEvidenceItems
                    .Where(item => item.ClientId == clientId && item.ClientEvidenceRequirementId == requirement.RequirementId)
                    .Select(item => item.Id)
                    .SingleAsync();
                await service.VerifyEvidenceAsync(itemId, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddYears(1)), "reviewer@example.test", "Verify sampled evidence.");
            }
            else
            {
                await service.CreateExceptionAsync(clientId, requirement.RequirementId, "Temporary audit-readiness exception.", DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), "approver@example.test", "Approve temporary exception.");
            }
        }

        var readiness = await service.LoadClientReadinessAsync(clientId);

        Assert.True(readiness.IsReadyForRiskAssessment);
        Assert.Equal(0, readiness.BlockedCount);
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(audit => audit.EntityType == "ClientEvidenceException"));
    }

    [Fact]
    public async Task Scan_links_matching_files_and_is_idempotent()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Folder Match Client", "FOLDER-001", @"z:\Kanaan Trust\Clients\Folder Match Client");
        var root = CreateTempRoot();
        var clientFolder = Directory.CreateDirectory(Path.Combine(root, "Folder Match Client"));
        await File.WriteAllTextAsync(Path.Combine(clientFolder.FullName, "identity document.pdf"), "identity");

        var firstRunId = await service.RunScanAsync(root, "scanner@example.test", "Initial evidence scan.");
        var secondRunId = await service.RunScanAsync(root, "scanner@example.test", "Repeat evidence scan.");

        Assert.NotEqual(firstRunId, secondRunId);
        Assert.Equal(1, await db.ClientEvidenceItems.CountAsync(item => item.ClientId == clientId));
        Assert.Equal(2, await db.ClientEvidenceScanRuns.CountAsync(run => run.Id == firstRunId || run.Id == secondRunId));
        Assert.Equal(2, await db.ClientEvidenceScanFiles.CountAsync(file => file.ClientId == clientId && file.MatchStatus == ClientEvidenceScanFileStatuses.Linked));
    }

    [Fact]
    public async Task Scan_keeps_unmatched_files_for_review()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "unknown passport.pdf"), "identity");

        await service.RunScanAsync(root, "scanner@example.test", "Scan unmatched evidence.");

        var file = await db.ClientEvidenceScanFiles
            .OrderByDescending(file => file.Id)
            .FirstAsync();
        Assert.Equal(ClientEvidenceScanFileStatuses.Unmatched, file.MatchStatus);
        Assert.Null(file.ClientId);
    }

    [Fact]
    public async Task Scan_root_requires_existing_server_path_and_reason()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();

        await Assert.ThrowsAsync<ValidationException>(() => service.SaveScanRootAsync("", "admin@example.test", ""));
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveScanRootAsync(@"C:\definitely-not-a-kcas-folder", "admin@example.test", "Set scan root."));
    }

    private static async Task<int> CreateClientAsync(ApplicationDbContext db, string name, string kanaanId, string folder, string category = ClientCategories.NaturalPerson)
    {
        var client = new Client
        {
            DisplayName = name,
            SurnameOrEntityName = name,
            KanaanId = kanaanId,
            ClientFolder = folder,
            ClientCategory = category
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "kcas-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
