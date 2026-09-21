using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientAdviceServiceTests(KcasWebApplicationFactory factory)
{
    [Theory]
    [InlineData(18, "Very low")]
    [InlineData(30, "Very low")]
    [InlineData(31, "Low")]
    [InlineData(48, "Low")]
    [InlineData(49, "Medium")]
    [InlineData(66, "Medium")]
    [InlineData(67, "Medium to high")]
    [InlineData(84, "Medium to high")]
    [InlineData(85, "High")]
    [InlineData(100, "High")]
    public void Risk_bands_are_unambiguous(int score, string expected)
        => Assert.Equal(expected, ClientAdviceService.RiskLevel(score));

    [Theory]
    [InlineData(11, "Very Conservative")]
    [InlineData(19, "Very Conservative")]
    [InlineData(20, "Conservative")]
    [InlineData(39, "Conservative")]
    [InlineData(40, "Moderate")]
    [InlineData(54, "Moderate")]
    [InlineData(55, "Moderately Aggressive")]
    [InlineData(74, "Moderately Aggressive")]
    [InlineData(75, "Aggressive")]
    [InlineData(85, "Aggressive")]
    public void Previous_form_bands_use_exclusive_upper_boundaries(int score, string expected)
        => Assert.Equal(expected, ClientAdviceService.RiskLevel(ClientAdviceMethodologies.PreviousFormCorrectedBands, score));

    [Fact]
    public async Task Previous_form_methodology_calculates_corrected_example_as_58_moderately_aggressive()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Previous Form Client");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.AnnualReview, "adviser@example.test");
        var edit = Complete(await service.LoadCaseAsync(caseId));
        var methodology = ClientAdviceService.Methodology(ClientAdviceMethodologies.PreviousFormCorrectedBands);
        edit.RiskMethodologyCode = methodology.Code;
        var scores = new Dictionary<string, int>
        {
            ["HORIZON"] = 4, ["RETIREMENT"] = 1, ["REGULAR_INCOME"] = 1, ["WITHDRAWAL"] = 4,
            ["DEPENDANTS"] = 5, ["AGE"] = 2, ["INCOME_CHANGE"] = 3, ["EMERGENCY_FUNDS"] = 15,
            ["MAJOR_OBLIGATIONS"] = 5, ["ATTITUDE"] = 9, ["VOLATILITY"] = 9
        };
        edit.RiskResponses = methodology.Questions.Select(question => new ClientAdviceRiskResponseEditModel
        {
            QuestionCode = question.Code,
            AnswerCode = question.Options.Single(option => option.Score == scores[question.Code]).Code,
            Explanation = "Supported by the reviewed client record."
        }).ToList();

        await service.SaveDraftAsync(edit, "adviser@example.test");

        var saved = await service.LoadCaseAsync(caseId);
        Assert.Equal(58, saved.CalculatedRiskScore);
        Assert.Equal("Moderately Aggressive", saved.CalculatedRiskLevel);
        Assert.Equal("Moderately Aggressive", saved.FinalRiskLevel);
    }

    [Fact]
    public async Task Complete_case_requires_independent_review_and_freezes_snapshot()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Advice Test Client");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.NewInvestment, "adviser@example.test");

        var edit = Complete(await service.LoadCaseAsync(caseId));
        await service.SaveDraftAsync(edit, "adviser@example.test");
        await service.SubmitForReviewAsync(caseId, "adviser@example.test");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(caseId, "adviser@example.test", "Self approval."));

        await service.ApproveAsync(caseId, "reviewer@example.test", "Independent review complete.");
        var approved = await db.ClientAdviceCases.AsNoTracking().Include(item => item.Approvals).Include(item => item.Documents).SingleAsync(item => item.Id == caseId);
        Assert.Equal(ClientAdviceStatuses.ApprovedForIssue, approved.Status);
        Assert.NotNull(approved.FrozenSnapshotJson);
        Assert.Equal(64, approved.FrozenSnapshotSha256?.Length);
        Assert.Equal("reviewer@example.test", Assert.Single(approved.Approvals).Reviewer);
        var generated = Assert.Single(approved.Documents);
        Assert.Equal(ClientAdviceDocumentTypes.GeneratedAdviceRecord, generated.DocumentType);
        Assert.Equal(clientId, generated.ClientId);
        Assert.Equal(64, generated.FileSha256?.Length);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(edit, "adviser@example.test"));
        var pdf = await service.ExportPdfAsync(caseId);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.Equal(generated.FileSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf)).ToLowerInvariant());

        var revisionId = await service.CreateRevisionAsync(caseId, "second-adviser@example.test");
        var revision = await db.ClientAdviceCases.AsNoTracking().SingleAsync(item => item.Id == revisionId);
        Assert.Equal(caseId, revision.PreviousAdviceCaseId);
        Assert.Equal(2, revision.Revision);
        Assert.Equal(ClientAdviceStatuses.Draft, revision.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRevisionAsync(caseId, "third-adviser@example.test"));
    }

    [Fact]
    public async Task Annual_review_does_not_require_a_new_investment_amount()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Annual Review Client");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.AnnualReview, "adviser@example.test");

        var edit = Complete(await service.LoadCaseAsync(caseId));
        edit.AdviceScope = "Review the continuing suitability of the existing portfolio.";
        edit.InvestmentAmount = null;
        edit.RecommendationSummary = "Retain the existing portfolio subject to the recorded review actions.";
        await service.SaveDraftAsync(edit, "adviser@example.test");

        await service.SubmitForReviewAsync(caseId, "adviser@example.test");

        Assert.Equal(ClientAdviceStatuses.ReadyForReview,
            await db.ClientAdviceCases.Where(item => item.Id == caseId).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task Open_review_finding_blocks_approval_until_resolved()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Finding Test Client");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.Switch, "preparer@example.test");
        var edit = Complete(await service.LoadCaseAsync(caseId));
        await service.SaveDraftAsync(edit, "preparer@example.test");
        await service.AddFindingAsync(caseId, new ClientAdviceFindingEditModel
        {
            Severity = ClientAdviceFindingSeverities.High,
            Category = "Recommendation",
            AffectedField = "RecommendationRationale",
            Finding = "The recommendation requires a clearer comparison.",
            EvidenceReference = "Fund factsheet",
            RecommendedCorrection = "Add the comparison."
        }, "Codex");
        await service.SubmitForReviewAsync(caseId, "preparer@example.test");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(caseId, "reviewer@example.test", "Review."));

        var findingId = await db.ClientAdviceReviewFindings.Where(item => item.ClientAdviceCaseId == caseId).Select(item => item.Id).SingleAsync();
        await service.ResolveFindingAsync(findingId, false, "Comparison checked and added.", "preparer@example.test");
        await service.ApproveAsync(caseId, "reviewer@example.test", "Review complete.");
        Assert.Equal(ClientAdviceStatuses.ApprovedForIssue,
            await db.ClientAdviceCases.Where(item => item.Id == caseId).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task Client_confirmation_can_follow_issue_but_blocks_completion_until_recorded()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Client Response Test");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.AnnualReview, "preparer@example.test");
        var edit = Complete(await service.LoadCaseAsync(caseId));
        edit.InvestmentAmount = null;
        edit.MeetingSummary = "Client described their objectives in email correspondence.";
        await service.SaveDraftAsync(edit, "preparer@example.test");
        await service.AddFindingAsync(caseId, new ClientAdviceFindingEditModel
        {
            Severity = ClientAdviceFindingSeverities.High,
            Category = "Client confirmation",
            Finding = "Ask the client to confirm the recorded personal circumstances and risk answers.",
            EvidenceReference = "Client email",
            RecommendedCorrection = "Include a confirmation request with the proposal."
        }, "Codex");
        var findingId = await db.ClientAdviceReviewFindings.Where(value => value.ClientAdviceCaseId == caseId)
            .Select(value => value.Id).SingleAsync();
        await service.RequestClientConfirmationAsync(findingId, "preparer@example.test");
        await service.SubmitForReviewAsync(caseId, "preparer@example.test");
        await service.ApproveAsync(caseId, "reviewer@example.test", "Suitability and correspondence reviewed.");
        var approvedPdf = System.Text.Encoding.ASCII.GetString(await service.ExportPdfAsync(caseId));
        Assert.Contains("Client confirmation of information", approvedPdf);
        await service.MarkIssuedAsync(caseId, "issuer@example.test");

        var path = Path.Combine(Path.GetTempPath(), $"signed-advice-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, "%PDF-1.4 test signed record"u8.ToArray());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordSignedDocumentAsync(caseId, path, "issuer@example.test"));
            await service.RecordClientConfirmationAsync(findingId, "Signed response received 2026-09-21; no corrections.", "issuer@example.test");
            await service.RecordSignedDocumentAsync(caseId, path, "issuer@example.test");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
        Assert.Equal(ClientAdviceStatuses.Complete,
            await db.ClientAdviceCases.Where(value => value.Id == caseId).Select(value => value.Status).SingleAsync());
        Assert.Equal(approvedPdf, System.Text.Encoding.ASCII.GetString(await service.ExportPdfAsync(caseId)));
    }

    [Fact]
    public async Task Draft_preview_contains_client_record_issue_report_and_blocker_appendix_without_approving_case()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Draft Preview Client");
        var client = await db.Clients.SingleAsync(item => item.Id == clientId);
        client.FullName = "Draft Preview";
        client.SurnameOrEntityName = "Client";
        await db.SaveChangesAsync();
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.AnnualReview, "preparer@example.test");
        var edit = Complete(await service.LoadCaseAsync(caseId));
        edit.InvestmentAmount = null;
        edit.FactSources[0].Notes = "The complete source note must remain visible through its final confirmation marker END-SOURCE-NOTE.";
        await service.SaveDraftAsync(edit, "preparer@example.test");
        await service.AddFindingAsync(caseId, new ClientAdviceFindingEditModel
        {
            Severity = ClientAdviceFindingSeverities.High,
            Category = "Suitability",
            AffectedField = "RecommendationRationale",
            Finding = "Explain why the proposed allocation is suitable.",
            EvidenceReference = "Risk Analyser and portfolio",
            RecommendedCorrection = "Add the allocation comparison and adviser conclusion, including the final confirmation marker END-REVIEW-ACTION."
        }, "Codex");

        var pdf = await service.ExportDraftPreviewPdfAsync(caseId, "preparer@example.test");
        var content = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("DRAFT - NOT FOR CLIENT ISSUE", content);
        Assert.Contains("Internal draft review report", content);
        Assert.Contains("Why it matters", content);
        Assert.Contains("Blocker appendix", content);
        Assert.Contains("Explain why the proposed allocation is suitable", content);
        Assert.Contains("Draft Preview Client", content);
        Assert.DoesNotContain(client.DisplayName, content);
        Assert.Contains("END-SOURCE-NOTE", content);
        Assert.Contains("END-REVIEW-ACTION", content);
        Assert.Equal(ClientAdviceStatuses.Draft,
            await db.ClientAdviceCases.Where(item => item.Id == caseId).Select(item => item.Status).SingleAsync());
        Assert.False(await db.ClientAdviceDocuments.AnyAsync(item => item.ClientAdviceCaseId == caseId));
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(item => item.EntityType == nameof(ClientAdviceCase) &&
            item.EntityId == caseId && item.Action == "AdviceDraftPreviewExported"));
    }

    [Fact]
    public async Task Approved_letter_sample_uses_client_layout_without_changing_workflow_state()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Approved Sample Client");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.AnnualReview, "preparer@example.test");
        var edit = Complete(await service.LoadCaseAsync(caseId));
        edit.InvestmentAmount = null;
        edit.FactSources[0].DocumentPath = @"C:\Private\InternalEvidence.pdf";
        edit.FactSources[0].Notes = "INTERNAL-EVIDENCE-NOTE";
        await service.SaveDraftAsync(edit, "preparer@example.test");

        var auditCount = await db.ComplianceAuditEvents.CountAsync(value => value.EntityType == nameof(ClientAdviceCase) && value.EntityId == caseId);
        var content = System.Text.Encoding.ASCII.GetString(await service.ExportApprovedSamplePdfAsync(caseId));

        Assert.Contains("SAMPLE - NOT APPROVED - NOT FOR CLIENT ISSUE", content);
        Assert.Contains("Record: SAMPLE - NOT APPROVED", content);
        Assert.Contains("Financial adviser: ", content);
        Assert.Contains("Independently reviewed by: Pending independent approval", content);
        Assert.DoesNotContain(@"C:\Private\InternalEvidence.pdf", content);
        Assert.DoesNotContain("INTERNAL-EVIDENCE-NOTE", content);
        Assert.DoesNotContain("Internal draft review report", content);
        Assert.Equal(ClientAdviceStatuses.Draft,
            await db.ClientAdviceCases.Where(value => value.Id == caseId).Select(value => value.Status).SingleAsync());
        Assert.Equal(auditCount, await db.ComplianceAuditEvents.CountAsync(value => value.EntityType == nameof(ClientAdviceCase) && value.EntityId == caseId));
        Assert.False(await db.ClientAdviceDocuments.AnyAsync(value => value.ClientAdviceCaseId == caseId));
    }

    [Fact]
    public async Task Signed_pdf_completes_an_issued_case_and_records_hash()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Signed Test Client");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.TopUp, "preparer@example.test");
        await service.SaveDraftAsync(Complete(await service.LoadCaseAsync(caseId)), "preparer@example.test");
        await service.SubmitForReviewAsync(caseId, "preparer@example.test");
        await service.ApproveAsync(caseId, "reviewer@example.test", "Approved.");
        var approvedPdfHash = await db.ClientAdviceDocuments.Where(item => item.ClientAdviceCaseId == caseId && item.DocumentType == ClientAdviceDocumentTypes.GeneratedAdviceRecord).Select(item => item.FileSha256).SingleAsync();
        await service.MarkIssuedAsync(caseId, "issuer@example.test");

        var path = Path.Combine(Path.GetTempPath(), $"signed-advice-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, "%PDF-1.4 test signed record"u8.ToArray());
            await service.RecordSignedDocumentAsync(caseId, path, "issuer@example.test");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        var completed = await db.ClientAdviceCases.AsNoTracking().Include(item => item.Documents).SingleAsync(item => item.Id == caseId);
        Assert.Equal(ClientAdviceStatuses.Complete, completed.Status);
        Assert.Equal(2, completed.Documents.Count);
        var document = completed.Documents.Single(item => item.DocumentType == ClientAdviceDocumentTypes.SignedAdviceRecord);
        Assert.Equal(ClientAdviceDocumentTypes.SignedAdviceRecord, document.DocumentType);
        Assert.Equal(clientId, document.ClientId);
        Assert.Equal(64, document.FileSha256?.Length);
        var regenerated = await service.ExportPdfAsync(caseId);
        Assert.Equal(approvedPdfHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(regenerated)).ToLowerInvariant());
    }

    [Fact]
    public async Task Family_subjects_manifest_and_imported_findings_are_recorded()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Family Advice A", "A901");
        var relatedId = await CreateClientAsync(db, "Family Advice B", "A901");
        var caseId = await service.CreateDraftAsync(clientId, ClientAdviceTypes.AnnualReview, "preparer@example.test");

        Assert.Equal(1, await service.AddKanaanFamilyParticipantsAsync(caseId, "preparer@example.test"));
        var model = await service.LoadCaseAsync(caseId);
        Assert.Equal([clientId, relatedId], model.Participants.Select(item => item.ClientId).Order().ToArray());

        var manifest = System.Text.Encoding.UTF8.GetString(await service.ExportReviewManifestAsync(caseId));
        Assert.Contains("kcas-client-advice-review-v1", manifest);
        Assert.Contains("Family Advice B", manifest);

        var json = """
            { "findings": [{ "severity": "High", "category": "Suitability", "affectedField": "RecommendationRationale", "finding": "Explain the family allocation.", "evidenceReference": "Advice record", "recommendedCorrection": "Add the rationale." }] }
            """u8.ToArray();
        Assert.Equal(1, await service.ImportFindingsAsync(caseId, json, "Codex"));
        var finding = await db.ClientAdviceReviewFindings.SingleAsync(item => item.ClientAdviceCaseId == caseId);
        Assert.Equal("Codex", finding.PerformedBy);
        Assert.Equal(ClientAdviceFindingSeverities.High, finding.Severity);
    }

    [Fact]
    public async Task Historical_advice_documents_are_indexed_without_creating_a_case()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var clientId = await CreateClientAsync(db, "Historical Advice Client");
        var run = new ClientEvidenceScanRun { RootPath = @"C:\Clients", Status = ClientEvidenceScanStatuses.Completed };
        run.Files.Add(new ClientEvidenceScanFile
        {
            ClientId = clientId, FullPath = @"C:\Clients\Risk Analyser 2024.pdf", RelativePath = "Risk Analyser 2024.pdf",
            FileName = "Risk Analyser 2024.pdf", FileSha256 = new string('a', 64), FileSizeBytes = 1234,
            FileLastWriteTimeUtc = DateTime.UtcNow, MatchStatus = ClientEvidenceScanFileStatuses.Linked
        });
        db.ClientEvidenceScanRuns.Add(run);
        await db.SaveChangesAsync();

        Assert.Equal(1, await service.IndexHistoricalDocumentsAsync(clientId, "indexer@example.test"));
        Assert.Equal(0, await service.IndexHistoricalDocumentsAsync(clientId, "indexer@example.test"));
        var document = await db.ClientAdviceDocuments.SingleAsync(item => item.ClientId == clientId && item.ClientAdviceCaseId == null);
        Assert.Equal(ClientAdviceDocumentTypes.HistoricalRiskAnalyser, document.DocumentType);
        Assert.False(await db.ClientAdviceCases.AnyAsync(item => item.ClientId == clientId));
    }

    [Fact]
    public async Task Historical_advice_index_reads_the_mapped_client_folder_without_scan_metadata()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"kcas-advice-history-{Guid.NewGuid():N}");
        var faisFolder = Path.Combine(folder, "Storage Data", "FAIS Documents", "2022");
        Directory.CreateDirectory(faisFolder);
        var signedCar = Path.Combine(faisFolder, "Client Advice Record - signed.pdf");
        var genericFaisRecord = Path.Combine(folder, "Storage Data", "FAIS Documents", "2009.pdf");
        await File.WriteAllBytesAsync(signedCar, "%PDF-1.4 signed CAR"u8.ToArray());
        await File.WriteAllBytesAsync(genericFaisRecord, "%PDF-1.4 historical FAIS record"u8.ToArray());
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
            var clientId = await CreateClientAsync(db, "Folder Advice Client", clientFolder: folder);

            Assert.Equal(2, await service.IndexHistoricalDocumentsAsync(clientId, "indexer@example.test"));
            Assert.Equal(0, await service.IndexHistoricalDocumentsAsync(clientId, "indexer@example.test"));
            Assert.Equal(2, await db.ClientAdviceDocuments.CountAsync(item => item.ClientId == clientId && item.ClientAdviceCaseId == null));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static ClientAdviceEditModel Complete(ClientAdviceEditModel edit)
    {
        edit.AdviceScope = "Advise on a new long-term investment.";
        edit.MeetingSummary = "The client requested investment advice at a recorded meeting.";
        edit.NeedsAndObjectives = "Long-term capital growth with adequate emergency liquidity.";
        edit.FinancialSituation = "Income, expenses, existing investments and liquidity were reviewed.";
        edit.AdviceLimitations = "No material limitations.";
        edit.ProductKnowledgeSummary = "The client understands unit trusts, equities and market volatility.";
        edit.InvestmentAmount = 100_000;
        edit.InvestmentPortfolioPercent = 20;
        edit.DomesticPreferencePercent = 40;
        edit.OffshorePreferencePercent = 60;
        edit.FactSources.Add(new ClientAdviceFactSourceEditModel
        {
            FactName = "Needs, objectives and financial position",
            SourceDate = DateOnly.FromDateTime(DateTime.Today),
            DocumentPath = @"Client folder\Advice\Client fact find.pdf",
            Notes = "Current fact find reviewed with the client."
        });
        foreach (var response in edit.RiskResponses)
        {
            response.AnswerCode = ClientAdviceService.RiskQuestions.Single(question => question.Code == response.QuestionCode).Options[1].Code;
        }
        edit.Products.Add(new ClientAdviceProductEditModel
        {
            ProductName = "Considered Balanced Fund",
            Provider = "Example Provider",
            ProductType = "Unit trust",
            IsRecommended = false,
            Motivation = "Considered but offers less suitable growth exposure."
        });
        edit.Products.Add(new ClientAdviceProductEditModel
        {
            ProductName = "Recommended Growth Fund",
            Provider = "Example Provider",
            ProductType = "Unit trust",
            IsRecommended = true,
            Amount = 100_000,
            AllocationPercent = 100,
            Motivation = "Matches the time horizon and agreed growth objective."
        });
        edit.RecommendationSummary = "Invest R100 000 in the recommended diversified growth fund.";
        edit.RecommendationRationale = "The recommendation matches the client's horizon, liquidity and risk profile.";
        edit.CostsAndFees = "All initial, ongoing, advice and underlying fund charges were disclosed.";
        edit.TaxConsequences = "Applicable income and capital-gains tax consequences were discussed.";
        edit.LiquidityAndRestrictions = "Normal repurchase timing and liquidity limitations were explained.";
        edit.MaterialRisks = "Capital and returns are not guaranteed and market values may fall.";
        edit.ClientDeparture = "No departure from the recommendation.";
        edit.WarningsGiven = "The client was warned that past performance does not guarantee future returns.";
        return edit;
    }

    private static async Task<int> CreateClientAsync(ApplicationDbContext db, string name, string? kanaanId = null, string? clientFolder = null)
    {
        var client = new Client { DisplayName = $"{name} {Guid.NewGuid():N}", SurnameOrEntityName = name, KanaanId = kanaanId, ClientFolder = clientFolder, ClientCategory = ClientCategories.NaturalPerson };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }
}
