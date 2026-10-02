using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientVisibilityService(ApplicationDbContext db)
{
    public async Task<int> PreviewAsync(string action)
    {
        ValidateAction(action);
        return (await LoadTargetsAsync(action)).Count;
    }

    public async Task<int> ApplyAsync(string action, int expectedCount, string? userName)
    {
        ValidateAction(action);
        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("A signed-in administrator is required.");

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync()
            : null;
        var targets = await LoadTargetsAsync(action);
        if (targets.Count != expectedCount)
            throw new InvalidOperationException("The matching client count changed. Preview the action again before applying it.");

        var hide = action == ClientVisibilityActions.HideZeroBalance;
        foreach (var client in targets)
        {
            var oldValue = client.ExcludeFromComplianceLists;
            client.ExcludeFromComplianceLists = hide;
            db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
            {
                EntityType = nameof(Client),
                EntityId = client.Id,
                Action = "ClientListVisibilityChanged",
                OldValueJson = JsonSerializer.Serialize(new { ExcludeFromComplianceLists = oldValue }),
                NewValueJson = JsonSerializer.Serialize(new { client.ExcludeFromComplianceLists, BulkAction = action }),
                UserName = userName.Trim(),
                Reason = $"Administrator applied {ClientVisibilityActions.Label(action)}."
            });
        }

        await db.SaveChangesAsync();
        if (transaction is not null)
            await transaction.CommitAsync();
        return targets.Count;
    }

    private async Task<List<Client>> LoadTargetsAsync(string action)
    {
        if (action == ClientVisibilityActions.UnhideAll)
            return await db.Clients.Where(client => client.ExcludeFromComplianceLists).ToListAsync();

        var hide = action == ClientVisibilityActions.HideZeroBalance;
        var clients = await db.Clients
            .Where(client => client.ExcludeFromComplianceLists != hide)
            .Include(client => client.InvestmentAccounts)
            .Include(client => client.FundValuations)
            .AsSplitQuery()
            .ToListAsync();

        return clients.Where(client =>
            ClientSearchService.BuildInvestmentPosition(
                client.InvestmentAccounts, client.FundValuations).TotalCurrentValueZar == 0m)
            .ToList();
    }

    private static void ValidateAction(string action)
    {
        if (!ClientVisibilityActions.All.Contains(action))
            throw new ArgumentException("Select a valid client visibility action.", nameof(action));
    }
}

public static class ClientVisibilityActions
{
    public const string HideZeroBalance = "HideZeroBalance";
    public const string UnhideZeroBalance = "UnhideZeroBalance";
    public const string UnhideAll = "UnhideAll";

    public static readonly string[] All = [HideZeroBalance, UnhideZeroBalance, UnhideAll];

    public static string Label(string action) => action switch
    {
        HideZeroBalance => "hide zero-balance clients",
        UnhideZeroBalance => "unhide zero-balance clients",
        UnhideAll => "unhide all clients",
        _ => action
    };
}
