using System.Security.Claims;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class SanctionsAutomationTests(KcasWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly List<int> snapshotIds = [];
    private readonly List<int> checkIds = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshots = await db.SanctionsSourceSnapshots.Where(x => snapshotIds.Contains(x.Id)).ToListAsync();
        var batches = snapshots.Select(x => x.ClientSanctionsBatchId).ToList();
        var employeeBatches = snapshots.Select(x => x.EmployeeTfsBatchId).ToList();
        var evidence = await db.SanctionsAutomatedResults.Where(x => snapshotIds.Contains(x.SanctionsSourceSnapshotId) && x.ClientEvidenceItemId != null)
            .Select(x => x.ClientEvidenceItemId!.Value).ToListAsync();
        var subjects = await db.ClientSanctionsSubjects.Where(x => batches.Contains(x.ClientSanctionsBatchId)).ToListAsync();
        var ids = subjects.Select(x => x.Id).ToList();
        var tasks = subjects.Where(x => x.ComplianceTaskId != null).Select(x => x.ComplianceTaskId!.Value).ToList();
        await db.SanctionsSourceChecks.Where(x => checkIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.SanctionsAutomatedResults.Where(x => snapshotIds.Contains(x.SanctionsSourceSnapshotId)).ExecuteDeleteAsync();
        await db.SanctionsSourceSnapshots.Where(x => snapshotIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ClientSanctionsCoverageRecords.Where(x => ids.Contains(x.ClientSanctionsSubjectId)).ExecuteDeleteAsync();
        await db.ClientSanctionsSubjects.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ClientEvidenceItems.Where(x => evidence.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ClientSanctionsBatches.Where(x => batches.Contains(x.Id)).ExecuteDeleteAsync();
        await db.EmployeeComplianceTasks.Where(x => x.EmployeeTfsBatchId != null && employeeBatches.Contains(x.EmployeeTfsBatchId.Value)).ExecuteDeleteAsync();
        await db.EmployeeTfsBatches.Where(x => employeeBatches.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ComplianceTasks.Where(x => tasks.Contains(x.Id) || x.LinkedEntityType == nameof(SanctionsSourceCheck) && checkIds.Contains(x.LinkedEntityId!.Value)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Real_results_retained_changed_lists_rescreen_hidden_clients_and_employees_without_approval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var suffix = Guid.NewGuid().ToString("N");
        var client = new Client { DisplayName = "Clear TestPerson " + suffix, FullName = "Clear", SurnameOrEntityName = "TestPerson " + suffix,
            ClientCategory = ClientCategories.NaturalPerson, KanaanId = "TEST", ExcludeFromComplianceLists = true };
        var employee = new EmployeeProfile { DisplayName = "Listed Person", LegalName = "Listed Person" };
        var inactive = new EmployeeProfile { DisplayName = "Inactive Person", LegalName = "Inactive Person", EmploymentStatus = "Inactive" };
        db.Clients.Add(client); db.EmployeeProfiles.AddRange(employee, inactive); await db.SaveChangesAsync();
        var feed = new FakeFeed { Payload = SanctionsListTests.Xml() };
        var service = Service(scope, feed);
        var first = await CheckAsync(service, actor);
        Assert.Equal("Updated", first.Outcome);
        Assert.True(first.SubjectsChecked > 0);
        var snapshot = await db.SanctionsSourceSnapshots.SingleAsync(x => x.Id == first.SanctionsSourceSnapshotId);
        Assert.Equal(feed.Payload, snapshot.Payload);
        Assert.Null(snapshot.PublishedAtUtc);
        var result = await db.SanctionsAutomatedResults.SingleAsync(x => x.SanctionsSourceSnapshotId == snapshot.Id && x.ClientEvidenceItemId != null &&
            db.ClientSanctionsSubjects.Any(s => s.Id == x.ClientSanctionsSubjectId && s.ClientId == client.Id));
        Assert.Equal("NoMatch", result.Outcome);
        var item = await db.ClientEvidenceItems.SingleAsync(x => x.Id == result.ClientEvidenceItemId);
        Assert.Equal(SanctionsAutomationService.Performer, item.ScreeningPerformedBy);
        Assert.NotNull(item.ScreeningReviewedAtUtc);
        Assert.Contains(snapshot.ContentSha256, item.ScreeningSources);
        Assert.Equal("PossibleMatch", (await db.SanctionsAutomatedResults.SingleAsync(x => x.SanctionsSourceSnapshotId == snapshot.Id && x.EmployeeProfileId == employee.Id)).Outcome);
        Assert.False(await db.SanctionsAutomatedResults.AnyAsync(x => x.EmployeeProfileId == inactive.Id));
        Assert.False(await db.EmployeeComplianceReviews.AnyAsync(x => x.EmployeeProfileId == employee.Id));
        Assert.Empty(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id));
        var unchanged = await CheckAsync(service, actor);
        Assert.Equal("Unchanged", unchanged.Outcome);
        Assert.Equal(snapshot.Id, unchanged.SanctionsSourceSnapshotId);
        feed.Payload = SanctionsListTests.Xml(client.DisplayName);
        var changed = await CheckAsync(service, actor);
        Assert.Equal("Updated", changed.Outcome);
        Assert.NotEmpty(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id));
        Assert.Equal("ManualReviewRequired", (await db.SanctionsAutomatedResults.SingleAsync(x => x.EmployeeProfileId == employee.Id && x.SanctionsSourceSnapshotId == changed.SanctionsSourceSnapshotId)).Outcome);
        feed.Payload = [];
        var failed = await CheckAsync(service, actor);
        Assert.Equal("Failed", failed.Outcome);
        Assert.Null(failed.SanctionsSourceSnapshotId);
        Assert.Contains(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id), x => x.Contains("source retrieval"));
        Assert.NotNull(await service.ReminderAsync(actor));
        var reader = await ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CheckNowAsync(reader));
        Assert.Null(await service.ReminderAsync(reader));
        Assert.Equal(snapshot.ContentSha256, (await service.SnapshotAsync(snapshot.Id, actor))!.ContentSha256);
    }

    [Fact]
    public async Task Initial_source_failure_blocks_instead_of_inventing_clearance()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var client = new Client { DisplayName = "Initial failure " + Guid.NewGuid().ToString("N"), KanaanId = "TEST" };
        db.Clients.Add(client); await db.SaveChangesAsync();
        var failure = await CheckAsync(Service(scope, new FakeFeed { Payload = [] }), actor);
        Assert.Equal("Failed", failure.Outcome);
        Assert.Contains(await ClientSanctionsCoverageService.BlockersAsync(db, client.Id), x => x.Contains("source retrieval"));
    }

    private SanctionsAutomationService Service(IServiceScope scope, FakeFeed feed) => new(scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(), feed,
        Options.Create(new SanctionsAutomationOptions { MinimumIndividuals = 1, MinimumEntities = 1 }), new SanctionsAutomationLock());
    private async Task<SanctionsSourceCheck> CheckAsync(SanctionsAutomationService service, ClaimsPrincipal actor)
    {
        var check = (await service.CheckNowAsync(actor))!;
        checkIds.Add(check.Id);
        if (check.SanctionsSourceSnapshotId is { } id && !snapshotIds.Contains(id)) snapshotIds.Add(id);
        return check;
    }
    private static async Task<ClaimsPrincipal> ActorAsync(IServiceScope scope, string role)
    {
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = "automation-" + Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        Assert.True((await manager.CreateAsync(user)).Succeeded); Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        if (role == KcasRoles.Administrator) Assert.True((await manager.AddToRoleAsync(user, KcasRoles.ComplianceAdministrator)).Succeeded);
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, email)], "Test"));
    }
    private sealed class FakeFeed : ISanctionsFeed
    {
        public byte[] Payload { get; set; } = [];
        public Task<byte[]> DownloadAsync(CancellationToken cancellationToken) => Task.FromResult(Payload);
    }
}
