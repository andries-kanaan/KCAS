using System.Globalization;

namespace KCAS.Admin.Data;

public static class ClientFormOptions
{
    public static readonly string[] Titles = ["Mr", "Mrs", "Ms", "Miss", "Dr", "Prof", "Rev"];
    public static readonly string[] Languages = ["English", "Afrikaans", "Zulu", "Xhosa", "Sesotho", "Setswana", "Sepedi", "Tsonga", "Swati", "Venda", "Ndebele", "French", "German", "Portuguese", "Hindi", "Arabic"];
    public static readonly string[] Genders = ["Male", "Female"];
    public static readonly string[] MaritalStatuses = ["Single", "Married in community of property", "Married out of community of property with accrual", "Married out of community of property without accrual", "Married", "Divorced", "Widowed", "Separated", "Life partner"];
    public static readonly string[] Qualifications = ["Primary school", "Secondary school", "Matric", "Certificate", "Diploma", "Bachelor's degree", "Honours degree", "Master's degree", "Doctorate"];
    public static readonly string[] ContactTypes = ["Email", "Mobile", "HomePhone", "WorkPhone", "Fax", "Other"];
    public static readonly string[] AddressTypes = ["Physical", "Postal", "Work", "Other"];
    public static readonly string[] RelationshipTypes = ["Spouse", "Child", "Dependent", "FamilyContact", "Other"];
    public static readonly string[] Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name).EnglishName).Append("South Africa")
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
