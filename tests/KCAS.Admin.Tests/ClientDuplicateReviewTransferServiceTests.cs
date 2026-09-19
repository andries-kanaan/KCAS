using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientDuplicateReviewTransferServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Resolved_duplicate_exports_previews_and_applies_without_an_assessment()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientDuplicateReviewTransferService>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var canonical = new Client
        {
            LegacyClientId = Random.Shared.Next(7000000, 7999999),
            KanaanId = $"CAN-{suffix}",
            DisplayName = "Canonical Test Client",
            SurnameOrEntityName = "Client",
            LifecycleStatus = ClientLifecycleStatuses.Historical
        };
        db.Clients.Add(canonical);
        await db.SaveChangesAsync();
        var duplicate = new Client
        {
            LegacyClientId = Random.Shared.Next(8000000, 8999999),
            KanaanId = $"DUP-{suffix}",
            DisplayName = "Duplicate Test Client",
            SurnameOrEntityName = "Client",
            LifecycleStatus = ClientLifecycleStatuses.Duplicate,
            DuplicateOfClientId = canonical.Id,
            LifecycleReason = "Same historical holding as the canonical record.",
            LifecycleReviewedBy = "Codex",
            LifecycleReviewedAtUtc = DateTime.UtcNow
        };
        db.Clients.Add(duplicate);
        await db.SaveChangesAsync();

        var source = await service.LoadSourceAsync(duplicate.Id);
        Assert.Equal(duplicate.KanaanId, source.Duplicate.KanaanId);
        Assert.Equal(canonical.LegacyClientId, source.Canonical.LegacyClientId);
        Assert.Equal(duplicate.LifecycleReason, source.Reason);

        const string passphrase = "testpassphrase";
        var export = await service.ExportAsync(
            duplicate.Id, passphrase, "exporter@example.test", "Transfer reviewed duplicate.");
        var bytes = await File.ReadAllBytesAsync(export.StoragePath);
        Assert.EndsWith(".kcas-duplicate-review", export.FileName);

        duplicate.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        duplicate.DuplicateOfClientId = null;
        duplicate.LifecycleReason = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var preview = await service.PreviewAsync(bytes, passphrase);
        Assert.True(preview.CanApply);
        Assert.Equal(duplicate.Id, preview.DuplicateClientId);
        Assert.Equal(canonical.Id, preview.CanonicalClientId);
        await service.ApplyAsync(bytes, passphrase, "importer@example.test", "Approved live match.");

        var liveDuplicate = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == duplicate.Id);
        Assert.Equal(ClientLifecycleStatuses.Duplicate, liveDuplicate.LifecycleStatus);
        Assert.Equal(canonical.Id, liveDuplicate.DuplicateOfClientId);
        Assert.Equal("Same historical holding as the canonical record.", liveDuplicate.LifecycleReason);
        Assert.False(liveDuplicate.IsActive);
        Assert.False((await service.PreviewAsync(bytes, passphrase)).CanApply);
        Assert.True((await service.PreviewAsync(bytes, passphrase)).AlreadyApplied);
    }

    [Fact]
    public async Task Preview_blocks_a_changed_live_canonical_identity()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientDuplicateReviewTransferService>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var canonical = new Client
        {
            LegacyClientId = Random.Shared.Next(9000000, 9999999),
            KanaanId = $"CAN-{suffix}",
            DisplayName = "Canonical Identity",
            SurnameOrEntityName = "Identity",
            LifecycleStatus = ClientLifecycleStatuses.Historical
        };
        db.Clients.Add(canonical);
        await db.SaveChangesAsync();
        var duplicate = new Client
        {
            LegacyClientId = Random.Shared.Next(10000000, 10999999),
            KanaanId = $"DUP-{suffix}",
            DisplayName = "Duplicate Identity",
            SurnameOrEntityName = "Identity",
            LifecycleStatus = ClientLifecycleStatuses.Duplicate,
            DuplicateOfClientId = canonical.Id,
            LifecycleReason = "Verified duplicate of canonical identity."
        };
        db.Clients.Add(duplicate);
        await db.SaveChangesAsync();
        var export = await service.ExportAsync(
            duplicate.Id, "testpassphrase", "exporter@example.test", "Transfer reviewed duplicate.");

        canonical.KanaanId = $"OTHER-{suffix}";
        duplicate.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        duplicate.DuplicateOfClientId = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ValidationException>(() => service.LoadSourceAsync(duplicate.Id));
        var preview = await service.PreviewAsync(await File.ReadAllBytesAsync(export.StoragePath), "testpassphrase");
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Conflicts, conflict => conflict.Contains("canonical client", StringComparison.OrdinalIgnoreCase));
    }
}
