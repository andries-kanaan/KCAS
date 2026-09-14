using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class LegacyImportRunRecorderTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Delete_import_history_clears_runs_rows_differences_and_source_snapshots()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await LegacyImportWebService.DeleteImportHistoryAsync(db, CancellationToken.None);

        var run = new LegacyImportRun
        {
            Mode = LegacyImportModes.Scan,
            Status = LegacyImportRunStatuses.AwaitingReview,
            SourceLabel = "seed",
            SourceSnapshotSha256 = new string('b', 64),
            StartedAtUtc = DateTime.UtcNow
        };
        var row = new LegacyImportRowState
        {
            LegacyImportRun = run,
            SourceTable = "tbl_client",
            SourceId = 123,
            Classification = LegacyImportClassifications.Changed,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            IncomingFingerprint = new string('c', 64),
            IncomingPayloadJson = "{\"id\":\"123\",\"name\":\"Incoming\"}",
            BaselineFingerprint = new string('d', 64),
            BaselinePayloadJson = "{\"id\":\"123\",\"name\":\"Baseline\"}"
        };
        row.Differences.Add(new LegacyImportDifference
        {
            FieldName = "name",
            BaselineValue = "Baseline",
            IncomingValue = "Incoming"
        });
        db.LegacyImportRuns.Add(run);
        db.LegacyImportRowStates.Add(row);
        db.LegacySourceSnapshots.Add(new LegacySourceSnapshot
        {
            SourceTable = "tbl_client",
            SourceId = 123,
            Fingerprint = new string('d', 64),
            PayloadJson = "{\"id\":\"123\",\"name\":\"Baseline\"}",
            AcceptedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await LegacyImportWebService.DeleteImportHistoryAsync(db, CancellationToken.None);

        Assert.Equal(0, await db.LegacyImportRuns.CountAsync());
        Assert.Equal(0, await db.LegacyImportRowStates.CountAsync());
        Assert.Equal(0, await db.LegacyImportDifferences.CountAsync());
        Assert.Equal(0, await db.LegacySourceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Delete_compliance_operational_data_clears_reviews_and_scans_but_preserves_setup()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await LegacyImportWebService.DeleteComplianceOperationalDataAsync(db, CancellationToken.None);

        var client = new Client
        {
            LegacyClientId = 901001,
            DisplayName = "Baseline Reset Client",
            SurnameOrEntityName = "Reset Client",
            ClientCategory = ClientCategories.NaturalPerson
        };
        var requirement = new ClientEvidenceRequirement
        {
            ClientCategory = ClientCategories.NaturalPerson,
            RequirementGroup = "Baseline reset test",
            EvidenceType = "PEP",
            Title = "PEP screening",
            SortOrder = 999
        };
        var root = new ClientEvidenceScanRoot
        {
            RootPath = @"C:\EvidenceRoot",
            IsActive = true,
            UpdatedBy = "tester"
        };
        var run = new ClientEvidenceScanRun
        {
            RootPath = root.RootPath,
            Status = ClientEvidenceScanStatuses.Completed,
            StartedBy = "tester",
            TotalFiles = 1,
            LinkedFiles = 1,
            FinishedAtUtc = DateTime.UtcNow
        };
        var scanFile = new ClientEvidenceScanFile
        {
            ScanRun = run,
            Client = client,
            FullPath = @"C:\EvidenceRoot\pep.pdf",
            RelativePath = "pep.pdf",
            FileName = "pep.pdf",
            FileSha256 = new string('a', 64),
            FileLastWriteTimeUtc = DateTime.UtcNow,
            MatchStatus = ClientEvidenceScanFileStatuses.Linked
        };

        db.Clients.Add(client);
        db.ClientEvidenceRequirements.Add(requirement);
        db.ClientEvidenceScanRoots.Add(root);
        db.ClientEvidenceScanRuns.Add(run);
        db.ClientEvidenceScanFiles.Add(scanFile);
        db.ClientEvidenceItems.Add(new ClientEvidenceItem
        {
            Client = client,
            Requirement = requirement,
            ScanFile = scanFile,
            EvidenceType = requirement.EvidenceType,
            Title = requirement.Title,
            FileName = scanFile.FileName,
            Status = ClientEvidenceStatuses.Verified,
            Reviewer = "tester"
        });
        db.ClientEvidenceExceptions.Add(new ClientEvidenceException
        {
            Client = client,
            Requirement = requirement,
            Reason = "Legacy baseline test exception",
            ApprovedBy = "tester",
            ReviewDate = DateOnly.FromDateTime(DateTime.Today)
        });
        db.ComplianceTasks.Add(new ComplianceTask
        {
            Title = "Review legacy evidence",
            Status = ComplianceStatuses.Draft,
            LinkedEntityType = nameof(Client),
            LinkedEntityId = client.Id
        });
        db.ComplianceEvidence.Add(new ComplianceEvidence
        {
            EvidenceType = "Screening",
            Title = "Screening evidence",
            LinkedEntityType = nameof(Client),
            LinkedEntityId = client.Id
        });
        db.ComplianceApprovals.Add(new ComplianceApproval
        {
            TargetEntityType = nameof(ClientEvidenceException),
            TargetEntityId = 1,
            Decision = "Approved",
            Approver = "tester",
            Reason = "Legacy baseline test approval"
        });
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = nameof(ClientEvidenceItem),
            EntityId = 1,
            Action = "Verify",
            UserName = "tester",
            Reason = "Legacy baseline test audit"
        });
        await db.SaveChangesAsync();

        var requirementId = requirement.Id;
        var rootId = root.Id;

        await LegacyImportWebService.DeleteComplianceOperationalDataAsync(db, CancellationToken.None);

        Assert.Equal(0, await db.ClientEvidenceItems.CountAsync());
        Assert.Equal(0, await db.ClientEvidenceExceptions.CountAsync());
        Assert.Equal(0, await db.ClientEvidenceScanFiles.CountAsync());
        Assert.Equal(0, await db.ClientEvidenceScanRuns.CountAsync());
        Assert.Equal(0, await db.ComplianceTasks.CountAsync());
        Assert.Equal(0, await db.ComplianceEvidence.CountAsync());
        Assert.Equal(0, await db.ComplianceApprovals.CountAsync());
        Assert.Equal(0, await db.ComplianceAuditEvents.CountAsync());
        Assert.True(await db.ClientEvidenceRequirements.AnyAsync(item => item.Id == requirementId));
        Assert.True(await db.ClientEvidenceScanRoots.AnyAsync(item => item.Id == rootId));

        db.ChangeTracker.Clear();
        db.ClientEvidenceRequirements.RemoveRange(db.ClientEvidenceRequirements.Where(item => item.Id == requirementId));
        db.ClientEvidenceScanRoots.RemoveRange(db.ClientEvidenceScanRoots.Where(item => item.Id == rootId));
        db.Clients.RemoveRange(db.Clients.Where(item => item.Id == client.Id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_imported_fund_valuation_data_replaces_legacy_fund_set_and_tbl_fund_snapshots()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.ClientFundValuations.RemoveRange(db.ClientFundValuations.Where(item => item.LegacyFundId == 902101 || item.LegacyFundId == 902102));
        db.LegacySourceSnapshots.RemoveRange(db.LegacySourceSnapshots.Where(item => item.SourceId == 902001 || item.SourceId == 902101));
        db.Clients.RemoveRange(db.Clients.Where(item => item.LegacyClientId == 902001));
        await db.SaveChangesAsync();

        var client = new Client
        {
            LegacyClientId = 902001,
            DisplayName = "Fund Refresh Client",
            SurnameOrEntityName = "Fund Refresh Client",
            ClientCategory = ClientCategories.NaturalPerson
        };
        db.Clients.Add(client);
        db.ClientFundValuations.Add(new ClientFundValuation
        {
            Client = client,
            LegacyFundId = 902101,
            FundName = "Imported fund",
            PayloadJson = "{}",
            ImportedAtUtc = DateTime.UtcNow
        });
        db.ClientFundValuations.Add(new ClientFundValuation
        {
            Client = client,
            LegacyFundId = 902102,
            FundName = "Second imported fund",
            PayloadJson = "{}",
            ImportedAtUtc = DateTime.UtcNow
        });
        db.LegacySourceSnapshots.Add(new LegacySourceSnapshot
        {
            SourceTable = "tbl_fund",
            SourceId = 902101,
            PayloadJson = "{}",
            Fingerprint = new string('1', 64),
            AcceptedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        });
        db.LegacySourceSnapshots.Add(new LegacySourceSnapshot
        {
            SourceTable = "tbl_client",
            SourceId = 902001,
            PayloadJson = "{}",
            Fingerprint = new string('2', 64),
            AcceptedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await LegacyImportWebService.DeleteImportedFundValuationDataAsync(db, CancellationToken.None);

        Assert.False(await db.ClientFundValuations.AnyAsync(item => item.LegacyFundId == 902101));
        Assert.False(await db.ClientFundValuations.AnyAsync(item => item.LegacyFundId == 902102));
        Assert.False(await db.LegacySourceSnapshots.AnyAsync(item => item.SourceTable == "tbl_fund" && item.SourceId == 902101));
        Assert.True(await db.LegacySourceSnapshots.AnyAsync(item => item.SourceTable == "tbl_client" && item.SourceId == 902001));

        db.ChangeTracker.Clear();
        db.ClientFundValuations.RemoveRange(db.ClientFundValuations.Where(item => item.ClientId == client.Id));
        db.LegacySourceSnapshots.RemoveRange(db.LegacySourceSnapshots.Where(item => item.SourceId == 902001));
        db.Clients.RemoveRange(db.Clients.Where(item => item.LegacyClientId == 902001));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Existing_target_without_a_source_snapshot_is_changed_not_new()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.ApplyNew, "test-source", new string('a', 64));

        var row = recorder.Stage(
            "tbl_existing",
            500,
            "{\"id\":\"500\",\"name\":\"Existing\"}",
            existingBaselinePayloadJson: null,
            targetEntityType: "Client",
            targetEntityId: 42,
            appliedNew: true);
        await recorder.CompleteAsync(0);

        Assert.Equal(LegacyImportClassifications.Changed, row.Classification);
        Assert.Equal(LegacyImportApplyStatuses.PendingReview, row.ApplyStatus);
        Assert.Equal(0, recorder.Run.NewCount);
        Assert.Equal(1, recorder.Run.ChangedCount);
        Assert.Equal(0, recorder.Run.AppliedCount);
    }

    [Fact]
    public async Task Review_actions_record_reasoned_decisions()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<LegacyImportWebService>();
        await LegacyImportWebService.DeleteImportHistoryAsync(db, CancellationToken.None);

        var deferRow = await SeedReviewRowAsync(db, 8001, LegacyImportClassifications.Changed, withDifference: true);
        var rejectRow = await SeedReviewRowAsync(db, 8002, LegacyImportClassifications.MissingFromSource, withDifference: false);
        var manualRow = await SeedReviewRowAsync(db, 8003, LegacyImportClassifications.Changed, withDifference: true);

        await service.DeferReviewAsync(deferRow.Id, "reviewer@example.test", "Need adviser confirmation.");
        await service.RejectReviewAsync(rejectRow.Id, "reviewer@example.test", "Source deletion is not accepted.");
        await service.RecordManualResolutionAsync(manualRow.Id, "reviewer@example.test", "Corrected manually in KCAS.", "Resolved from client file.");
        db.ChangeTracker.Clear();

        var deferred = await db.LegacyImportRowStates.Include(row => row.Differences).SingleAsync(row => row.Id == deferRow.Id);
        Assert.Equal(LegacyImportApplyStatuses.PendingReview, deferred.ApplyStatus);
        Assert.All(deferred.Differences, difference =>
        {
            Assert.Equal(LegacyImportDecisionStatuses.Deferred, difference.Decision);
            Assert.Equal("Need adviser confirmation.", difference.ReviewReason);
            Assert.Equal("reviewer@example.test", difference.ReviewedBy);
            Assert.NotNull(difference.ReviewedAtUtc);
        });

        var rejected = await db.LegacyImportRowStates.Include(row => row.Differences).SingleAsync(row => row.Id == rejectRow.Id);
        Assert.Equal(LegacyImportApplyStatuses.NotApplicable, rejected.ApplyStatus);
        var rowDecision = Assert.Single(rejected.Differences);
        Assert.Equal("__row__", rowDecision.FieldName);
        Assert.Equal(LegacyImportDecisionStatuses.Rejected, rowDecision.Decision);
        Assert.Equal("Source deletion is not accepted.", rowDecision.ReviewReason);

        var manual = await db.LegacyImportRowStates.Include(row => row.Differences).SingleAsync(row => row.Id == manualRow.Id);
        Assert.Equal(LegacyImportApplyStatuses.NotApplicable, manual.ApplyStatus);
        Assert.All(manual.Differences, difference =>
        {
            Assert.Equal(LegacyImportDecisionStatuses.Corrected, difference.Decision);
            Assert.Equal("Corrected manually in KCAS.", difference.ResolvedValue);
            Assert.Equal("Resolved from client file.", difference.ReviewReason);
        });
    }

    [Fact]
    public async Task Accepted_new_source_becomes_the_idempotent_baseline_and_changes_remain_pending()
    {
        const string initial = "{\"id\":\"7001\",\"name\":\"Initial\"}";
        const string changed = "{\"id\":\"7001\",\"name\":\"Changed\"}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.ApplyNew, "test-source", new string('a', 64));
            var row = recorder.Stage("tbl_test", 7001, initial, null, "Test", null, appliedNew: true);
            await recorder.CompleteAsync(0);

            Assert.Equal(LegacyImportClassifications.New, row.Classification);
            Assert.Equal(LegacyImportApplyStatuses.Applied, row.ApplyStatus);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.Scan, "test-source", new string('a', 64));
            var row = recorder.Stage("tbl_test", 7001, initial, null, "Test", null);
            await recorder.CompleteAsync(0);

            Assert.Equal(LegacyImportClassifications.Unchanged, row.Classification);
            Assert.Equal(LegacyImportRunStatuses.Completed, recorder.Run.Status);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.Scan, "test-source", new string('a', 64));
            var row = recorder.Stage("tbl_test", 7001, changed, null, "Test", null);
            await recorder.CompleteAsync(0);

            Assert.Equal(LegacyImportClassifications.Changed, row.Classification);
            Assert.Equal(LegacyImportApplyStatuses.PendingReview, row.ApplyStatus);
            Assert.Equal(LegacyImportRunStatuses.AwaitingReview, recorder.Run.Status);
            Assert.Single(row.Differences);
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = await verificationDb.LegacySourceSnapshots.AsNoTracking()
            .SingleAsync(item => item.SourceTable == "tbl_test" && item.SourceId == 7001);
        Assert.Contains("Initial", snapshot.PayloadJson);
        Assert.DoesNotContain("Changed", snapshot.PayloadJson);
    }

    [Fact]
    public async Task Safe_changed_source_can_be_marked_ready_to_apply()
    {
        const string baseline = "{\"id\":\"7101\",\"email\":\"old@example.test\"}";
        const string incoming = "{\"id\":\"7101\",\"email\":\"new@example.test\"}";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.Scan, "test-source", new string('a', 64));
        var row = recorder.Stage(
            "tbl_client",
            7101,
            incoming,
            baseline,
            nameof(Client),
            7101,
            readyToApplyChanged: true);
        await recorder.CompleteAsync(0);

        Assert.Equal(LegacyImportClassifications.Changed, row.Classification);
        Assert.Equal(LegacyImportApplyStatuses.ReadyToApply, row.ApplyStatus);
        Assert.Equal(LegacyImportRunStatuses.Completed, recorder.Run.Status);
        Assert.Equal(1, recorder.Run.ChangedCount);
        Assert.Equal(0, recorder.Run.AppliedCount);
    }

    [Fact]
    public async Task Recommended_action_accepts_investment_metadata_without_overwriting_reviewed_surrender_date()
    {
        const int legacyClientId = 903001;
        const int legacyAccountId = 903101;
        const string baselinePayload = "{\"id\":\"903101\",\"client_id\":\"903001\",\"surrender_date\":null,\"date_updated\":\"05/08/2024 09:16:42\",\"updated_by\":\"Nonjabulo\",\"updated_by_id\":\"11\"}";
        const string incomingPayload = "{\"id\":\"903101\",\"client_id\":\"903001\",\"surrender_date\":null,\"date_updated\":\"09/11/2026 09:14:03\",\"updated_by\":\"Johannes Delport\",\"updated_by_id\":\"9\"}";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<LegacyImportWebService>();

        db.ClientInvestmentReconciliationReviews.RemoveRange(db.ClientInvestmentReconciliationReviews.Where(item => item.Client.LegacyClientId == legacyClientId));
        db.ClientInvestmentAccounts.RemoveRange(db.ClientInvestmentAccounts.Where(item => item.LegacyInvestmentAccountId == legacyAccountId));
        db.Clients.RemoveRange(db.Clients.Where(item => item.LegacyClientId == legacyClientId));
        db.LegacyImportRuns.RemoveRange(db.LegacyImportRuns.Where(item => item.SourceLabel == "recommended-action-test"));
        db.LegacySourceSnapshots.RemoveRange(db.LegacySourceSnapshots.Where(item => item.SourceTable == "tbl_investmentaccount" && item.SourceId == legacyAccountId));
        await db.SaveChangesAsync();

        var client = new Client
        {
            LegacyClientId = legacyClientId,
            DisplayName = "Recommended Action Client",
            SurnameOrEntityName = "Recommended Action Client",
            ClientCategory = ClientCategories.NaturalPerson
        };
        var account = new ClientInvestmentAccount
        {
            Client = client,
            LegacyInvestmentAccountId = legacyAccountId,
            LegacyClientId = legacyClientId,
            AccountNumber = "RA-REVIEWED",
            SurrenderDate = new DateOnly(2024, 3, 15),
            UpdatedBy = "codex@local",
            PayloadJson = baselinePayload,
            ImportedAtUtc = DateTime.UtcNow
        };
        db.Clients.Add(client);
        db.ClientInvestmentAccounts.Add(account);
        await db.SaveChangesAsync();

        db.ClientInvestmentReconciliationReviews.Add(new ClientInvestmentReconciliationReview
        {
            ClientId = client.Id,
            ClientInvestmentAccountId = account.Id,
            Outcome = ClientInvestmentReconciliationOutcomes.HistoricalSurrendered,
            AppliedSurrenderDate = account.SurrenderDate,
            EvidenceReference = "KCAS assessment",
            Reason = "Reviewed surrender date must be preserved.",
            SnapshotSha256 = new string('3', 64),
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedBy = "codex@local"
        });
        var run = new LegacyImportRun
        {
            Mode = LegacyImportModes.Scan,
            Status = LegacyImportRunStatuses.AwaitingReview,
            SourceLabel = "recommended-action-test",
            SourceSnapshotSha256 = new string('4', 64),
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            ChangedCount = 1
        };
        var row = new LegacyImportRowState
        {
            LegacyImportRun = run,
            SourceTable = "tbl_investmentaccount",
            SourceId = legacyAccountId,
            Classification = LegacyImportClassifications.Changed,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            TargetEntityType = nameof(ClientInvestmentAccount),
            TargetEntityId = account.Id,
            IncomingPayloadJson = incomingPayload,
            IncomingFingerprint = LegacyImportReconciler.Fingerprint(LegacyImportReconciler.CanonicalizePayload(incomingPayload)),
            BaselinePayloadJson = baselinePayload,
            BaselineFingerprint = LegacyImportReconciler.Fingerprint(LegacyImportReconciler.CanonicalizePayload(baselinePayload))
        };
        row.Differences.Add(new LegacyImportDifference { FieldName = "date_updated", BaselineValue = "05/08/2024 09:16:42", IncomingValue = "09/11/2026 09:14:03" });
        row.Differences.Add(new LegacyImportDifference { FieldName = "updated_by", BaselineValue = "Nonjabulo", IncomingValue = "Johannes Delport" });
        row.Differences.Add(new LegacyImportDifference { FieldName = "updated_by_id", BaselineValue = "11", IncomingValue = "9" });
        db.LegacyImportRowStates.Add(row);
        await db.SaveChangesAsync();

        await service.ApplyRecommendedActionAsync(row.Id, "reviewer@example.test");

        db.ChangeTracker.Clear();
        var updatedAccount = await db.ClientInvestmentAccounts.SingleAsync(item => item.LegacyInvestmentAccountId == legacyAccountId);
        var updatedRow = await db.LegacyImportRowStates.Include(item => item.Differences).SingleAsync(item => item.Id == row.Id);
        var snapshot = await db.LegacySourceSnapshots.SingleAsync(item => item.SourceTable == "tbl_investmentaccount" && item.SourceId == legacyAccountId);

        Assert.Equal(new DateOnly(2024, 3, 15), updatedAccount.SurrenderDate);
        Assert.Equal("Johannes Delport", updatedAccount.UpdatedBy);
        Assert.Equal(9, updatedAccount.LegacyUpdatedByUserId);
        Assert.Contains("Johannes Delport", updatedAccount.PayloadJson);
        Assert.Equal(LegacyImportApplyStatuses.Applied, updatedRow.ApplyStatus);
        Assert.All(updatedRow.Differences, difference => Assert.Equal(LegacyImportDecisionStatuses.AcceptLegacy, difference.Decision));
        Assert.Contains("Johannes Delport", snapshot.PayloadJson);
    }

    [Fact]
    public async Task Recommended_action_accepts_newer_kanaantrust_surrender_date()
    {
        const int legacyClientId = 903002;
        const int legacyAccountId = 903102;
        const string baselinePayload = "{\"id\":\"903102\",\"client_id\":\"903002\",\"surrender_date\":null,\"date_updated\":\"07/09/2026 09:15:04\",\"updated_by\":null,\"updated_by_id\":null}";
        const string incomingPayload = "{\"id\":\"903102\",\"client_id\":\"903002\",\"surrender_date\":\"09/03/2026 00:00:00\",\"date_updated\":\"09/11/2026 09:14:03\",\"updated_by\":\"Johannes Delport\",\"updated_by_id\":\"9\"}";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<LegacyImportWebService>();

        db.ClientInvestmentReconciliationReviews.RemoveRange(db.ClientInvestmentReconciliationReviews.Where(item => item.Client.LegacyClientId == legacyClientId));
        db.ClientInvestmentAccounts.RemoveRange(db.ClientInvestmentAccounts.Where(item => item.LegacyInvestmentAccountId == legacyAccountId));
        db.Clients.RemoveRange(db.Clients.Where(item => item.LegacyClientId == legacyClientId));
        db.LegacyImportRuns.RemoveRange(db.LegacyImportRuns.Where(item => item.SourceLabel == "recommended-surrender-test"));
        db.LegacySourceSnapshots.RemoveRange(db.LegacySourceSnapshots.Where(item => item.SourceTable == "tbl_investmentaccount" && item.SourceId == legacyAccountId));
        await db.SaveChangesAsync();

        var client = new Client
        {
            LegacyClientId = legacyClientId,
            DisplayName = "Recommended Surrender Client",
            SurnameOrEntityName = "Recommended Surrender Client",
            ClientCategory = ClientCategories.NaturalPerson
        };
        var account = new ClientInvestmentAccount
        {
            Client = client,
            LegacyInvestmentAccountId = legacyAccountId,
            LegacyClientId = legacyClientId,
            AccountNumber = "IA-CURRENT",
            SurrenderDate = null,
            UpdatedBy = "codex@local",
            PayloadJson = baselinePayload,
            ImportedAtUtc = DateTime.UtcNow
        };
        db.Clients.Add(client);
        db.ClientInvestmentAccounts.Add(account);
        await db.SaveChangesAsync();

        db.ClientInvestmentReconciliationReviews.Add(new ClientInvestmentReconciliationReview
        {
            ClientId = client.Id,
            ClientInvestmentAccountId = account.Id,
            Outcome = ClientInvestmentReconciliationOutcomes.Current,
            AppliedSurrenderDate = null,
            EvidenceReference = "KCAS current valuation",
            Reason = "Reviewed as current before the later KanaanTrust surrender update.",
            SnapshotSha256 = new string('5', 64),
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedBy = "codex@local"
        });
        var run = new LegacyImportRun
        {
            Mode = LegacyImportModes.Scan,
            Status = LegacyImportRunStatuses.AwaitingReview,
            SourceLabel = "recommended-surrender-test",
            SourceSnapshotSha256 = new string('6', 64),
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            ChangedCount = 1
        };
        var row = new LegacyImportRowState
        {
            LegacyImportRun = run,
            SourceTable = "tbl_investmentaccount",
            SourceId = legacyAccountId,
            Classification = LegacyImportClassifications.Changed,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            TargetEntityType = nameof(ClientInvestmentAccount),
            TargetEntityId = account.Id,
            IncomingPayloadJson = incomingPayload,
            IncomingFingerprint = LegacyImportReconciler.Fingerprint(LegacyImportReconciler.CanonicalizePayload(incomingPayload)),
            BaselinePayloadJson = baselinePayload,
            BaselineFingerprint = LegacyImportReconciler.Fingerprint(LegacyImportReconciler.CanonicalizePayload(baselinePayload))
        };
        row.Differences.Add(new LegacyImportDifference { FieldName = "date_updated", BaselineValue = "07/09/2026 09:15:04", IncomingValue = "09/11/2026 09:14:03" });
        row.Differences.Add(new LegacyImportDifference { FieldName = "surrender_date", BaselineValue = null, IncomingValue = "09/03/2026 00:00:00" });
        row.Differences.Add(new LegacyImportDifference { FieldName = "updated_by", BaselineValue = null, IncomingValue = "Johannes Delport" });
        row.Differences.Add(new LegacyImportDifference { FieldName = "updated_by_id", BaselineValue = null, IncomingValue = "9" });
        db.LegacyImportRowStates.Add(row);
        await db.SaveChangesAsync();

        Assert.Contains("Accept the KanaanTrust surrender date", service.RecommendedActionDescription(row));

        await service.ApplyRecommendedActionAsync(row.Id, "reviewer@example.test");

        db.ChangeTracker.Clear();
        var updatedAccount = await db.ClientInvestmentAccounts.SingleAsync(item => item.LegacyInvestmentAccountId == legacyAccountId);
        var updatedRow = await db.LegacyImportRowStates.Include(item => item.Differences).SingleAsync(item => item.Id == row.Id);
        var snapshot = await db.LegacySourceSnapshots.SingleAsync(item => item.SourceTable == "tbl_investmentaccount" && item.SourceId == legacyAccountId);
        var latestReview = await db.ClientInvestmentReconciliationReviews
            .Where(item => item.ClientInvestmentAccountId == updatedAccount.Id)
            .OrderByDescending(item => item.ReviewedAtUtc)
            .FirstAsync();

        Assert.Equal(new DateOnly(2026, 9, 3), updatedAccount.SurrenderDate);
        Assert.Equal("Johannes Delport", updatedAccount.UpdatedBy);
        Assert.Equal(9, updatedAccount.LegacyUpdatedByUserId);
        Assert.Contains("09/03/2026", updatedAccount.PayloadJson);
        Assert.Equal(LegacyImportApplyStatuses.Applied, updatedRow.ApplyStatus);
        Assert.All(updatedRow.Differences, difference => Assert.Equal(LegacyImportDecisionStatuses.AcceptLegacy, difference.Decision));
        Assert.Contains("09/03/2026", snapshot.PayloadJson);
        Assert.Equal(ClientInvestmentReconciliationOutcomes.HistoricalSurrendered, latestReview.Outcome);
        Assert.Equal(new DateOnly(2026, 9, 3), latestReview.AppliedSurrenderDate);
        Assert.Contains("KanaanTrust SQL import run", latestReview.EvidenceReference);
    }

    private static async Task<LegacyImportRowState> SeedReviewRowAsync(
        ApplicationDbContext db,
        long sourceId,
        string classification,
        bool withDifference)
    {
        var run = new LegacyImportRun
        {
            Mode = LegacyImportModes.Scan,
            Status = LegacyImportRunStatuses.AwaitingReview,
            SourceLabel = "test-source",
            SourceSnapshotSha256 = new string('e', 64),
            StartedAtUtc = DateTime.UtcNow
        };
        var row = new LegacyImportRowState
        {
            LegacyImportRun = run,
            SourceTable = "tbl_client",
            SourceId = sourceId,
            Classification = classification,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            IncomingFingerprint = new string('f', 64),
            IncomingPayloadJson = $"{{\"id\":\"{sourceId}\",\"name\":\"Incoming\"}}",
            BaselineFingerprint = new string('0', 64),
            BaselinePayloadJson = $"{{\"id\":\"{sourceId}\",\"name\":\"Baseline\"}}"
        };
        if (withDifference)
        {
            row.Differences.Add(new LegacyImportDifference
            {
                FieldName = "name",
                BaselineValue = "Baseline",
                IncomingValue = "Incoming"
            });
        }

        db.LegacyImportRowStates.Add(row);
        await db.SaveChangesAsync();
        return row;
    }
}
