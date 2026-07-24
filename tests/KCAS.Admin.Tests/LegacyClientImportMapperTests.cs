using System.Text.Json;
using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyClientImportMapperTests
{
    [Fact]
    public void Map_splits_legacy_client_into_normalized_profile()
    {
        var client = LegacyClientImportMapper.Map(SampleRow(), new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(42, client.LegacyClientId);
        Assert.Equal("007", client.KanaanId);
        Assert.Equal("Van Vuuren, A", client.DisplayName);
        Assert.Equal("Van Vuuren", client.SurnameOrEntityName);
        Assert.Equal(ClientCategories.NaturalPerson, client.ClientCategory);
        Assert.Equal(ClientCategorySources.LegacyImportInferred, client.ClientCategorySource);
        Assert.Equal("Afrikaans", client.Language);
        Assert.Equal("8001015009087", client.PersonalProfile?.SouthAfricanIdNumber);
        Assert.True(client.PersonalProfile?.IsTaxClient);
        Assert.Equal("Degree", client.PersonalProfile?.HighestQualification);
        Assert.Equal("Children detail", client.PersonalProfile?.FamilyDetailRaw);
        Assert.Equal(25000m, client.FinancialProfile?.GrossMonthlySalary);
        Assert.Equal(7.5m, client.FinancialProfile?.CapitalRequirementPercent);
        Assert.Equal("Broker One", client.FinancialProfile?.RepresentativeName);
        Assert.Equal("Goals raw", client.FinancialProfile?.OtherGoalsRaw);
        Assert.Equal("Will raw", client.FinancialProfile?.WillDetailRaw);
    }

    [Fact]
    public void Map_normalizes_contacts_addresses_relationships_and_keeps_snapshot()
    {
        var client = LegacyClientImportMapper.Map(SampleRow(), DateTime.UtcNow);

        Assert.Contains(client.ContactPoints, contact => contact.ContactType == "Email" && contact.Value == "primary@example.test" && contact.IsPrimary);
        Assert.Contains(client.ContactPoints, contact => contact.ContactType == "Mobile" && contact.Value == "0821234567" && contact.IsPrimary);
        Assert.Contains(client.Addresses, address => address.AddressType == "Physical" && address.LinesRaw.Contains("Main Street"));
        Assert.Contains(client.Relationships, relationship => relationship.RelationshipType == "Spouse" && relationship.Name == "B Van Vuuren");
        Assert.Contains(client.Relationships, relationship => relationship.RelationshipType == "Spouse" && relationship.GrossMonthlySalary == 20000m);
        Assert.Contains(client.Relationships, relationship => relationship.RelationshipType == "FamilyContact" && relationship.Name == "Emergency Person");

        var snapshot = Assert.Single(client.LegacySnapshots);
        using var document = JsonDocument.Parse(snapshot.PayloadJson);
        Assert.Equal("Van Vuuren", document.RootElement.GetProperty("client_surname").GetString());
    }

    [Fact]
    public void Map_treats_empty_strings_as_null_and_keeps_invalid_money_out_of_typed_fields()
    {
        var row = SampleRow();
        row["client_full_name"] = "";
        row["basic_salary"] = "not money";
        row["email01"] = " ";

        var client = LegacyClientImportMapper.Map(row, DateTime.UtcNow);

        Assert.Null(client.FullName);
        Assert.Null(client.FinancialProfile?.GrossMonthlySalary);
        Assert.DoesNotContain(client.ContactPoints, contact => contact.LegacySourceField == "email01");
    }

    [Theory]
    [InlineData("An-Mar Trust", "An-Mar Trust", @"z:\Kanaan Trust\Clients\Clients\An-Mar Trust", ClientCategories.Trust)]
    [InlineData("KSB Durban Trading CC", "KSB Durban Trading CC", @"z:\Kanaan Trust\Clients\Clients\KSB Durban Trading CC", ClientCategories.LegalPerson)]
    [InlineData("Darke", "Alwyn Louis", @"z:\Kanaan Trust\Clients\Clients\Darke AL Estate Late", ClientCategories.Other)]
    public void Map_infers_client_category_from_legacy_name_and_final_folder_segment(string surname, string? fullName, string folder, string expectedCategory)
    {
        var row = SampleRow();
        row["client_surname"] = surname;
        row["client_full_name"] = fullName;
        row["client_folder"] = folder;

        var client = LegacyClientImportMapper.Map(row, DateTime.UtcNow);

        Assert.Equal(expectedCategory, client.ClientCategory);
        Assert.Equal(ClientCategorySources.LegacyImportInferred, client.ClientCategorySource);
        Assert.False(string.IsNullOrWhiteSpace(client.ClientCategoryReason));
    }

    [Fact]
    public void ApplyUpdatedGraph_refreshes_inferred_category_but_preserves_manual_category()
    {
        var sourceRow = SampleRow();
        sourceRow["client_surname"] = "An-Mar Trust";
        sourceRow["client_full_name"] = "An-Mar Trust";
        var source = LegacyClientImportMapper.Map(sourceRow, DateTime.UtcNow);
        var inferredTarget = LegacyClientImportMapper.Map(SampleRow(), DateTime.UtcNow);
        var manualTarget = LegacyClientImportMapper.Map(SampleRow(), DateTime.UtcNow);
        manualTarget.ClientCategory = ClientCategories.LegalPerson;
        manualTarget.ClientCategorySource = ClientCategorySources.Manual;
        manualTarget.ClientCategoryReason = "Manually corrected.";

        LegacyClientImportMapper.ApplyUpdatedGraph(inferredTarget, source);
        LegacyClientImportMapper.ApplyUpdatedGraph(manualTarget, source);

        Assert.Equal(ClientCategories.Trust, inferredTarget.ClientCategory);
        Assert.Equal(ClientCategorySources.LegacyImportInferred, inferredTarget.ClientCategorySource);
        Assert.Equal(ClientCategories.LegalPerson, manualTarget.ClientCategory);
        Assert.Equal(ClientCategorySources.Manual, manualTarget.ClientCategorySource);
    }

    private static Dictionary<string, string?> SampleRow()
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "42",
            ["kanaan_id"] = "007",
            ["title"] = "Mr",
            ["initials"] = "A",
            ["client_surname"] = "Van Vuuren",
            ["client_full_name"] = "",
            ["language"] = "Afrikaans",
            ["client_folder"] = @"z:\Kanaan Trust\Clients\Van Vuuren A",
            ["date_opened"] = "2026-05-01",
            ["date_updated"] = "2026-05-02",
            ["client_id_number"] = "8001015009087",
            ["gender"] = "M",
            ["marital_status"] = "Married",
            ["tax_office"] = "Pretoria",
            ["tax_no"] = "1234567890",
            ["tax_client"] = "Y",
            ["highest_qualification"] = "Degree",
            ["smoke"] = "N",
            ["traveling"] = "10",
            ["number_of_dependents"] = "2",
            ["client_employer"] = "Kanaan",
            ["client_job"] = "Advisor",
            ["basic_salary"] = "25000",
            ["basic_salary_annual"] = "300000",
            ["monthly_expenses"] = "12000",
            ["yearly_bonus"] = "10000",
            ["other_income"] = "2500",
            ["retirement_age"] = "65",
            ["pension_fund_name"] = "Kanaan PF",
            ["employer_pension_contribution"] = "1500",
            ["employer_pension_contributionp"] = "5",
            ["cap_req_p"] = "7,5%",
            ["min_retirement_incomep"] = "60",
            ["exp_retirement_incomep"] = "70",
            ["pf_p"] = "20",
            ["rp_tax"] = "100",
            ["pf_tax"] = "200",
            ["ras_tax"] = "300",
            ["name_of_broker"] = "Broker One",
            ["drep"] = "25",
            ["drap"] = "25",
            ["drfp"] = "25",
            ["drop"] = "25",
            ["bank_detail"] = "Bank raw",
            ["will_detail"] = "Will raw",
            ["other_goals"] = "Goals raw",
            ["other_details"] = "Details raw",
            ["email01"] = "primary@example.test",
            ["email02"] = "secondary@example.test",
            ["email03"] = "",
            ["email04"] = "",
            ["email05"] = "",
            ["cell"] = "0821234567",
            ["alt_cell"] = "0831234567",
            ["home_tel"] = "0121234567",
            ["work_tel"] = "0111234567",
            ["fax"] = "0127654321",
            ["offshore_tel_no"] = "",
            ["offshore_cell_no"] = "",
            ["offshore_email_address"] = "",
            ["physical_address"] = "1 Main Street",
            ["postal_address"] = "PO Box 1",
            ["offshore_address"] = "",
            ["spouse_record"] = "43",
            ["spouse_name"] = "B Van Vuuren",
            ["spouse_initials"] = "B",
            ["spouse_gender"] = "F",
            ["spouse_birthdate"] = "1982-02-02",
            ["spouse_id_number"] = "8202020000000",
            ["spouse_email"] = "spouse@example.test",
            ["spouse_home_tel"] = "0120000000",
            ["spouse_work_tel"] = "0110000000",
            ["spouse_cell"] = "0841234567",
            ["spouse_employer"] = "School",
            ["spouse_job"] = "Teacher",
            ["spouse_basic_salary"] = "20000",
            ["spouse_basic_salary_annual"] = "240000",
            ["spouse_yearly_bonus"] = "8000",
            ["spouse_other_income"] = "1200",
            ["spouse_pension_fund_name"] = "Spouse PF",
            ["spouse_employer_pension_contribution"] = "1000",
            ["spouse_employer_pension_contributionp"] = "4",
            ["spouse_highest_qualification"] = "Diploma",
            ["family_detail"] = "Children detail",
            ["family_contact_name"] = "Emergency Person",
            ["family_contact_rel"] = "Sibling",
            ["family_contact_email"] = "family@example.test",
            ["family_contact_home_tel"] = "0122222222",
            ["family_contact_work_tel"] = "0112222222",
            ["family_contact_cell"] = "0851234567"
        };
    }
}
