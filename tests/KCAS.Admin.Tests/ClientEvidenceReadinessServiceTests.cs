using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientEvidenceReadinessServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Readiness_defaults_block_clients_until_required_evidence_is_verified()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Evidence Client", "EVID-001", @"z:\Kanaan Trust\Clients\Evidence Client");

        var readiness = await service.LoadClientReadinessAsync(clientId);

        Assert.True(readiness.RequiredCount >= 10);
        Assert.False(readiness.IsReadyForRiskAssessment);
        Assert.True(readiness.BlockedCount > 0);
    }

    [Fact]
    public async Task Requirement_matrix_varies_for_trust_clients()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var naturalPersonId = await CreateClientAsync(db, "Natural Matrix Client", "NAT-001", @"z:\Kanaan Trust\Clients\Natural Matrix Client");
        var trustId = await CreateClientAsync(db, "Trust Matrix Client", "TRUST-001", @"z:\Kanaan Trust\Clients\Trust Matrix Client", ClientCategories.Trust);

        var naturalPerson = await service.LoadClientReadinessAsync(naturalPersonId);
        var trust = await service.LoadClientReadinessAsync(trustId);

        Assert.True(trust.RequiredCount > naturalPerson.RequiredCount);
        Assert.Contains(trust.Requirements, requirement => requirement.EvidenceType == "TrustDeed");
        Assert.DoesNotContain(naturalPerson.Requirements, requirement => requirement.EvidenceType == "TrustDeed");
    }

    [Fact]
    public async Task Verified_evidence_and_exceptions_can_make_client_ready()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Ready Client", "READY-001", @"z:\Kanaan Trust\Clients\Ready Client");

        var initial = await service.LoadClientReadinessAsync(clientId);
        foreach (var requirement in initial.Requirements)
        {
            if (requirement.RequirementId == initial.Requirements[0].RequirementId)
            {
                db.ClientEvidenceItems.Add(new ClientEvidenceItem
                {
                    ClientId = clientId,
                    ClientEvidenceRequirementId = requirement.RequirementId,
                    EvidenceType = requirement.EvidenceType,
                    Title = requirement.Title,
                    Status = ClientEvidenceStatuses.Linked
                });
                await db.SaveChangesAsync();
                var itemId = await db.ClientEvidenceItems
                    .Where(item => item.ClientId == clientId && item.ClientEvidenceRequirementId == requirement.RequirementId)
                    .Select(item => item.Id)
                    .SingleAsync();
                await service.VerifyEvidenceAsync(itemId, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddYears(1)), "reviewer@example.test", "Verify sampled evidence.");
            }
            else
            {
                await service.CreateExceptionAsync(clientId, requirement.RequirementId, "Temporary audit-readiness exception.", DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), "approver@example.test", "Approve temporary exception.");
            }
        }

        var readiness = await service.LoadClientReadinessAsync(clientId);

        Assert.True(readiness.IsReadyForRiskAssessment);
        Assert.Equal(0, readiness.BlockedCount);
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(audit => audit.EntityType == "ClientEvidenceException"));
    }

    [Fact]
    public async Task Scan_links_matching_files_and_is_idempotent()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Folder Match Client", "FOLDER-001", @"z:\Kanaan Trust\Clients\Folder Match Client");
        var root = CreateTempRoot();
        var clientFolder = Directory.CreateDirectory(Path.Combine(root, "Folder Match Client"));
        await File.WriteAllTextAsync(Path.Combine(clientFolder.FullName, "identity document.pdf"), "identity");

        var firstRunId = await service.RunScanAsync(root, "scanner@example.test", "Initial evidence scan.");
        var secondRunId = await service.RunScanAsync(root, "scanner@example.test", "Repeat evidence scan.");

        Assert.NotEqual(firstRunId, secondRunId);
        Assert.Equal(1, await db.ClientEvidenceItems.CountAsync(item => item.ClientId == clientId));
        Assert.Equal(2, await db.ClientEvidenceScanRuns.CountAsync(run => run.Id == firstRunId || run.Id == secondRunId));
        Assert.Equal(2, await db.ClientEvidenceScanFiles.CountAsync(file => file.ClientId == clientId && file.MatchStatus == ClientEvidenceScanFileStatuses.Linked));
    }

    [Fact]
    public async Task Scan_uses_client_folder_segment_to_avoid_family_name_ambiguity()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var agId = await CreateClientAsync(db, "Arie Gysbert", "264", @"z:\Kanaan Trust\Clients\Clients\Fourie AG", initials: "AG", fullName: "Arie Gysbert", surnameOrEntityName: "Fourie");
        await CreateClientAsync(db, "Petrus Johannes", "030", @"z:\Kanaan Trust\Clients\Clients\Fourie PJ", initials: "PJ", fullName: "Petrus Johannes", surnameOrEntityName: "Fourie");
        await CreateClientAsync(db, "Anneke", "131", @"z:\Kanaan Trust\Clients\Clients\Fourie A", initials: "A", fullName: "Anneke", surnameOrEntityName: "Fourie");
        var root = CreateTempRoot();
        var folder = Directory.CreateDirectory(Path.Combine(root, "FOURIE AG", "Storage Data", "Application Forms", "Unit Trust"));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "Kanaan BCI Withdrawal Form (Fourie AG).pdf"), "withdrawal");

        await service.RunScanAsync(root, "scanner@example.test", "Scan Fourie folders.");

        var scanFile = await db.ClientEvidenceScanFiles.SingleAsync(file => file.FileName == "Kanaan BCI Withdrawal Form (Fourie AG).pdf");
        Assert.Equal(ClientEvidenceScanFileStatuses.Linked, scanFile.MatchStatus);
        Assert.Equal(agId, scanFile.ClientId);
        Assert.Equal(1, scanFile.CandidateCount);
    }

    [Fact]
    public async Task Client_folder_scan_forces_selected_client_and_resolves_prior_ambiguity()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var selectedClientId = await CreateClientAsync(db, "John Smith", "SMITH-001", @"z:\Kanaan Trust\Clients\Old Smith J", initials: "J", fullName: "John", surnameOrEntityName: "Smith");
        await CreateClientAsync(db, "Jane Smith", "SMITH-002", @"z:\Kanaan Trust\Clients\Smith J", initials: "J", fullName: "Jane", surnameOrEntityName: "Smith");
        var root = CreateTempRoot();
        var selectedFolder = Directory.CreateDirectory(Path.Combine(root, "Smith J", "Storage Data", "FICA"));
        var evidencePath = Path.Combine(selectedFolder.FullName, "identity document.pdf");
        await File.WriteAllTextAsync(evidencePath, "identity");

        await service.RunScanAsync(root, "scanner@example.test", "Scan ambiguous family folder.");
        var ambiguousFile = await db.ClientEvidenceScanFiles.SingleAsync(file => file.FullPath == evidencePath);
        Assert.Equal(ClientEvidenceScanFileStatuses.Ambiguous, ambiguousFile.MatchStatus);
        Assert.Null(ambiguousFile.ClientId);

        await service.RunClientFolderScanAsync(selectedClientId, Path.Combine(root, "Smith J"), "scanner@example.test", "Resolve selected client folder.");

        var client = await db.Clients.SingleAsync(client => client.Id == selectedClientId);
        Assert.Equal(Path.Combine(root, "Smith J"), client.ClientFolder);
        Assert.Equal(1, await db.ClientEvidenceItems.CountAsync(item => item.ClientId == selectedClientId));
        var resolvedPriorFile = await db.ClientEvidenceScanFiles.SingleAsync(file => file.Id == ambiguousFile.Id);
        Assert.Equal(ClientEvidenceScanFileStatuses.Linked, resolvedPriorFile.MatchStatus);
        Assert.Equal(selectedClientId, resolvedPriorFile.ClientId);
    }

    [Fact]
    public async Task Scan_suggests_evidence_types_from_sample_folder_patterns()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Philip Nel", "354", @"z:\Kanaan Trust\Clients\Clients\Badenhorst PN", initials: "PN", fullName: "Philip Nel", surnameOrEntityName: "Badenhorst");
        var root = CreateTempRoot();
        await WriteFileAsync(root, "BADENHORST PN", "Storage Data", "FICA", "Individual", "Proof of address PN Badenhorst.pdf");
        await WriteFileAsync(root, "BADENHORST PN", "Storage Data", "Tax", "2025", "SARS tax certificate.pdf");
        await WriteFileAsync(root, "BADENHORST PN", "Storage Data", "Application Forms", "Offshore", "Bidvest", "BOP.pdf");
        await WriteFileAsync(root, "BADENHORST PN", "Storage Data", "Application Forms", "Unit Trust", "AIMS", "Beneficiaries", "Beneficiary Nomination.pdf");

        await service.RunScanAsync(root, "scanner@example.test", "Scan sampled evidence types.");

        var files = await db.ClientEvidenceScanFiles.Where(file => file.ClientId == clientId).ToListAsync();
        Assert.Contains(files, file => file.FileName == "Proof of address PN Badenhorst.pdf" && file.SuggestedEvidenceType == "Address");
        Assert.Contains(files, file => file.FileName == "SARS tax certificate.pdf" && file.SuggestedEvidenceType == "TaxResidency");
        Assert.Contains(files, file => file.FileName == "BOP.pdf" && file.SuggestedEvidenceType == "SourceOfFunds");
        Assert.Contains(files, file => file.FileName == "Beneficiary Nomination.pdf" && file.SuggestedEvidenceType == "BeneficialOwnership");
    }

    [Fact]
    public async Task Scan_corrects_imported_natural_person_category_from_strong_trust_evidence()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(
            db,
            "Evidence Category Client",
            "CAT-TRUST-001",
            @"z:\Kanaan Trust\Clients\Evidence Category Client",
            categorySource: ClientCategorySources.LegacyImportInferred);
        var root = CreateTempRoot();
        await WriteFileAsync(root, "Evidence Category Client", "Storage Data", "Trust", "Trust Deed.pdf");

        await service.RunScanAsync(root, "scanner@example.test", "Scan category evidence.");

        var client = await db.Clients.SingleAsync(client => client.Id == clientId);
        Assert.Equal(ClientCategories.Trust, client.ClientCategory);
        Assert.Equal(ClientCategorySources.EvidenceScanInferred, client.ClientCategorySource);
        Assert.Contains("trust", client.ClientCategoryReason!, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(audit =>
            audit.EntityType == "Client" &&
            audit.EntityId == clientId &&
            audit.Action == "InferClientCategoryFromEvidence"));
    }

    [Fact]
    public async Task Scan_does_not_override_manual_client_category()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(
            db,
            "Manual Category Client",
            "CAT-MANUAL-001",
            @"z:\Kanaan Trust\Clients\Manual Category Client",
            ClientCategories.LegalPerson,
            ClientCategorySources.Manual);
        var root = CreateTempRoot();
        await WriteFileAsync(root, "Manual Category Client", "Storage Data", "Trust", "Trust Deed.pdf");

        await service.RunScanAsync(root, "scanner@example.test", "Scan category evidence.");

        var client = await db.Clients.SingleAsync(client => client.Id == clientId);
        Assert.Equal(ClientCategories.LegalPerson, client.ClientCategory);
        Assert.Equal(ClientCategorySources.Manual, client.ClientCategorySource);
        Assert.False(await db.ComplianceAuditEvents.AnyAsync(audit =>
            audit.EntityType == "Client" &&
            audit.EntityId == clientId &&
            audit.Action == "InferClientCategoryFromEvidence"));
    }

    [Fact]
    public async Task Scan_linked_evidence_does_not_complete_requirements_until_verified()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Linked Review Client", "LINK-001", @"z:\Kanaan Trust\Clients\Linked Review Client");
        var root = CreateTempRoot();
        var clientFolder = Directory.CreateDirectory(Path.Combine(root, "Linked Review Client"));
        await File.WriteAllTextAsync(Path.Combine(clientFolder.FullName, "identity document.pdf"), "identity");

        await service.RunScanAsync(root, "scanner@example.test", "Scan linked evidence.");
        var readiness = await service.LoadClientReadinessAsync(clientId);

        Assert.True(readiness.LinkedEvidenceCount > 0);
        Assert.Equal(0, readiness.VerifiedEvidenceCount);
        Assert.Equal(0, readiness.CompleteCount);
        Assert.True(readiness.BlockedCount > 0);
    }

    [Fact]
    public async Task Batch_verification_updates_verified_and_readiness_counts()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Batch Verify Client", "BATCH-001", @"z:\Kanaan Trust\Clients\Batch Verify Client");
        var requirementId = await EnsureRequirementIdAsync(service, db, "TaxResidency");
        db.ClientEvidenceItems.Add(new ClientEvidenceItem
        {
            ClientId = clientId,
            ClientEvidenceRequirementId = requirementId,
            EvidenceType = "TaxResidency",
            Title = "Tax evidence",
            Status = ClientEvidenceStatuses.Linked
        });
        await db.SaveChangesAsync();
        var itemId = await db.ClientEvidenceItems.Where(item => item.ClientId == clientId).Select(item => item.Id).SingleAsync();

        var verifiedCount = await service.VerifyEvidenceBatchAsync(clientId, [itemId], DateOnly.FromDateTime(DateTime.Today), null, "reviewer@example.test", "Verify batch evidence.");
        var readiness = await service.LoadClientReadinessAsync(clientId);

        Assert.Equal(1, verifiedCount);
        Assert.Equal(1, readiness.VerifiedEvidenceCount);
        Assert.Contains(readiness.Requirements, requirement => requirement.EvidenceType == "TaxResidency" && requirement.IsComplete);
    }

    [Fact]
    public async Task Record_review_creates_verified_evidence_for_screening_requirements()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Screening Review Client", "SCREEN-001", @"z:\Kanaan Trust\Clients\Screening Review Client");
        var requirementId = await EnsureRequirementIdAsync(service, db, "PepPip");

        var itemId = await service.RecordRequirementReviewAsync(clientId, requirementId, "reviewer@example.test", "PEP/PIP screening completed with no match.");
        var readiness = await service.LoadClientReadinessAsync(clientId);

        var item = await db.ClientEvidenceItems.SingleAsync(item => item.Id == itemId);
        Assert.Equal(ClientEvidenceStatuses.Verified, item.Status);
        Assert.Equal("reviewer@example.test", item.Reviewer);
        Assert.NotNull(item.VerifiedDate);
        Assert.Equal(ClientEvidenceRiskSignals.Low, item.ScreeningRiskSignal);
        Assert.Equal(ClientEvidenceScreeningOutcomes.NoMatch, item.ScreeningOutcome);
        Assert.Contains(readiness.Requirements, requirement => requirement.EvidenceType == "PepPip" && requirement.IsComplete);
    }

    [Fact]
    public async Task Screening_review_requires_notes_for_higher_risk_or_layered_clients()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Layered Trust", "TRUST-REV-001", @"z:\Kanaan Trust\Clients\Layered Trust", ClientCategories.Trust);
        var requirementId = await EnsureRequirementIdAsync(service, db, "PepPip");

        await Assert.ThrowsAsync<ValidationException>(() => service.RecordRequirementReviewAsync(clientId, requirementId, new ClientEvidenceScreeningReviewRequest
        {
            SubjectType = ClientEvidenceScreeningSubjectTypes.Trustee,
            SubjectName = "Trustee One",
            Outcome = ClientEvidenceScreeningOutcomes.NoMatch,
            RiskSignal = ClientEvidenceRiskSignals.Low,
            ReviewDate = DateOnly.FromDateTime(DateTime.Today)
        }, "reviewer@example.test", null));
    }

    [Fact]
    public async Task Sanctions_confirmed_match_records_escalation_signal()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Sanctions Review Client", "SAN-001", @"z:\Kanaan Trust\Clients\Sanctions Review Client");
        var requirementId = await EnsureRequirementIdAsync(service, db, "SanctionsTfs");

        var itemId = await service.RecordRequirementReviewAsync(clientId, requirementId, new ClientEvidenceScreeningReviewRequest
        {
            SubjectType = ClientEvidenceScreeningSubjectTypes.Client,
            SubjectName = "Sanctions Review Client",
            Outcome = ClientEvidenceScreeningOutcomes.ConfirmedMatch,
            RiskSignal = ClientEvidenceRiskSignals.High,
            ReviewDate = DateOnly.FromDateTime(DateTime.Today),
            Notes = "Confirmed sanctions match escalated for formal handling."
        }, "reviewer@example.test", null);

        var item = await db.ClientEvidenceItems.SingleAsync(item => item.Id == itemId);
        Assert.True(item.EscalationRequired);
        Assert.Equal(ClientEvidenceRiskSignals.High, item.ScreeningRiskSignal);
        Assert.Contains((await service.LoadClientReadinessAsync(clientId)).Requirements, requirement => requirement.EvidenceType == "SanctionsTfs" && requirement.IsComplete);
    }

    [Fact]
    public async Task Manual_scan_file_resolution_links_without_verifying()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Manual Resolve Client", "MAN-001", @"z:\Kanaan Trust\Clients\Manual Resolve Client");
        var run = new ClientEvidenceScanRun { RootPath = CreateTempRoot(), Status = ClientEvidenceScanStatuses.Completed };
        var scanFile = new ClientEvidenceScanFile
        {
            ScanRun = run,
            FullPath = Path.Combine(run.RootPath, "unknown passport.pdf"),
            RelativePath = "unknown passport.pdf",
            FileName = "unknown passport.pdf",
            FileSha256 = "abc",
            MatchStatus = ClientEvidenceScanFileStatuses.Unmatched,
            SuggestedEvidenceType = "Identity"
        };
        db.ClientEvidenceScanRuns.Add(run);
        db.ClientEvidenceScanFiles.Add(scanFile);
        await db.SaveChangesAsync();

        await service.ResolveScanFileAsync(scanFile.Id, clientId, "Identity", "reviewer@example.test", "Resolve scan file.");

        var item = await db.ClientEvidenceItems.SingleAsync(item => item.ClientId == clientId);
        Assert.Equal(ClientEvidenceStatuses.Linked, item.Status);
        Assert.Null(item.VerifiedDate);
        Assert.Equal(ClientEvidenceScanFileStatuses.Linked, (await db.ClientEvidenceScanFiles.SingleAsync(file => file.Id == scanFile.Id)).MatchStatus);
    }

    [Fact]
    public async Task Scan_keeps_unmatched_files_for_review()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "unknown passport.pdf"), "identity");

        await service.RunScanAsync(root, "scanner@example.test", "Scan unmatched evidence.");

        var file = await db.ClientEvidenceScanFiles
            .OrderByDescending(file => file.Id)
            .FirstAsync();
        Assert.Equal(ClientEvidenceScanFileStatuses.Unmatched, file.MatchStatus);
        Assert.Null(file.ClientId);
    }

    [Fact]
    public async Task Stale_running_scan_can_be_cancelled()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = CreateTempRoot();

        var runId = await service.StartScanRunAsync(root, "scanner@example.test", "Start cancellable scan.");
        await service.CancelUntrackedScanAsync(runId, "scanner@example.test", "Cancel stale scan.");

        var run = await db.ClientEvidenceScanRuns.SingleAsync(run => run.Id == runId);
        Assert.Equal(ClientEvidenceScanStatuses.Cancelled, run.Status);
        Assert.NotNull(run.FinishedAtUtc);
    }

    [Fact]
    public async Task Scan_root_requires_existing_server_path_and_reason()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();

        await Assert.ThrowsAsync<ValidationException>(() => service.SaveScanRootAsync("", "admin@example.test", ""));
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveScanRootAsync(@"C:\definitely-not-a-kcas-folder", "admin@example.test", "Set scan root."));
    }

    [Fact]
    public async Task Folder_browser_lists_server_side_child_folders()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var root = CreateTempRoot();
        var child = Directory.CreateDirectory(Path.Combine(root, "Client Documents"));

        var browser = await service.BrowseServerFoldersAsync(root);
        var childBrowser = await service.BrowseServerFoldersAsync(child.FullName);

        Assert.Equal(root, browser.CurrentPath);
        Assert.Contains(browser.Folders, folder => folder.Name == "Client Documents" && folder.FullPath == child.FullName);
        Assert.Equal(child.FullName, childBrowser.CurrentPath);
        Assert.Equal(root, childBrowser.ParentPath);
    }

    private static async Task<int> CreateClientAsync(
        ApplicationDbContext db,
        string name,
        string kanaanId,
        string folder,
        string category = ClientCategories.NaturalPerson,
        string categorySource = ClientCategorySources.LegacyImportInferred,
        string? initials = null,
        string? fullName = null,
        string? surnameOrEntityName = null)
    {
        var client = new Client
        {
            DisplayName = name,
            Initials = initials,
            FullName = fullName,
            SurnameOrEntityName = surnameOrEntityName ?? name,
            KanaanId = kanaanId,
            ClientFolder = folder,
            ClientCategory = category,
            ClientCategorySource = categorySource
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "kcas-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WriteFileAsync(string root, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Path.GetFileName(path));
    }

    private static async Task<int> EnsureRequirementIdAsync(ClientEvidenceReadinessService service, ApplicationDbContext db, string evidenceType)
    {
        await service.LoadDashboardAsync();
        return await db.ClientEvidenceRequirements
            .Where(requirement => requirement.EvidenceType == evidenceType)
            .Select(requirement => requirement.Id)
            .SingleAsync();
    }
}
