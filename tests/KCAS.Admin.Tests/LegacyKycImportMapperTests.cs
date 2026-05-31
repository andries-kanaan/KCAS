using System.Text.Json;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyKycImportMapperTests
{
    private static readonly IReadOnlyDictionary<int, string> MainClassNames = new Dictionary<int, string>
    {
        [2] = "Equities",
        [6] = "Other"
    };

    private static readonly IReadOnlyDictionary<int, string> SubClassNames = new Dictionary<int, string>
    {
        [4] = "Unit Trust (Volunt. Cont.)",
        [29] = "Life and Disability Cover"
    };

    [Fact]
    public void Map_preserves_life_cover_fields_flags_audit_and_snapshot()
    {
        var policy = LegacyKycImportMapper.Map(LifeCoverRow(), clientId: 10, MainClassNames, SubClassNames, new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(18, policy.LegacyKycId);
        Assert.Equal(10, policy.ClientId);
        Assert.Equal(10, policy.LegacyClientId);
        Assert.Equal("Other", policy.MainClassName);
        Assert.Equal("Life and Disability Cover", policy.SubClassName);
        Assert.Equal("Sanlam", policy.Administrator);
        Assert.Equal("40428014X1", policy.PolicyNumber);
        Assert.Equal(162053.09m, policy.Value);
        Assert.Equal(162053.09m, policy.LifeCover);
        Assert.Equal(50000m, policy.DisabilityCover);
        Assert.Equal(25000m, policy.DreadDiseaseCover);
        Assert.Equal(1000m, policy.CompulsoryContributionValue);
        Assert.Equal(2000m, policy.VoluntaryContributionValue);
        Assert.Equal(100m, policy.MonthlyPremium);
        Assert.Equal(100000m, policy.OnceOffPremium);
        Assert.True(policy.IncludeInCalculations);
        Assert.False(policy.IsQuote);
        Assert.Equal("legacy user", policy.OpenedBy);
        Assert.Equal(7, policy.LegacyOpenedByUserId);

        using var document = JsonDocument.Parse(policy.PayloadJson);
        Assert.Equal("Life Cover", document.RootElement.GetProperty("product").GetString());
        Assert.Equal("5.5", document.RootElement.GetProperty("taxp").GetString());
    }

    [Fact]
    public void Map_keeps_non_life_policy_value_without_life_cover_fields()
    {
        var row = AssetRow();

        var policy = LegacyKycImportMapper.Map(row, clientId: 20, MainClassNames, SubClassNames, DateTime.UtcNow);

        Assert.Equal("Equities", policy.MainClassName);
        Assert.Equal("Unit Trust (Volunt. Cont.)", policy.SubClassName);
        Assert.Equal(195125m, policy.Value);
        Assert.Null(policy.LifeCover);
        Assert.Null(policy.DisabilityCover);
        Assert.Equal(1000m, policy.Debt);
        Assert.Equal(250m, policy.MonthlyIncome);
        Assert.Equal(7.5m, policy.CapitalAdequacyRatioPercent);
        Assert.True(policy.IsRetirementAnnuity);
        Assert.False(policy.IsPreservationFund);
    }

    [Fact]
    public void Map_treats_blank_or_invalid_numbers_as_null_but_keeps_payload()
    {
        var row = AssetRow();
        row["value"] = "not money";
        row["premium"] = "";

        var policy = LegacyKycImportMapper.Map(row, clientId: 20, MainClassNames, SubClassNames, DateTime.UtcNow);

        Assert.Null(policy.Value);
        Assert.Null(policy.MonthlyPremium);

        using var document = JsonDocument.Parse(policy.PayloadJson);
        Assert.Equal("not money", document.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void ApplyUpdatedValues_replaces_imported_policy_fields()
    {
        var target = LegacyKycImportMapper.Map(AssetRow(), clientId: 20, MainClassNames, SubClassNames, DateTime.UtcNow);
        var row = AssetRow();
        row["administrator"] = "Updated admin";
        row["value"] = "200000";
        row["include"] = "0";
        var source = LegacyKycImportMapper.Map(row, clientId: 20, MainClassNames, SubClassNames, DateTime.UtcNow);

        LegacyKycImportMapper.ApplyUpdatedValues(target, source);

        Assert.Equal("Updated admin", target.Administrator);
        Assert.Equal(200000m, target.Value);
        Assert.False(target.IncludeInCalculations);
    }

    private static Dictionary<string, string?> LifeCoverRow()
    {
        var row = AssetRow();
        row["id"] = "18";
        row["main_class"] = "6";
        row["sub_class"] = "29";
        row["administrator"] = "Sanlam";
        row["product"] = "Life Cover";
        row["policy_no"] = "40428014X1";
        row["value"] = "162053.09";
        row["value02"] = "50000";
        row["value03"] = "25000";
        row["value04"] = "1000";
        row["value05"] = "2000";
        row["premium"] = "100";
        row["premium02"] = "100000";
        row["quote"] = "0";
        return row;
    }

    private static Dictionary<string, string?> AssetRow()
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "1",
            ["update_time"] = "2026-05-30 08:00:00",
            ["main_class"] = "2",
            ["sub_class"] = "4",
            ["sc_extra"] = "",
            ["administrator"] = "Investec",
            ["product"] = "Kanaan BCI Flexi FoF",
            ["policy_no"] = "261123",
            ["description"] = "Imported allocation",
            ["client_id"] = "10",
            ["kid"] = "KID-10",
            ["value"] = "195125",
            ["value02"] = "0",
            ["value03"] = "0",
            ["value04"] = "0",
            ["value05"] = "0",
            ["debt"] = "1000",
            ["premium"] = "50",
            ["premium02"] = "0",
            ["income"] = "250",
            ["car"] = "7,5%",
            ["include"] = "1",
            ["surrender"] = "0",
            ["ra"] = "1",
            ["pf"] = "0",
            ["rp"] = "0",
            ["quote"] = "0",
            ["fund"] = "Fund name",
            ["taxp"] = "5.5",
            ["opened_by"] = "legacy user",
            ["updated_by"] = "legacy updater",
            ["opened_by_id"] = "7",
            ["updated_by_id"] = "8",
            ["date_opened"] = "2026-05-30 09:00:00",
            ["date_updated"] = "2026-05-31 10:00:00"
        };
    }
}
