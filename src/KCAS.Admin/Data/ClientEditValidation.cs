using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace KCAS.Admin.Data;

public sealed record ClientEditError(object Owner, string Field, string Message);

public static class ClientEditValidation
{
    public static List<ClientEditError> Validate(ClientEditModel model, ClientEditModel? original = null)
    {
        var errors = new List<ClientEditError>();
        if (string.IsNullOrWhiteSpace(model.SurnameOrEntityName))
            errors.Add(new(model, nameof(model.SurnameOrEntityName), "Enter a surname or entity name."));
        Lengths(model, typeof(Client), errors);
        Lengths(model, typeof(ClientPersonalProfile), errors);
        Lengths(model, typeof(ClientFinancialProfile), errors);
        Id(model, model.SouthAfricanIdNumber, original?.SouthAfricanIdNumber, errors);
        if (!string.IsNullOrWhiteSpace(model.PassportNumber) && string.IsNullOrWhiteSpace(model.PassportCountry))
            errors.Add(new(model, nameof(model.PassportCountry), "Select the passport's issuing country."));
        Number(model, nameof(model.NumberOfDependents), model.NumberOfDependents, 0, 100, errors);
        Number(model, nameof(model.WorkdayTravelPercent), model.WorkdayTravelPercent, 0, 100, errors);
        Number(model, nameof(model.RetirementAge), model.RetirementAge, 0, 120, errors);
        Number(model, nameof(model.GrossMonthlySalary), model.GrossMonthlySalary, 0, decimal.MaxValue, errors);
        Number(model, nameof(model.MonthlyExpenses), model.MonthlyExpenses, 0, decimal.MaxValue, errors);
        foreach (var contact in model.ContactPoints)
        {
            Lengths(contact, typeof(ClientContactPoint), errors);
            if (string.Equals(contact.ContactType, "Email", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(contact.Value) && !new EmailAddressAttribute().IsValid(contact.Value.Trim()) &&
                original?.ContactPoints.Any(old => old.ContactType == contact.ContactType && old.Value == contact.Value) != true)
                errors.Add(new(contact, nameof(contact.Value), "Enter a valid email address."));
        }
        foreach (var address in model.Addresses) Lengths(address, typeof(ClientAddress), errors);
        foreach (var relationship in model.Relationships)
        {
            Lengths(relationship, typeof(ClientRelationship), errors);
            var old = original?.Relationships.FirstOrDefault(row => row.Id == relationship.Id && row.Id.HasValue);
            Id(relationship, relationship.SouthAfricanIdNumber, old?.SouthAfricanIdNumber, errors);
            if (!string.IsNullOrWhiteSpace(relationship.Email) && !new EmailAddressAttribute().IsValid(relationship.Email.Trim()) &&
                old?.Email != relationship.Email)
                errors.Add(new(relationship, nameof(relationship.Email), "Enter a valid email address."));
            if (relationship.BirthDate?.Date > DateTime.Today)
                errors.Add(new(relationship, nameof(relationship.BirthDate), "Birth date cannot be in the future."));
            foreach (var field in new[] { nameof(relationship.GrossMonthlySalary), nameof(relationship.GrossAnnualSalary),
                nameof(relationship.YearlyBonus), nameof(relationship.OtherIncome), nameof(relationship.EmployerPensionContributionAmount) })
                Number(relationship, field, (decimal?)typeof(ClientRelationshipEditModel).GetProperty(field)!.GetValue(relationship), 0, decimal.MaxValue, errors);
            Number(relationship, nameof(relationship.EmployerPensionContributionPercent), relationship.EmployerPensionContributionPercent, 0, 100, errors);
        }
        return errors.DistinctBy(error => (error.Owner, error.Field, error.Message)).ToList();
    }

    public static bool IsValidSouthAfricanId(string? value)
    {
        var id = value?.Trim();
        if (id is null || id.Length != 13 || id.Any(character => character < '0' || character > '9') || id[10] is not ('0' or '1'))
            return false;
        // Validate the encoded calendar date without guessing the holder's century.
        var month = int.Parse(id.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(id.AsSpan(4, 2), CultureInfo.InvariantCulture);
        var year = 2000 + int.Parse(id.AsSpan(0, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        var sum = 0;
        for (var index = 0; index < id.Length; index++)
        {
            var digit = id[index] - '0';
            if (index % 2 == 1) digit = digit * 2 > 9 ? digit * 2 - 9 : digit * 2;
            sum += digit;
        }
        return sum % 10 == 0;
    }

    private static void Id(object owner, string? value, string? original, List<ClientEditError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && value?.Trim() != original?.Trim() && !IsValidSouthAfricanId(value))
            errors.Add(new(owner, "SouthAfricanIdNumber", "Enter a valid 13-digit South African ID number (date, citizenship digit and checksum)."));
    }

    private static void Number(object owner, string field, decimal? value, decimal minimum, decimal maximum, List<ClientEditError> errors)
    {
        if (value is not null && (value < minimum || value > maximum))
            errors.Add(new(owner, field, maximum == decimal.MaxValue ? "Enter a non-negative amount." : $"Enter a value between {minimum} and {maximum}."));
    }

    private static void Lengths(object owner, Type entityType, List<ClientEditError> errors)
    {
        foreach (var property in entityType.GetProperties())
        {
            var limit = property.GetCustomAttributes(typeof(MaxLengthAttribute), true).Cast<MaxLengthAttribute>().FirstOrDefault();
            if (limit is null || owner.GetType().GetProperty(property.Name)?.GetValue(owner) is not string text) continue;
            if (text.Trim().Length > limit.Length)
                errors.Add(new(owner, property.Name, $"{property.Name}: use at most {limit.Length} characters."));
        }
    }
}
