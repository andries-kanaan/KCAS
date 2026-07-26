using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyBaselineImportValidator
{
    public static void EnsureComplete(LegacyImportRun verificationRun)
    {
        if (verificationRun.Mode != LegacyImportModes.Scan ||
            verificationRun.CompletedAtUtc is null ||
            verificationRun.Status is not (LegacyImportRunStatuses.Completed or LegacyImportRunStatuses.AwaitingReview))
        {
            throw new InvalidOperationException("The baseline verification run did not complete successfully.");
        }

        if (verificationRun.NewCount > 0)
        {
            throw new InvalidOperationException(
                $"Baseline import verification found {verificationRun.NewCount} mapped source row(s) that were not imported.");
        }
    }
}
