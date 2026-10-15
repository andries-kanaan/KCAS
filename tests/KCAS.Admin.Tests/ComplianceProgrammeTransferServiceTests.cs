using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ComplianceProgrammeTransferServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Programme_bundle_exports_applies_once_and_maps_signed_final_paths_to_live_root()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fileRoot = Path.Combine(Path.GetTempPath(), "kcas-programme-pi-" + Guid.NewGuid().ToString("N"));
        var sourceFiles = InsuranceServiceTests.Files(Path.Combine(fileRoot, "source"));
        var liveFiles = InsuranceServiceTests.Files(Path.Combine(fileRoot, "live"));
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        ComplianceProgrammeTransferService Transfer(InsuranceFiles files) => new(db,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Insurance:ComplianceRoot"] = files.Root,
                ["ComplianceProgrammeTransfers:StorageRoot"] = Path.Combine(fileRoot, "packages") }).Build(), environment);
        var transfers = Transfer(sourceFiles);
        var policy = InsuranceServiceTests.Policy();
        var piPath = "Compliance/PI Cover/2030/quote.pdf";
        string? outgoingPath = null;
        string? incomingPath = null;

        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var localRoot = ComplianceProgrammeTransferService.DefaultLocalSignedFinalRoot;
            var sourceRmcpPath = Path.Combine(localRoot, $"RMCP-{suffix}.pdf");
            var sourceBraPath = Path.Combine(localRoot, $"BRA-{suffix}.pdf");
            var sourceEvidencePath = Path.Combine(localRoot, $"Evidence-{suffix}.pdf");

            var bra = new BusinessRiskAssessment
            {
                Name = $"Transfer BRA {suffix}",
                AssessmentYear = 2026,
                AsAtDate = new DateOnly(2026, 7, 31),
                Status = ComplianceStatuses.Active,
                Scope = "Whole-business FIC Act programme scope.",
                MethodologyNarrative = "Likelihood and impact are assessed using the approved methodology.",
                ManagementJudgement = "Residual risk is acceptable within the approved tolerance.",
                Limitations = "Limited to signed-final inspection pack evidence.",
                RiskTolerance = "No uncontrolled high residual risk.",
                SnapshotJson = """{"name":"Transfer BRA"}""",
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-2),
                PreparedBy = "tester@example.test",
                UpdatedBy = "tester@example.test",
                Items =
                [
                    new()
                    {
                        Category = BusinessRiskCategories.Clients,
                        RiskStatement = "Client risk exposure.",
                        EvidenceAndRationale = "Supported by the signed BRA.",
                        Likelihood = 2,
                        Impact = 3,
                        InherentScore = 6,
                        InherentRating = BusinessRiskRatings.High,
                        KeyControls = "CDD, EDD and monitoring controls.",
                        ControlEffectiveness = BusinessRiskControlEffectiveness.Effective,
                        ResidualRating = BusinessRiskRatings.Standard,
                        ResidualRationale = "Controls reduce residual risk.",
                        TreatmentDecision = BusinessRiskTreatmentDecisions.Accept,
                        Owner = "Key Individuals",
                        SortOrder = 10
                    }
                ],
                Approvals =
                [
                    new()
                    {
                        Approver = "ki@example.test",
                        Reason = "Approved for transfer test.",
                        ApprovedAtUtc = DateTime.UtcNow.AddDays(-1)
                    }
                ]
            };
            db.BusinessRiskAssessments.Add(bra);
            await db.SaveChangesAsync();

            var rmcp = new RmcpVersion
            {
                BusinessRiskAssessmentId = bra.Id,
                Title = $"Transfer RMCP {suffix}",
                VersionReference = $"2026-transfer-{suffix}",
                Status = ComplianceStatuses.Active,
                Scope = "FIC Act RMCP controls.",
                Owner = "Key Individuals",
                ReviewMonths = 12,
                EffectiveDate = new DateOnly(2026, 7, 31),
                NextReviewDate = new DateOnly(2027, 7, 31),
                SignedDocumentLocation = sourceRmcpPath,
                ApprovalResolutionLocation = sourceBraPath,
                ChangeSummary = "Signed final transfer package.",
                SnapshotJson = """{"title":"Transfer RMCP"}""",
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-1),
                PreparedBy = "tester@example.test",
                UpdatedBy = "tester@example.test",
                Controls =
                [
                    new()
                    {
                        BusinessRiskItemId = bra.Items.Single().Id,
                        Domain = RmcpControlDomains.ClientRisk,
                        Code = $"CTRL-{suffix}",
                        Title = "Client risk assessment control",
                        ProcedureSummary = "Assess and document client risk.",
                        Owner = "Compliance",
                        Frequency = "Per client review",
                        EvidenceExpectation = "Current assessment and linked evidence.",
                        MonitoringMethod = "Periodic KI review.",
                        EscalationProcedure = "Escalate material exceptions.",
                        SortOrder = 10
                    }
                ]
            };
            db.RmcpVersions.Add(rmcp);
            db.ControlledDocuments.Add(new ControlledDocument
            {
                DocumentType = "RMCP",
                Title = $"Signed RMCP {suffix}",
                Owner = "Key Individuals",
                VersionReference = rmcp.VersionReference,
                Status = ComplianceStatuses.Active,
                EffectiveDate = rmcp.EffectiveDate,
                Location = sourceRmcpPath,
                UpdatedBy = "tester@example.test"
            });
            db.ComplianceEvidence.Add(new ComplianceEvidence
            {
                EvidenceType = "SignedFinal",
                Title = $"Signed evidence {suffix}",
                Source = "Signed final folder",
                Location = sourceEvidencePath,
                VerifiedDate = new DateOnly(2026, 7, 31),
                Reviewer = "tester@example.test",
                LinkedEntityType = nameof(BusinessRiskAssessment),
                LinkedEntityId = bra.Id,
                UpdatedBy = "tester@example.test"
            });
            await db.SaveChangesAsync();
            db.ComplianceApprovals.Add(new ComplianceApproval
            {
                TargetEntityType = nameof(RmcpVersion),
                TargetEntityId = rmcp.Id,
                Decision = "Approved",
                Approver = "ki@example.test",
                DecidedAtUtc = DateTime.UtcNow.AddDays(-1),
                Reason = "Approved for transfer test."
            });
            await db.SaveChangesAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(sourceFiles.Resolve(piPath))!);
            await File.WriteAllTextAsync(sourceFiles.Resolve(piPath), "Synthetic PI evidence shared with live");
            policy.Documents.Add(new() { Title = "Synthetic quotation", RelativePath = piPath,
                Sha256 = await sourceFiles.FingerprintAsync(piPath) });
            db.InsurancePolicies.Add(policy);
            await db.SaveChangesAsync();
            const string passphrase = "programme-transfer-test";
            var exported = await transfers.ExportAsync(passphrase, "exporter@example.test",
                "Transfer signed programme bundle.");
            outgoingPath = exported.StoragePath;
            Assert.Matches(@"^KCAS-programme-2026-[a-f0-9]{12}\.kcas-programme$", exported.FileName);
            Assert.True(exported.ControlledDocumentCount >= 1);
            Assert.True(exported.EvidenceCount >= 1);

            var encrypted = await File.ReadAllBytesAsync(exported.StoragePath);
            var preview = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(preview.CanApply);
            Assert.Equal(ComplianceProgrammeTransferService.DefaultLiveSignedFinalRoot, preview.MappedLiveRoot);
            Assert.Contains(preview.Package.ControlledDocuments, item => item.Title == $"Signed RMCP {suffix}");
            Assert.Contains(preview.Package.Evidence, item => item.Title == $"Signed evidence {suffix}");

            db.ComplianceApprovals.RemoveRange(await db.ComplianceApprovals
                .Where(item => item.TargetEntityType == nameof(RmcpVersion) && item.TargetEntityId == rmcp.Id)
                .ToListAsync());
            db.ComplianceEvidence.RemoveRange(await db.ComplianceEvidence
                .Where(item => item.Title == $"Signed evidence {suffix}")
                .ToListAsync());
            db.ControlledDocuments.RemoveRange(await db.ControlledDocuments
                .Where(item => item.Title == $"Signed RMCP {suffix}")
                .ToListAsync());
            db.RmcpVersions.Remove(rmcp);
            db.BusinessRiskAssessments.Remove(bra);
            db.InsurancePolicies.Remove(policy);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            transfers = Transfer(liveFiles);
            var missing = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.False(missing.CanApply);
            Assert.Contains(missing.Conflicts, x => x.Contains("PI policy"));
            Directory.CreateDirectory(Path.GetDirectoryName(liveFiles.Resolve(piPath))!);
            await File.WriteAllTextAsync(liveFiles.Resolve(piPath), "Incorrect live copy");
            Assert.False((await transfers.PreviewAsync(encrypted, passphrase)).CanApply);
            File.Copy(sourceFiles.Resolve(piPath), liveFiles.Resolve(piPath), true);
            Assert.True((await transfers.PreviewAsync(encrypted, passphrase)).CanApply);
            var imported = await transfers.ApplyAsync(encrypted, passphrase, "importer@example.test",
                "Approved programme import.");
            incomingPath = imported.StoragePath;
            Assert.Equal(exported.ControlledDocumentCount, imported.ControlledDocumentCount);
            Assert.Equal(exported.EvidenceCount, imported.EvidenceCount);

            var liveRmcp = await db.RmcpVersions.AsNoTracking()
                .SingleAsync(item => item.VersionReference == rmcp.VersionReference);
            Assert.StartsWith(ComplianceProgrammeTransferService.DefaultLiveSignedFinalRoot,
                liveRmcp.SignedDocumentLocation, StringComparison.OrdinalIgnoreCase);
            var liveDocument = await db.ControlledDocuments.AsNoTracking()
                .SingleAsync(item => item.Title == $"Signed RMCP {suffix}");
            Assert.StartsWith(ComplianceProgrammeTransferService.DefaultLiveSignedFinalRoot,
                liveDocument.Location, StringComparison.OrdinalIgnoreCase);
            var liveEvidence = await db.ComplianceEvidence.AsNoTracking()
                .SingleAsync(item => item.Title == $"Signed evidence {suffix}");
            Assert.StartsWith(ComplianceProgrammeTransferService.DefaultLiveSignedFinalRoot,
                liveEvidence.Location, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(imported.BusinessRiskAssessmentId, liveEvidence.LinkedEntityId);
            var livePolicy = await db.InsurancePolicies.AsNoTracking().Include(x => x.Documents).SingleAsync(x => x.TransferKey == policy.TransferKey);
            Assert.Equal(policy.Version, livePolicy.Version);
            Assert.Equal(piPath, Assert.Single(livePolicy.Documents).RelativePath);
            Assert.Equal(await sourceFiles.FingerprintAsync(piPath), await liveFiles.FingerprintAsync(livePolicy.Documents.Single().RelativePath));

            var duplicate = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(duplicate.AlreadyApplied);
            Assert.False(duplicate.CanApply);
            await Assert.ThrowsAsync<InvalidOperationException>(() => transfers.ApplyAsync(
                encrypted, passphrase, "importer@example.test", "Attempt duplicate import."));
        }
        finally
        {
            DeleteFile(outgoingPath);
            DeleteFile(incomingPath);
            db.ChangeTracker.Clear();
            await db.ComplianceTasks.Where(x => x.TaskType == ComplianceTaskTypes.InsuranceRenewal &&
                db.InsurancePolicies.Any(p => p.Id == x.InsurancePolicyId && p.TransferKey == policy.TransferKey)).ExecuteDeleteAsync();
            await db.InsurancePolicies.Where(x => x.TransferKey == policy.TransferKey).ExecuteDeleteAsync();
            if (Directory.Exists(fileRoot)) Directory.Delete(fileRoot, true);
        }
    }

    private static void DeleteFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }
}
