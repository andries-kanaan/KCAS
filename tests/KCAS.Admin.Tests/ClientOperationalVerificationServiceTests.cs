using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientOperationalVerificationServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Lifecycle_classification_requires_reason_and_duplicate_target()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var client = NewClient("Lifecycle source");
        var canonical = NewClient("Lifecycle canonical");
        db.AddRange(client, canonical);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ClassifyLifecycleAsync(client.Id, new(ClientLifecycleStatuses.Current, null, ""), "reviewer@example.test"));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ClassifyLifecycleAsync(client.Id, new(ClientLifecycleStatuses.Duplicate, null, "Duplicate found."), "reviewer@example.test"));

        await service.ClassifyLifecycleAsync(
            client.Id,
            new(ClientLifecycleStatuses.Duplicate, canonical.Id, "Same identity confirmed from source folder."),
            "reviewer@example.test");

        var saved = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.Equal(ClientLifecycleStatuses.Duplicate, saved.LifecycleStatus);
        Assert.Equal(canonical.Id, saved.DuplicateOfClientId);
        Assert.False(saved.IsActive);
        Assert.Equal("reviewer@example.test", saved.LifecycleReviewedBy);
    }

    [Fact]
    public async Task Replacement_remains_pending_until_human_accepts_and_then_applies()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var client = NewClient("Original display");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var itemId = await service.AddVerificationItemAsync(
            client.Id,
            new(
                ClientVerificationFields.DisplayName,
                ClientVerificationChangeTypes.Replace,
                "Verified display",
                @"Client folder\identity.pdf",
                "Identity document uses the full verified name.",
                true),
            "codex-assisted@example.test");

        Assert.Equal("Original display", (await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id)).DisplayName);
        Assert.Equal(1, await service.CountBlockingPendingAsync(client.Id));

        await service.VerifyAsync(itemId, "andries@example.test", "Compared with the identity document.");

        var savedClient = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        var savedItem = await db.ClientVerificationItems.AsNoTracking().SingleAsync(item => item.Id == itemId);
        Assert.Equal("Verified display", savedClient.DisplayName);
        Assert.Equal(ClientVerificationStatuses.Verified, savedItem.Status);
        Assert.Equal("andries@example.test", savedItem.DecidedBy);
        Assert.NotNull(savedItem.AppliedAtUtc);
        Assert.Equal(0, await service.CountBlockingPendingAsync(client.Id));
    }

    [Fact]
    public async Task Stale_recommendation_cannot_overwrite_a_later_edit()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationalVerificationService>();
        var client = NewClient("Original");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var itemId = await service.AddVerificationItemAsync(
            client.Id,
            new(
                ClientVerificationFields.DisplayName,
                ClientVerificationChangeTypes.Replace,
                "Proposal",
                "source.pdf",
                "Recommend a controlled change.",
                true),
            "codex-assisted@example.test");

        client.DisplayName = "Edited elsewhere";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAsync(itemId, "andries@example.test", "Attempt stale decision."));
        Assert.Equal(ClientVerificationStatuses.Pending,
            (await db.ClientVerificationItems.AsNoTracking().SingleAsync(item => item.Id == itemId)).Status);
    }

    private static Client NewClient(string name) => new()
    {
        KanaanId = $"VERIFY-{Guid.NewGuid():N}"[..22],
        FullName = name,
        SurnameOrEntityName = name,
        DisplayName = name
    };
}
