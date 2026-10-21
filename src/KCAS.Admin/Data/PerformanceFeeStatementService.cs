using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed record FeeStatementReceipt(int Id, string FileName, string Sha256, string ImportedBy, DateTime ImportedAtUtc, int OwnerProfileId = 0);
public sealed record FeeStatementPreview(FeeStatementDocument Document, FeeStatementLedger Ledger, string Account, string[] OtherAccounts, bool AlreadyImported);

public sealed partial class PerformanceFeeService
{
    public async Task<FeeStatementPreview> PreviewStatementAsync(int id, byte[] content, string fileName, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var profile = await Row(db, id, actor);
        return await PreviewStatement(db, profile, content, fileName);
    }

    public async Task<int> ImportStatementAsync(int id, string version, byte[] content, string fileName, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var profile = await Row(db, id, actor); ComplianceWorkflowAccess.Expect(profile.Version, version); CheckPending(profile);
        var preview = await PreviewStatement(db, profile, content, fileName);
        var affected = await ClientProfiles(db, profile.Account.ClientId);
        var profileIds = affected.Select(x => x.Id).ToArray();
        var existing = await db.PerformanceFeeStatements.FirstOrDefaultAsync(x => profileIds.Contains(x.PerformanceFeeProfileId) && x.Sha256 == preview.Document.Sha256);
        if (existing is not null) return existing.Id;
        foreach (var item in affected) CheckPending(item);
        var statement = new PerformanceFeeStatement { PerformanceFeeProfileId = id, FileName = preview.Document.FileName,
            Sha256 = preview.Document.Sha256, Content = content, DocumentJson = FeeJson.Write(preview.Document), ImportedBy = ComplianceWorkflowAccess.Label(actor) };
        db.PerformanceFeeStatements.Add(statement);
        foreach (var item in affected)
        {
            InvalidateDraft(item, "Statement history changed. Rebuild the draft from the preserved administrator statements.");
            Touch(item, actor); item.Version = ComplianceWorkflowAccess.NewVersion(); item.CodexRequestVersion = null;
        }
        Event(profile, null, "StatementImported", "Manual", actor, Today,
            $"Preserved {preview.Document.Rows.Count(x => x.Account == preview.Account)} source rows for {preview.Account}; overlaps are deduplicated, original file retained. No investment or valuation was changed.", preview.Document.FileName + "; SHA256 " + preview.Document.Sha256);
        Touch(profile, actor); profile.Version = ComplianceWorkflowAccess.NewVersion(); profile.CodexRequestVersion = null;
        await db.SaveChangesAsync(); return statement.Id;
    }

