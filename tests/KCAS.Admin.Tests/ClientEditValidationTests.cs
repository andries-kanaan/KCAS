using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class ClientEditValidationTests
{
    [Theory]
    [InlineData("8001015009087", true)]
    [InlineData("1001020000086", true)]
    [InlineData(" 8001015009087 ", true)]
    [InlineData("8001015009088", false)]
    [InlineData("8002315009087", false)]
    [InlineData("8001015009287", false)]
    [InlineData("80010150090A7", false)]
    [InlineData("800101500908", false)]
    [InlineData("８００１０１５００９０８７", false)]
    public void Sa_id_validation_checks_digits_date_citizenship_and_checksum(string value, bool expected) =>
        Assert.Equal(expected, ClientEditValidation.IsValidSouthAfricanId(value));

    [Fact]
    public void Unchanged_invalid_imported_id_is_preserved_but_a_changed_invalid_id_is_rejected()
    {
        var original = new ClientEditModel { Id = 1, SurnameOrEntityName = "Test", SouthAfricanIdNumber = "unknown" };
        var edited = new ClientEditModel { Id = 1, SurnameOrEntityName = "Updated", SouthAfricanIdNumber = "unknown" };
        Assert.Empty(ClientEditValidation.Validate(edited, original));
        edited.SouthAfricanIdNumber = "different";
        Assert.Contains(ClientEditValidation.Validate(edited, original), error => error.Field == nameof(edited.SouthAfricanIdNumber));
    }

    [Fact]
    public void Passport_only_and_optional_identity_are_supported_without_applying_sa_id_rules()
    {
        var model = new ClientEditModel { SurnameOrEntityName = "Test", PassportNumber = "AB-123456", PassportCountry = "United Kingdom" };
        Assert.Empty(ClientEditValidation.Validate(model));
        model.PassportCountry = null;
        Assert.Contains(ClientEditValidation.Validate(model), error => error.Field == nameof(model.PassportCountry));
        model.PassportNumber = null;
        Assert.Empty(ClientEditValidation.Validate(model));
    }

    [Fact]
    public void Validation_covers_nested_rows_lengths_amounts_percentages_and_required_name()
    {
        var contact = new ClientContactPointEditModel { ContactType = "Email", Value = "not an email" };
        var relationship = new ClientRelationshipEditModel { Name = "Test", SouthAfricanIdNumber = "123", EmployerPensionContributionPercent = 101, BirthDate = DateTime.Today.AddDays(1) };
        var model = new ClientEditModel { ContactPoints = [contact], Relationships = [relationship], NumberOfDependents = -1, WorkdayTravelPercent = 101,
            GrossMonthlySalary = -1, ClientFolder = new string('x', 513) };
        var errors = ClientEditValidation.Validate(model);
        Assert.Contains(errors, error => error.Owner == contact && error.Field == nameof(contact.Value));
        Assert.Contains(errors, error => error.Owner == relationship && error.Field == nameof(relationship.SouthAfricanIdNumber));
        Assert.Contains(errors, error => error.Owner == relationship && error.Field == nameof(relationship.EmployerPensionContributionPercent));
        Assert.Contains(errors, error => error.Owner == relationship && error.Field == nameof(relationship.BirthDate));
        foreach (var field in new[] { nameof(model.SurnameOrEntityName), nameof(model.NumberOfDependents), nameof(model.WorkdayTravelPercent), nameof(model.GrossMonthlySalary), nameof(model.ClientFolder) })
            Assert.Contains(errors, error => error.Owner == model && error.Field == field);
    }

    [Fact]
    public void Existing_choice_values_are_not_rewritten_or_rejected()
    {
        var model = new ClientEditModel { SurnameOrEntityName = "Test", Title = "Advocate", Language = "Dutch", Gender = "M", MaritalStatus = "Imported description" };
        Assert.Empty(ClientEditValidation.Validate(model));
        Assert.Equal("M", model.Gender);
        Assert.Equal("Dutch", model.Language);
        Assert.Equal(new[] { "Male", "Female" }, ClientFormOptions.Genders);
        Assert.Contains("South Africa", ClientFormOptions.Countries);
    }
}
