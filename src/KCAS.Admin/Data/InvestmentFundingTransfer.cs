using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentFundingAccountReference
{
    public int? LegacyClientId { get; set; }
    public string? KanaanId { get; set; }
    public string ClientName { get; set; } = "";
    public int? LegacyAccountId { get; set; }
    public string? AccountNumber { get; set; }
    public string? Administrator { get; set; }
    public DateOnly? InvestmentDate { get; set; }
    public string? ProductName { get; set; }
    public string? FundName { get; set; }
}

public sealed class InvestmentFundingConnectionPackage
{
    public InvestmentFundingAccountReference Source { get; set; } = new();
    public InvestmentFundingAccountReference Destination { get; set; } = new();
    public DateOnly MatchedThroughDate { get; set; }
    public string SourceSnapshot { get; set; } = "";
    public string DestinationSnapshot { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public string Reason { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
}

internal static class InvestmentFundingTransfer
{
    public static async Task<List<InvestmentFundingConnectionPackage>> ExportAsync(ApplicationDbContext db, Client client, CancellationToken ct)
    {
        var accounts = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Client).Include(x => x.Transactions)
            .Where(x => x.ClientId == client.Id || (client.KanaanId != null && client.KanaanId != "" && x.Client.KanaanId == client.KanaanId)).ToListAsync(ct);
        var ids = accounts.Select(x => x.Id).ToList();
        var reviews = await db.ClientInvestmentReconciliationReviews.AsNoTracking().Where(x => ids.Contains(x.ClientInvestmentAccountId)).ToListAsync(ct);
        var connections = await db.InvestmentFundingConnections.AsNoTracking().Where(x => ids.Contains(x.SourceAccountId) && ids.Contains(x.DestinationAccountId)).ToListAsync(ct);
        var clientIds = accounts.Select(x => x.ClientId).Distinct().ToList();
        var valuations = await db.ClientFundValuations.AsNoTracking().Where(x => clientIds.Contains(x.ClientId)).ToListAsync(ct);
        var result = new List<InvestmentFundingConnectionPackage>();
        var exportedSources = new HashSet<int>();
        var connectedIds = accounts.Where(x => x.ClientId == client.Id).Select(x => x.Id).ToHashSet();
        var latestConnections = connections.GroupBy(x => x.SourceAccountId)
            .Select(g => g.OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).First()).ToList();
        var latestReviews = reviews.GroupBy(x => x.ClientInvestmentAccountId)
            .Select(g => g.OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).First()).ToList();
        while (true)
        {
            var count = connectedIds.Count;
            connectedIds.UnionWith(latestConnections.Where(x => connectedIds.Contains(x.DestinationAccountId)).Select(x => x.SourceAccountId).ToList());
            connectedIds.UnionWith(latestReviews.Where(x => x.Outcome == ClientInvestmentReconciliationOutcomes.Transferred &&
                x.RelatedClientInvestmentAccountId.HasValue && connectedIds.Contains(x.RelatedClientInvestmentAccountId.Value) &&
                !latestConnections.Any(c => c.SourceAccountId == x.ClientInvestmentAccountId)).Select(x => x.ClientInvestmentAccountId).ToList());
            if (connectedIds.Count == count) break;
        }
        foreach (var root in accounts.Where(x => x.ClientId == client.Id))
        {
            var report = InvestmentReturnCalculator.Build(root, accounts, valuations, reviews, connections: connections);
            foreach (var entry in report.FundingHistory)
            {
                if (!exportedSources.Add(entry.SourceAccountId)) continue;
                var source = accounts.Single(x => x.Id == entry.SourceAccountId);
                var destination = accounts.Single(x => x.Id == entry.DestinationAccountId);
                var stored = connections.Where(x => x.SourceAccountId == source.Id).OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
                var through = entry.Match.Incoming.Max(x => x.Date);
                result.Add(new() { Source = Reference(source), Destination = Reference(destination), MatchedThroughDate = through,
                    SourceSnapshot = InvestmentFundingService.Snapshot(source), DestinationSnapshot = InvestmentFundingService.Snapshot(destination, through),
                    EvidenceReference = entry.EvidenceReference, Reason = entry.Reason, PerformedBy = entry.PerformedBy,
                    RecordedBy = stored?.RecordedBy ?? entry.PerformedBy, RecordedAtUtc = entry.RecordedAtUtc });
            }
        }
        // Preserve stale explicit findings too. Live must not silently drop a return-history limitation.
        foreach (var item in latestConnections.Where(x => connectedIds.Contains(x.DestinationAccountId)))
        {
            var source = accounts.Single(x => x.Id == item.SourceAccountId);
            if (!exportedSources.Add(source.Id)) continue;
            result.Add(new() { Source = Reference(source), Destination = Reference(accounts.Single(x => x.Id == item.DestinationAccountId)),
                MatchedThroughDate = item.MatchedThroughDate, SourceSnapshot = item.SourceSnapshot, DestinationSnapshot = item.DestinationSnapshot,
                EvidenceReference = item.EvidenceReference, Reason = item.Reason, PerformedBy = item.PerformedBy,
                RecordedBy = item.RecordedBy, RecordedAtUtc = item.RecordedAtUtc });
        }
        return result;
    }

    public static void Validate(ClientReviewPackage package)
    {
        if (package.FundingConnections is null || package.FundingConnections.Count > 2000)
            throw new ValidationException("Invalid funding-history collection.");
        var sources = new HashSet<string>();
        foreach (var item in package.FundingConnections)
        {
            if (item is null || item.Source is null || item.Destination is null || item.MatchedThroughDate == default ||
                item.MatchedThroughDate > DateOnly.FromDateTime(DateTime.Today) || item.RecordedAtUtc == default || item.RecordedAtUtc > DateTime.UtcNow.AddMinutes(1) ||
                !Hash(item.SourceSnapshot) || !Hash(item.DestinationSnapshot) ||
                string.IsNullOrWhiteSpace(item.EvidenceReference) || item.EvidenceReference.Length > 512 ||
                string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 1000 ||
                string.IsNullOrWhiteSpace(item.PerformedBy) || item.PerformedBy.Length > 191 || string.IsNullOrWhiteSpace(item.RecordedBy) || item.RecordedBy.Length > 191)
                throw new ValidationException("Funding-history evidence, snapshots or provenance are invalid.");
            foreach (var reference in new[] { item.Source, item.Destination })
                if ((!reference.LegacyClientId.HasValue && string.IsNullOrWhiteSpace(reference.ClientName)) ||
                    (!reference.LegacyAccountId.HasValue && string.IsNullOrWhiteSpace(reference.AccountNumber)) ||
                    (reference.KanaanId != package.Client.KanaanId) ||
                    new[] { reference.ClientName, reference.KanaanId, reference.AccountNumber, reference.Administrator, reference.ProductName, reference.FundName }.Any(x => x?.Length > 512))
                    throw new ValidationException("Funding-history client/account identity must be within the exported family.");
            if (!sources.Add(Key(item.Source)) || Key(item.Source) == Key(item.Destination))
                throw new ValidationException("Funding-history sources must be unique and cannot refer back to themselves.");
            if (string.IsNullOrWhiteSpace(package.Client.KanaanId) && new[] { item.Source, item.Destination }.Any(reference =>
                package.Client.LegacyClientId.HasValue ? reference.LegacyClientId != package.Client.LegacyClientId : reference.ClientName != package.Client.DisplayName))
                throw new ValidationException("Without a recorded family ID, funding history must stay within the exported client.");
        }
    }

    public static async Task InspectAsync(ApplicationDbContext db, ClientReviewPackage package, ICollection<string> warnings,
        string? importer, CancellationToken ct)
    {
        foreach (var item in package.FundingConnections)
        {
            var source = await ResolveAsync(db, item.Source, ct);
            var destination = await ResolveAsync(db, item.Destination, ct);
            if (source is null || destination is null || source.Id == destination.Id)
            {
                warnings.Add($"Return history {item.Source.AccountNumber} to {item.Destination.AccountNumber}: an account could not be matched uniquely. Funding provenance remains in the package; CAR requires resolving this link. Compliance import is not blocked.");
                continue;
            }
            if (item.SourceSnapshot != InvestmentFundingService.Snapshot(source) ||
                item.DestinationSnapshot != InvestmentFundingService.Snapshot(destination, item.MatchedThroughDate))
                warnings.Add($"Return history {item.Source.AccountNumber} to {item.Destination.AccountNumber}: historical cash flows differ; capital-history CAR needs review. Compliance import is not blocked.");
            if (importer is null) continue;
            if (await db.InvestmentFundingConnections.AnyAsync(x => x.SourceAccountId == source.Id && x.DestinationAccountId == destination.Id &&
                x.SourceSnapshot == item.SourceSnapshot && x.DestinationSnapshot == item.DestinationSnapshot && x.RecordedAtUtc == item.RecordedAtUtc, ct)) continue;
            var connection = new InvestmentFundingConnection { SourceAccountId = source.Id, DestinationAccountId = destination.Id,
                MatchedThroughDate = item.MatchedThroughDate, SourceSnapshot = item.SourceSnapshot, DestinationSnapshot = item.DestinationSnapshot,
                EvidenceReference = item.EvidenceReference, Reason = item.Reason, PerformedBy = item.PerformedBy,
                RecordedBy = item.RecordedBy, RecordedAtUtc = item.RecordedAtUtc };
            db.InvestmentFundingConnections.Add(connection);
            db.ComplianceAuditEvents.Add(new() { EntityType = nameof(InvestmentFundingConnection), EntityId = destination.Id,
                Action = "InvestmentFundingContinuityImported", UserName = importer, Reason = $"Funding provenance from package {package.PackageId}",
                NewValueJson = System.Text.Json.JsonSerializer.Serialize(new { package.PackageId, SourceAccountId = source.Id, DestinationAccountId = destination.Id, item }) });
        }
    }

    private static async Task<ClientInvestmentAccount?> ResolveAsync(ApplicationDbContext db, InvestmentFundingAccountReference reference, CancellationToken ct)
    {
        var clients = await db.Clients.AsNoTracking().Where(x => x.KanaanId == reference.KanaanId &&
            (reference.LegacyClientId.HasValue ? x.LegacyClientId == reference.LegacyClientId : x.DisplayName == reference.ClientName)).Select(x => x.Id).ToListAsync(ct);
        if (clients.Count != 1) return null;
        var clientId = clients[0];
        var accounts = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Transactions)
            .Where(x => x.ClientId == clientId).ToListAsync(ct);
        return ClientReviewTransferService.MatchInvestmentAccount(accounts, reference.LegacyAccountId, reference.AccountNumber,
            reference.Administrator, reference.InvestmentDate, reference.ProductName, reference.FundName);
    }

    private static bool Hash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static string Key(InvestmentFundingAccountReference reference) => System.Text.Json.JsonSerializer.Serialize(new {
        reference.LegacyClientId, Name = reference.LegacyClientId.HasValue ? null : reference.ClientName, reference.KanaanId,
        reference.LegacyAccountId, Account = reference.LegacyAccountId.HasValue ? null : reference.AccountNumber,
        Date = reference.LegacyAccountId.HasValue ? null : reference.InvestmentDate, Administrator = reference.LegacyAccountId.HasValue ? null : reference.Administrator });
    private static InvestmentFundingAccountReference Reference(ClientInvestmentAccount account) => new() {
        LegacyClientId = account.Client.LegacyClientId, KanaanId = account.Client.KanaanId, ClientName = account.Client.DisplayName,
        LegacyAccountId = account.LegacyInvestmentAccountId, AccountNumber = account.AccountNumber, Administrator = account.Administrator,
        InvestmentDate = account.InvestmentDate, ProductName = account.ProductName, FundName = account.FundName };
}
