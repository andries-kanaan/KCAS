using KCAS.Admin.LegacyImport;
using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class LegacyImportStagingLifecycleTests
{
    [Fact]
    public void Inactive_databases_keeps_only_the_active_validated_snapshot()
    {
        var inactive = LegacyImportStagingLifecycle.InactiveDatabases(
            [
                "kcas_blazor",
                "kcas_legacy_stage_aaaaaaaaaaaa",
                "kcas_legacy_stage_bbbbbbbbbbbb",
                "kcas_legacy_stage_not-a-hash"
            ],
            "kcas_legacy_stage_bbbbbbbbbbbb");

        Assert.Equal(["kcas_legacy_stage_aaaaaaaaaaaa"], inactive);
    }

    [Fact]
    public void Inactive_databases_rejects_an_unsafe_active_database_name()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LegacyImportStagingLifecycle.InactiveDatabases([], "kcas_blazor"));
    }

    [Theory]
    [InlineData(LegacyImportRunStatuses.Completed, true)]
    [InlineData(LegacyImportRunStatuses.AwaitingReview, true)]
    [InlineData(LegacyImportRunStatuses.Failed, false)]
    [InlineData(LegacyImportRunStatuses.Scanning, false)]
    [InlineData(LegacyImportRunStatuses.Superseded, false)]
    public void Only_successful_scan_runs_can_become_active(string status, bool expected)
    {
        var run = new LegacyImportRun { Mode = LegacyImportModes.Scan, Status = status };

        Assert.Equal(expected, LegacyImportStagingLifecycle.CanActivate(run));
    }
}
