using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientVisibilityServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Bulk_visibility_changes_only_matching_clients_and_can_unhide_all()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = new ClientVisibilityService(db);
        var suffix = Guid.NewGuid().ToString("N");
        var zero = NewClient($"Zero {suffix}", 0m, 1_500_000_001);
        var positive = NewClient($"Positive {suffix}", 250m, 1_500_000_002);
        var unknown = new Client
        {
            DisplayName = $"Unknown {suffix}",
            SurnameOrEntityName = "Unknown"
        };
        db.Clients.AddRange(zero, positive, unknown);
        await db.SaveChangesAsync();

        var preview = await service.PreviewAsync(ClientVisibilityActions.HideZeroBalance);
        Assert.True(preview >= 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(ClientVisibilityActions.HideZeroBalance, preview + 1, "admin@example.test"));
        Assert.False(zero.ExcludeFromComplianceLists);

        await service.ApplyAsync(ClientVisibilityActions.HideZeroBalance, preview, "admin@example.test");
        Assert.True(zero.ExcludeFromComplianceLists);
        Assert.False(positive.ExcludeFromComplianceLists);
        Assert.False(unknown.ExcludeFromComplianceLists);

        var unhideZeroCount = await service.PreviewAsync(ClientVisibilityActions.UnhideZeroBalance);
        await service.ApplyAsync(ClientVisibilityActions.UnhideZeroBalance, unhideZeroCount, "admin@example.test");
        Assert.False(zero.ExcludeFromComplianceLists);

        zero.ExcludeFromComplianceLists = true;
        positive.ExcludeFromComplianceLists = true;
        await db.SaveChangesAsync();
        var unhideAllCount = await service.PreviewAsync(ClientVisibilityActions.UnhideAll);
        await service.ApplyAsync(ClientVisibilityActions.UnhideAll, unhideAllCount, "admin@example.test");
        Assert.False(zero.ExcludeFromComplianceLists);
        Assert.False(positive.ExcludeFromComplianceLists);
        await transaction.RollbackAsync();
    }

    private static Client NewClient(string name, decimal amount, int legacyFundId) => new()
    {
        DisplayName = name,
        SurnameOrEntityName = name,
        FundValuations =
        {
            new ClientFundValuation
            {
                LegacyFundId = legacyFundId,
                InvestmentUniqueNumber = $"VIS-{Guid.NewGuid():N}",
                FundName = "Visibility test",
                AmountZar = amount
            }
        }
    };
}
