using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyBaselineImportValidatorTests
{
    [Fact]
    public void Accepts_completed_verification_with_no_remaining_new_rows()
    {
        LegacyBaselineImportValidator.EnsureComplete(CompletedVerification(newCount: 0));
    }

    [Fact]
    public void Rejects_verification_with_unimported_mapped_rows()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            LegacyBaselineImportValidator.EnsureComplete(CompletedVerification(newCount: 2)));

        Assert.Contains("2 mapped source row(s) that were not imported", error.Message);
    }

    [Fact]
    public void Rejects_incomplete_verification()
    {
        var run = CompletedVerification(newCount: 0);
        run.CompletedAtUtc = null;

        Assert.Throws<InvalidOperationException>(() => LegacyBaselineImportValidator.EnsureComplete(run));
    }

    private static LegacyImportRun CompletedVerification(int newCount) => new()
    {
        Mode = LegacyImportModes.Scan,
        Status = LegacyImportRunStatuses.Completed,
        CompletedAtUtc = DateTime.UtcNow,
        NewCount = newCount
    };
}