    public async Task<(List<FeeStatementReceipt> Receipts, FeeStatementLedger Ledger)> StatementsAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, false);
        var profile = await Row(db, id, actor);
        var sourceIds = db.PerformanceFeeProfiles.Where(x => x.Account.ClientId == profile.Account.ClientId).Select(x => x.Id);
        var receipts = (await db.PerformanceFeeStatements.AsNoTracking().Where(x => sourceIds.Contains(x.PerformanceFeeProfileId))
            .OrderBy(x => x.Id).Select(x => new FeeStatementReceipt(x.Id, x.FileName, x.Sha256, x.ImportedBy, x.ImportedAtUtc, x.PerformanceFeeProfileId)).ToListAsync()).DistinctBy(x => x.Sha256).ToList();
        return (receipts, await Ledger(db, profile));
    }

    public async Task<(byte[] Content, string FileName)> DownloadStatementAsync(int id, int statementId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, false);
        await Row(db, id, actor);
        var row = await db.PerformanceFeeStatements.AsNoTracking().SingleAsync(x => x.Id == statementId && x.PerformanceFeeProfileId == id);
        return (row.Content, row.FileName);
    }

    public async Task ReviewStatementRowAsync(int id, string version, string key, string kind, string reason, ClaimsPrincipal principal)
    {
        if (!FeeStatementKinds.Reviewable.Contains(kind)) throw new ValidationException("Select a supported transaction classification.");
        reason = ComplianceWorkflowAccess.Required(reason, "Evidence and classification reason", 2000);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var profile = await Row(db, id, actor); ComplianceWorkflowAccess.Expect(profile.Version, version); CheckPending(profile);
        var ledger = await Ledger(db, profile);
        var source = ledger.Rows.Single(x => x.Key == key);
        if (source.Kind is "Opening" or "Closing") throw new ValidationException("Opening and closing evidence cannot be reclassified as transactions.");
        var reviews = FeeJson.Read<Dictionary<string, FeeStatementReview>>(profile.StatementReviewJson);
        reviews[key] = new(kind, reason, ComplianceWorkflowAccess.Label(actor), DateTime.UtcNow);
        profile.StatementReviewJson = FeeJson.Write(reviews);
        InvalidateDraft(profile, "Transaction classification changed. Rebuild the draft from statements.");
        Event(profile, null, "StatementRowReviewed", "Manual", actor, Today, reason, $"{source.Source}; {source.Kind} -> {kind}; row {key}");
        profile.Version = ComplianceWorkflowAccess.NewVersion(); profile.CodexRequestVersion = null; Touch(profile, actor);
        await db.SaveChangesAsync();
    }

    public async Task<int> PrepareStatementsAsync(int id, string version, ClaimsPrincipal principal)
    {
        // The tracked profile version is a concurrency token, protecting against concurrent uploads/reviews.
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var profile = await Row(db, id, actor); ComplianceWorkflowAccess.Expect(profile.Version, version); CheckPending(profile);
        var ledger = await Ledger(db, profile);
        if (ledger.Rows.Count == 0) throw new ValidationException("Import this account's administrator statement first.");
        var checkpoint = profile.CheckpointJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<FeeCheckpoint>(profile.CheckpointJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (checkpoint is not null && checkpoint.Wraps.Count == 0) throw new ValidationException("The preserved pooled checkpoint needs an evidenced per-wrap allocation before using separate scopes.");
        var input = ledger.Prepare(FeeJson.Read<FeePreparation>(profile.PreparationJson), checkpoint);
        var policy = await SharedPolicy(db);
        ApplyCoveredMandate(input, profile.ClientInvestmentAccountId, (await ClientProfiles(db, profile.Account.ClientId)).Select(x =>
        { var source = FeeJson.Read<FeePreparation>(x.PreparationJson); ApplySharedPolicy(source, policy); source.MandateSourceAccountId = x.ClientInvestmentAccountId; return source; }));
        ApplySharedPolicy(input, policy);
        ApplyBenchmarks(input, await CpiObservations(db));
        // Ledger-generated unresolved review points cannot be supplied through the public draft editor.
        var claim = SavePreparation(profile, input,
            $"Prepared separate wrap calculations from {ledger.Rows.Count} preserved administrator rows; {ledger.DuplicateRows} overlapping rows ignored. Income reinvestments and ordinary fees are not client capital. Actual statement cutoffs and unresolved continuity/valuation gaps are retained. Mandate, CPI, VAT and prior-fee confirmations have not been inferred.", actor, "KCAS", reviewPointChange: true);
        await db.SaveChangesAsync();
        return claim.Id;
    }

    private static async Task<FeeStatementPreview> PreviewStatement(ApplicationDbContext db, PerformanceFeeProfile profile, byte[] content, string fileName)
    {
        var document = PerformanceFeeStatementReader.Read(content, fileName);
        var account = profile.Account.AccountNumber?.Trim().ToUpperInvariant() ?? "";
        if (account == "" || !document.Rows.Any(x => x.Account == account)) throw new ValidationException("This statement does not contain the selected KCAS account. No client-name or approximate account matching is used.");
        var documents = await Documents(db, profile.Id);
        var already = documents.Any(x => x.Sha256 == document.Sha256);
        if (!already) documents.Add(document);
        return new(document, FeeStatementLedger.Merge(documents, account, FeeJson.Read<Dictionary<string, FeeStatementReview>>(profile.StatementReviewJson)), account,
            document.Rows.Select(x => x.Account).Distinct().Where(x => x != account).ToArray(), already);
    }

    private static async Task<List<FeeStatementDocument>> Documents(ApplicationDbContext db, int id)
    {
        var clientId = await db.PerformanceFeeProfiles.Where(x => x.Id == id).Select(x => x.Account.ClientId).SingleAsync();
        var sourceIds = db.PerformanceFeeProfiles.Where(x => x.Account.ClientId == clientId).Select(x => x.Id);
        return (await db.PerformanceFeeStatements.Where(x => sourceIds.Contains(x.PerformanceFeeProfileId)).OrderBy(x => x.Id).ToListAsync())
            .Select(ParsePreservedStatement).DistinctBy(x => x.Sha256).ToList();
    }

    private static FeeStatementDocument ParsePreservedStatement(PerformanceFeeStatement row)
    {
        var document = FeeJson.Read<FeeStatementDocument>(row.DocumentJson);
        if (document.ParserVersion == PerformanceFeeStatementReader.CurrentVersion) return document;
        document = PerformanceFeeStatementReader.Read(row.Content, row.FileName);
        if (document.Sha256 != row.Sha256) throw new ValidationException("A preserved statement's original bytes do not match its recorded SHA256; investigate before preparing a claim.");
        // Read-only views discard this tracked metadata update; authorised preparation saves it once.
        row.DocumentJson = FeeJson.Write(document);
        return document;
    }

    private static async Task ValidateStatementSnapshot(ApplicationDbContext db, PerformanceFeeProfile row, FeePreparation input)
    {
        var documents = await Documents(db, row.Id);
        if (documents.Count == 0) return;
        var ledger = FeeStatementLedger.Merge(documents, row.Account.AccountNumber?.Trim().ToUpperInvariant() ?? "",
            FeeJson.Read<Dictionary<string, FeeStatementReview>>(row.StatementReviewJson));
        if (input.StatementHistory?.SourceFingerprint != ledger.History.SourceFingerprint || input.RequiresRebuild)
            throw new ValidationException("Statement summaries, history or interpretation changed. Prepare the client accounts again before KI approval.");
    }
    private static async Task<FeeStatementLedger> Ledger(ApplicationDbContext db, PerformanceFeeProfile profile) =>
        FeeStatementLedger.Merge(await Documents(db, profile.Id), profile.Account.AccountNumber?.Trim().ToUpperInvariant() ?? "",
            FeeJson.Read<Dictionary<string, FeeStatementReview>>(profile.StatementReviewJson));

    private static void InvalidateDraft(PerformanceFeeProfile profile, string issue)
    {
        foreach (var claim in profile.Claims.Where(x => x.Status == FeeClaimStages.Draft))
        {
            var input = FeeJson.Read<FeePreparation>(claim.InputJson);
            input.RequiresRebuild = true;
            if (!input.EvidenceIssues.Contains(issue)) input.EvidenceIssues.Add(issue);
            claim.InputJson = FeeJson.Write(input); claim.ResultJson = FeeJson.Write(Calculate(profile, input)); claim.Version = ComplianceWorkflowAccess.NewVersion();
        }
    }
}
