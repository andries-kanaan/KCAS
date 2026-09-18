using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientAdviceTransferServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Export_embeds_referenced_advice_files_outside_the_client_folder()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transfers = scope.ServiceProvider.GetRequiredService<ClientAdviceTransferService>();
        var root = Path.Combine(Path.GetTempPath(), $"kcas-advice-embed-{Guid.NewGuid():N}");
        var clientFolder = Path.Combine(root, "client");
        var workingFolder = Path.Combine(root, "working");
        Directory.CreateDirectory(clientFolder); Directory.CreateDirectory(workingFolder);
        var externalPath = Path.Combine(workingFolder, "review.pdf");
        await File.WriteAllBytesAsync(externalPath, [1, 2, 3, 4, 5]);
        string? packagePath = null;
        try
        {
            var client = new Client { LegacyClientId = Random.Shared.Next(600000, 699999), DisplayName = $"Embedded Advice {Guid.NewGuid():N}", SurnameOrEntityName = "Embedded", ClientFolder = clientFolder };
            client.AdviceDocuments.Add(new ClientAdviceDocument { DocumentType = ClientAdviceDocumentTypes.HistoricalAdviceRecord, FileName = "review.pdf", SourcePath = externalPath, FileSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3, 4, 5])).ToLowerInvariant(), FileSizeBytes = 5 });
            db.Clients.Add(client); await db.SaveChangesAsync();

            var export = await transfers.ExportAsync(client.Id, "embedded-file-test", "exporter@example.test", "Test external advice evidence.");
            packagePath = export.StoragePath;
            var preview = await transfers.PreviewAsync(await File.ReadAllBytesAsync(export.StoragePath), "embedded-file-test");
            var embedded = Assert.Single(preview.Package.EmbeddedFiles);
            Assert.Equal(externalPath, embedded.OriginalPath);
            Assert.True(preview.CanApply, string.Join(" | ", preview.Conflicts));
        }
        finally
        {
            DeleteFile(packagePath);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Advice_bundle_previews_applies_complete_case_graph_and_rejects_duplicate()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transfers = scope.ServiceProvider.GetRequiredService<ClientAdviceTransferService>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string? outgoingPath = null;
        string? incomingPath = null;

        try
        {
            var client = new Client
            {
                LegacyClientId = Random.Shared.Next(700000, 799999), KanaanId = $"ADV-{suffix}",
                DisplayName = $"Advice Transfer {suffix}", SurnameOrEntityName = $"Transfer {suffix}",
                ClientFolder = $@"C:\Download\_kanaan\ClientsKanaan\ADVICE {suffix}"
            };
            var account = new ClientInvestmentAccount
            {
                Client = client, LegacyInvestmentAccountId = Random.Shared.Next(800000, 899999),
                AccountNumber = $"ADV{suffix}", Administrator = "Transfer Provider", ProductName = "Transfer product"
            };
            client.InvestmentAccounts.Add(account);
            var advice = new ClientAdviceCase
            {
                Client = client, AdviceType = ClientAdviceTypes.AnnualReview,
                RiskMethodologyCode = ClientAdviceMethodologies.PreviousFormCorrectedBands,
                Status = ClientAdviceStatuses.ReadyForReview, Revision = 1,
                AdviceDate = new DateOnly(2026, 9, 18), AdviserName = "adviser@example.test",
                PreparedBy = "codex-assisted", AdviceScope = "Annual retirement-income review.",
                MeetingSummary = "Client requested a sustainable income review.",
                NeedsAndObjectives = "Preserve capital and maintain income.", FinancialSituation = "Documented.",
                AdviceLimitations = "Draft pending independent review.", ProductKnowledgeSummary = "Experienced investor.",
                CalculatedRiskScore = 58, CalculatedRiskLevel = "Moderately Aggressive", FinalRiskLevel = "Moderately Aggressive",
                RecommendationSummary = "Retain current investments.", RecommendationRationale = "Suitable pending review.",
                CostsAndFees = "Existing fees continue.", TaxConsequences = "Tax consequences disclosed.",
                LiquidityAndRestrictions = "Product restrictions apply.", MaterialRisks = "Market risk applies.",
                WarningsGiven = "Returns are not guaranteed.", SubmittedAtUtc = DateTime.UtcNow.AddHours(-1)
            };
            advice.Participants.Add(new ClientAdviceParticipant { Client = client, Role = "AdviceSubject" });
            advice.RiskResponses.Add(new ClientAdviceRiskResponse { QuestionCode = "TIME_HORIZON", AnswerCode = "LONG", Score = 7, Explanation = "Supported by the review." });
            advice.Products.Add(new ClientAdviceProduct { ProductName = "Transfer product", Provider = "Transfer Provider", ProductType = "Investment", IsRecommended = true, Motivation = "Retain." });
            advice.FactSources.Add(new ClientAdviceFactSource { FactName = "Portfolio", SourceDate = new DateOnly(2026, 9, 1), DocumentPath = $@"{client.ClientFolder}\Statements\portfolio.pdf", Notes = "Current statement." });
            advice.InvestmentLinks.Add(new ClientAdviceInvestmentLink { InvestmentAccount = account, Role = "ExistingPortfolio" });
            advice.ReviewFindings.Add(new ClientAdviceReviewFinding { Severity = ClientAdviceFindingSeverities.High, Category = "Suitability", Finding = "Independent review required.", RecommendedCorrection = "Complete review.", PerformedBy = "codex-assisted" });
            advice.Documents.Add(new ClientAdviceDocument { Client = client, DocumentType = ClientAdviceDocumentTypes.GeneratedAdviceRecord, FileName = "draft.pdf", FileSha256 = new string('a', 64), FileSizeBytes = 1234, RecordedBy = "codex-assisted" });
            client.AdviceCases.Add(advice);
            client.AdviceDocuments.Add(new ClientAdviceDocument { DocumentType = ClientAdviceDocumentTypes.HistoricalAdviceRecord, FileName = "historic.pdf", SourcePath = $@"{client.ClientFolder}\FAIS\historic.pdf", FileSha256 = new string('b', 64), FileSizeBytes = 4321, RecordedBy = "codex-assisted" });
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            db.ComplianceAuditEvents.Add(new ComplianceAuditEvent { EntityType = nameof(ClientAdviceCase), EntityId = advice.Id, Action = "AdviceSubmittedForReview", UserName = "adviser@example.test", Reason = "Ready for review.", TimestampUtc = advice.SubmittedAtUtc!.Value });
            await db.SaveChangesAsync();

            const string passphrase = "advice-transfer-test";
            var exported = await transfers.ExportAsync(client.Id, passphrase, "exporter@example.test", "Transfer advice to live.");
            outgoingPath = exported.StoragePath;
            Assert.Matches(@"^KCAS-advice-AdviceTransfer[a-f0-9]{8}-\d{8}-[a-f0-9]{12}\.kcas-advice$", exported.FileName);
            Assert.Equal(1, exported.CaseCount);
            Assert.Equal(2, exported.DocumentCount);
            var encrypted = await File.ReadAllBytesAsync(exported.StoragePath);

            db.ComplianceAuditEvents.RemoveRange(await db.ComplianceAuditEvents.Where(value => value.EntityType == nameof(ClientAdviceCase) && value.EntityId == advice.Id).ToListAsync());
            db.ClientAdviceCases.Remove(advice);
            db.ClientAdviceDocuments.RemoveRange(await db.ClientAdviceDocuments.Where(value => value.ClientId == client.Id && value.ClientAdviceCaseId == null).ToListAsync());
            client.ClientFolder = $@"E:\Userdata\Kanaan Trust\Clients\ADVICE {suffix}";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var preview = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(preview.CanApply, string.Join(" | ", preview.Conflicts));
            Assert.Equal(client.Id, preview.TargetClientId);
            Assert.Equal(client.ClientFolder, preview.TargetClientFolder);
            Assert.Single(preview.Package.Cases);
            Assert.Single(preview.Package.AuditEvents);

            var imported = await transfers.ApplyAsync(encrypted, passphrase, "importer@example.test", "Approved live advice import.");
            incomingPath = imported.StoragePath;
            Assert.Equal(client.Id, imported.ClientId);
            var liveCase = await db.ClientAdviceCases.AsNoTracking()
                .Include(value => value.Participants).Include(value => value.RiskResponses).Include(value => value.Products)
                .Include(value => value.FactSources).Include(value => value.InvestmentLinks)
                .Include(value => value.ReviewFindings).Include(value => value.Documents)
                .SingleAsync(value => value.ClientId == client.Id);
            Assert.Equal(ClientAdviceStatuses.ReadyForReview, liveCase.Status);
            Assert.Equal(58, liveCase.CalculatedRiskScore);
            Assert.Single(liveCase.Participants); Assert.Single(liveCase.RiskResponses); Assert.Single(liveCase.Products);
            Assert.Single(liveCase.InvestmentLinks); Assert.Single(liveCase.ReviewFindings); Assert.Single(liveCase.Documents);
            Assert.StartsWith(client.ClientFolder!, liveCase.FactSources.Single().DocumentPath, StringComparison.OrdinalIgnoreCase);
            var historical = await db.ClientAdviceDocuments.AsNoTracking().SingleAsync(value => value.ClientId == client.Id && value.ClientAdviceCaseId == null);
            Assert.StartsWith(client.ClientFolder!, historical.SourcePath!, StringComparison.OrdinalIgnoreCase);
            Assert.True(await db.ComplianceAuditEvents.AnyAsync(value => value.EntityType == nameof(ClientAdviceCase) && value.EntityId == liveCase.Id && value.Action == "AdviceSubmittedForReview"));
            Assert.True(await db.ComplianceAuditEvents.AnyAsync(value => value.Action == "ClientAdvicePackageApplied" && value.UserName == "importer@example.test"));

            var duplicate = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(duplicate.AlreadyApplied);
            Assert.False(duplicate.CanApply);
            await Assert.ThrowsAsync<InvalidOperationException>(() => transfers.ApplyAsync(encrypted, passphrase, "importer@example.test", "Duplicate import."));
        }
        finally
        {
            DeleteFile(outgoingPath); DeleteFile(incomingPath);
        }
    }

    private static void DeleteFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }
}
