using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyImportReconcilerTests
{
    [Fact]
    public void Compare_classifies_new_rows_without_a_baseline()
    {
        var row = LegacyImportReconciler.Compare(7, "tbl_client", 42, "{\"name\":\"New client\"}", null);

        Assert.Equal(LegacyImportClassifications.New, row.Classification);
        Assert.Equal(LegacyImportApplyStatuses.PendingReview, row.ApplyStatus);
        Assert.Empty(row.Differences);
    }

    [Fact]
    public void Compare_is_idempotent_for_equivalent_payloads()
    {
        var row = LegacyImportReconciler.Compare(
            7,
            "tbl_client",
            42,
            "{\"surname\":\" Van Tonder \",\"id\":\"42\"}",
            "{\"id\":\"42\",\"surname\":\"Van Tonder\"}");

        Assert.Equal(LegacyImportClassifications.Unchanged, row.Classification);
        Assert.Equal(row.BaselineFingerprint, row.IncomingFingerprint);
        Assert.Empty(row.Differences);
    }

    [Fact]
    public void Compare_stages_field_level_differences_without_resolving_them()
    {
        var row = LegacyImportReconciler.Compare(
            7,
            "tbl_client",
            42,
            "{\"id\":\"42\",\"email\":\"new@example.com\",\"mobile\":\"082\"}",
            "{\"id\":\"42\",\"email\":\"old@example.com\",\"mobile\":\"082\"}");

        Assert.Equal(LegacyImportClassifications.Changed, row.Classification);
        var difference = Assert.Single(row.Differences);
        Assert.Equal("email", difference.FieldName);
        Assert.Equal("old@example.com", difference.BaselineValue);
        Assert.Equal("new@example.com", difference.IncomingValue);
        Assert.Equal(LegacyImportDecisionStatuses.Pending, difference.Decision);
    }

    [Fact]
    public void Missing_rows_are_review_items_not_deletions()
    {
        var row = LegacyImportReconciler.Missing(7, "tbl_clientnote", 99, "{\"id\":\"99\"}");

        Assert.Equal(LegacyImportClassifications.MissingFromSource, row.Classification);
        Assert.Equal(LegacyImportApplyStatuses.PendingReview, row.ApplyStatus);
    }
}
