using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class PerformanceFeePolicyTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public void Shared_policy_preserves_individual_terms_and_never_confirms_a_proposed_convention()
    {
        var input = PerformanceFeeCalculatorTests.Input(); input.UseSharedPolicy = true; input.VatPercent = null;
        var policy = new FeeSharedPolicy { Version = "Synthetic-v1", VatReference = "Synthetic VAT evidence", MethodReference = "Synthetic convention rationale" };
        PerformanceFeeService.ApplySharedPolicy(input, policy);
        Assert.False(input.MethodMatchesMandate); Assert.Equal(15m, input.VatPercent); Assert.Equal(15m, input.FeePercent);
        var result = PerformanceFeeCalculator.Calculate(input, PerformanceFeeCalculatorTests.Start.AddYears(5));
        Assert.True(result.HasCalculation); Assert.Equal(2242.50m, result.Total); Assert.False(result.IsReady);
        Assert.DoesNotContain(result.Issues, x => x.Contains("VAT applies"));
        policy.ApprovedBy = "Synthetic KI"; policy.ApprovedAtUtc = DateTime.UtcNow;
        PerformanceFeeService.ApplySharedPolicy(input, policy);
        Assert.True(PerformanceFeeCalculator.Calculate(input, PerformanceFeeCalculatorTests.Start.AddYears(5)).IsReady);
        input.FeePercent = 20;
        PerformanceFeeService.ApplySharedPolicy(input, policy);
        Assert.Equal(20m, input.FeePercent); Assert.False(input.MethodMatchesMandate);
        Assert.Contains(input.ReviewIssues, x => x.Contains("outside"));
    }

    [Fact]
    public void Disclosure_review_blocks_approval_not_supported_arithmetic_and_zero_fee_does_not_bypass_it()
    {
        var input = PerformanceFeeCalculatorTests.Input();
        input.ReviewPoints.Add(new() { Key = "quote", Detail = "Synthetic quote differs from signed fee mandate" });
        var result = PerformanceFeeCalculator.Calculate(input, PerformanceFeeCalculatorTests.Start.AddYears(5));
        Assert.True(result.HasCalculation); Assert.Equal(1950m, result.FeeExVat); Assert.False(result.IsReady); Assert.Null(result.NextCheckpoint);
        var zero = PerformanceFeeZeroRateTests.ZeroInput(); zero.ReviewPoints = input.ReviewPoints;
        Assert.False(PerformanceFeeCalculator.Calculate(zero, PerformanceFeeCalculatorTests.Start.AddYears(5)).IsReady);
        input.ReviewPoints[0].ResolvedBy = "Synthetic KI";
        Assert.False(PerformanceFeeCalculator.Calculate(input, PerformanceFeeCalculatorTests.Start.AddYears(5)).IsReady);
        input.ReviewPoints[0].ResolvedAtUtc = DateTime.UtcNow; input.ReviewPoints[0].ResolutionReference = "Synthetic signed clarification";
        Assert.True(PerformanceFeeCalculator.Calculate(input, PerformanceFeeCalculatorTests.Start.AddYears(5)).IsReady);
    }

    [Fact]
    public async Task Convention_adoption_is_KI_only_VAT_is_evidenced_and_disclosure_resolution_cannot_be_bypassed()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fees = scope.ServiceProvider.GetRequiredService<PerformanceFeeService>();
        var compliance = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var ki = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var reader = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Reports);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic convention KI", Email = ki.Identity!.Name, IsActive = true };
        var client = new Client { DisplayName = "Synthetic convention " + Guid.NewGuid().ToString("N"), SurnameOrEntityName = "Synthetic" };
        var account = new ClientInvestmentAccount { Client = client, AccountNumber = "POLICY-" + Guid.NewGuid().ToString("N"), InvestmentDate = PerformanceFeeCalculatorTests.Start };
        db.GovernanceRoleAssignments.Add(assignment); db.ClientInvestmentAccounts.Add(account); await db.SaveChangesAsync();
        var id = 0; var policyIds = new List<int>();
        try
        {
            Assert.Null(await fees.SharedPolicyAsync(compliance));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fees.SaveSharedPolicyAsync(null, "Synthetic rationale", 15, "Synthetic VAT", "Synthetic proposal", reader));
            await fees.SaveSharedPolicyAsync(null, "Synthetic mandate and industry-principle comparison", 15, "Synthetic signed AFS and SARS standard-rated fee basis", "Synthetic proposal", compliance);
            var policy = (await fees.SharedPolicyAsync(compliance))!;
            policyIds = await db.ComplianceReferenceValues.Where(x => x.Category == "PerformanceFeePolicy" && x.Code == "SA-wrap-convention").Select(x => x.Id).ToListAsync();
            var generic = scope.ServiceProvider.GetRequiredService<ComplianceService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => generic.SaveReferenceValueAsync(new() { Category = "PerformanceFeePolicy", Code = "forged", Name = "Cannot forge KI adoption" }, compliance.Identity!.Name, "Synthetic bypass check"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => generic.SaveReferenceValueAsync(new() { Id = policyIds.Single(), Category = "Other", Code = "rename", Name = "Cannot rename policy" }, compliance.Identity!.Name, "Synthetic bypass check"));
            Assert.Null(policy.ApprovedBy); Assert.Equal(compliance.Identity!.Name, policy.RecordedBy);
            Assert.Contains(await scope.ServiceProvider.GetRequiredService<UserActionService>().LoadAsync(ki), x => x.Key == "pdf-convention-adoption");
            Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<UserActionService>().LoadAsync(compliance), x => x.Key == "pdf-convention-adoption");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fees.ApproveSharedPolicyAsync(policy.Version, "Cannot impersonate KI", compliance));
            await Assert.ThrowsAsync<ValidationException>(() => fees.SaveSharedPolicyAsync("stale", "Synthetic rationale", 15, "Synthetic VAT", "Stale proposal", compliance));
            id = await fees.RegisterAsync(account.Id, compliance); var profile = await fees.LoadAsync(id, compliance);
            var input = PerformanceFeeCalculatorTests.Input(); input.MethodMatchesMandate = false; input.VatPercent = null;
            var claimId = await fees.PrepareAsync(id, profile.Version, input, "Synthetic proposed convention preparation", compliance);
            var workspace = await fees.ClientWorkspaceAsync(id, compliance);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fees.UseSharedPolicyForClientAsync(id, workspace.Version, "Cannot select shared settings", reader));
            await fees.UseSharedPolicyForClientAsync(id, workspace.Version, "Synthetic matching signed mandate confirmed", compliance);
            profile = await fees.LoadAsync(id, compliance);
            await fees.PrepareAsync(id, profile.Version, FeeJson.Read<FeePreparation>(profile.PreparationJson), "Refresh selected shared convention", compliance);
            profile = await fees.LoadAsync(id, compliance);
            Assert.False(FeeJson.Read<FeeCalculationResult>(profile.Claims.Single().ResultJson).IsReady);
            Assert.Equal(15m, FeeJson.Read<FeePreparation>(profile.PreparationJson).VatPercent);
            await fees.RecordFeeReviewPointAsync(id, profile.Version, "quote", "Synthetic zero fee quote conflicts with signed mandate", compliance);
            profile = await fees.LoadAsync(id, compliance);
            var tampered = FeeJson.Read<FeePreparation>(profile.PreparationJson); tampered.ReviewPoints.Clear();
            await Assert.ThrowsAsync<ValidationException>(() => fees.PrepareAsync(id, profile.Version, tampered, "Cannot erase discrepancy", compliance));
            await fees.ApproveSharedPolicyAsync(policy.Version, "Synthetic KI adopts the fully described method for matching mandates", ki);
            profile = await fees.LoadAsync(id, compliance);
            Assert.True(FeeJson.Read<FeePreparation>(profile.Claims.Single().InputJson).RequiresRebuild);
            await Assert.ThrowsAsync<ValidationException>(() => fees.ApproveAsync(id, claimId, profile.Version, "Stale proposed snapshot", ki));
            input = FeeJson.Read<FeePreparation>(profile.PreparationJson);
            await fees.PrepareAsync(id, profile.Version, input, "Refresh adopted convention, retaining discrepancy", compliance);
            profile = await fees.LoadAsync(id, compliance);
            var result = FeeJson.Read<FeeCalculationResult>(profile.Claims.Single().ResultJson);
            Assert.Single(result.Issues); Assert.Contains("disclosure", result.Issues.Single()); Assert.Equal(2242.50m, result.Total);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fees.ResolveFeeReviewPointAsync(id, profile.Version, "quote", "No KI authority", compliance));
            await fees.ResolveFeeReviewPointAsync(id, profile.Version, "quote", "Synthetic actual signed correction reconciles the quote", ki);
            profile = await fees.LoadAsync(id, ki);
            Assert.True(FeeJson.Read<FeeCalculationResult>(profile.Claims.Single().ResultJson).IsReady);
            await fees.ApproveAsync(id, claimId, profile.Version, "Synthetic independently supported claim approval", ki);
            var frozen = (await fees.LoadAsync(id, ki)).Claims.Single().InputJson;
            var adopted = (await fees.SharedPolicyAsync(compliance))!;
            await fees.SaveSharedPolicyAsync(adopted.Version, "Synthetic revised convention rationale", 15, "Synthetic updated evidence", "New proposed version", compliance);
            Assert.Equal(frozen, (await fees.LoadAsync(id, ki)).Claims.Single().InputJson);
            Assert.Equal(FeeClaimStages.Approved, (await fees.LoadAsync(id, ki)).Claims.Single().Status);
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.PerformanceFeeProfiles.Where(x => x.Id == id).ExecuteDeleteAsync();
            await db.ClientInvestmentAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync(); await db.Clients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
            await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync();
            await db.ComplianceAuditEvents.Where(x => x.EntityType == "PerformanceFeePolicy" && policyIds.Contains(x.EntityId)).ExecuteDeleteAsync();
            await db.ComplianceReferenceValues.Where(x => policyIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}
