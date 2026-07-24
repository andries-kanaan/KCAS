using System.Globalization;
using System.Text.Json;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyClientImportMapper
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    public static Client Map(IReadOnlyDictionary<string, string?> row, DateTime importedAtUtc)
    {
        var legacyId = ReadInt(row, "id") ?? throw new InvalidOperationException("Legacy client row is missing a numeric id.");
        var surname = Read(row, "client_surname") ?? string.Empty;
        var fullName = Read(row, "client_full_name");
        var initials = Read(row, "initials");
        var displayName = BuildDisplayName(surname, fullName, initials);
        var category = ClientCategoryInference.InferFromLegacyClient(surname, fullName, displayName, Read(row, "client_folder"));

        var client = new Client
        {
            LegacyClientId = legacyId,
            KanaanId = Read(row, "kanaan_id"),
            Title = Read(row, "title"),
            Initials = initials,
            FullName = fullName,
            SurnameOrEntityName = surname,
            DisplayName = displayName,
            Language = Read(row, "language"),
            ClientFolder = Read(row, "client_folder"),
            ClientCategory = category.Category,
            ClientCategorySource = category.Source,
            ClientCategoryReason = category.Reason,
            ClientCategoryUpdatedAtUtc = importedAtUtc,
            CreatedAtUtc = ReadDateTime(row, "date_opened") ?? importedAtUtc,
            UpdatedAtUtc = ReadDateTime(row, "date_updated")
        };

        client.PersonalProfile = new ClientPersonalProfile
        {
            Client = client,
            SouthAfricanIdNumber = Read(row, "client_id_number"),
            Gender = Read(row, "gender"),
            MaritalStatus = Read(row, "marital_status"),
            TaxOffice = Read(row, "tax_office"),
            TaxNumber = Read(row, "tax_no"),
            IsTaxClient = ReadYesNo(row, "tax_client"),
            HighestQualification = Read(row, "highest_qualification"),
            Smoker = ReadYesNo(row, "smoke"),
            WorkdayTravelPercent = ReadDecimal(row, "traveling"),
            NumberOfDependents = ReadInt(row, "number_of_dependents"),
            FamilyDetailRaw = Read(row, "family_detail")
        };

        client.FinancialProfile = new ClientFinancialProfile
        {
            Client = client,
            Employer = Read(row, "client_employer"),
            Occupation = Read(row, "client_job"),
            GrossMonthlySalary = ReadDecimal(row, "basic_salary"),
            GrossAnnualSalary = ReadDecimal(row, "basic_salary_annual"),
            MonthlyExpenses = ReadDecimal(row, "monthly_expenses"),
            YearlyBonus = ReadDecimal(row, "yearly_bonus"),
            OtherIncome = ReadDecimal(row, "other_income"),
            RetirementAge = ReadInt(row, "retirement_age"),
            PensionFundName = Read(row, "pension_fund_name"),
            EmployerPensionContributionAmount = ReadDecimal(row, "employer_pension_contribution"),
            EmployerPensionContributionPercent = ReadDecimal(row, "employer_pension_contributionp"),
            CapitalRequirementPercent = ReadDecimal(row, "cap_req_p"),
            MinimumRetirementIncomePercent = ReadDecimal(row, "min_retirement_incomep"),
            ExpectedRetirementIncomePercent = ReadDecimal(row, "exp_retirement_incomep"),
            PreservationFundLumpSumPercent = ReadDecimal(row, "pf_p"),
            RetirementProvisionTax = ReadDecimal(row, "rp_tax"),
            PensionFundTax = ReadDecimal(row, "pf_tax"),
            RetirementAnnuityTax = ReadDecimal(row, "ras_tax"),
            RepresentativeName = Read(row, "name_of_broker"),
            RepresentativeEquitiesPercent = ReadDecimal(row, "drep"),
            RepresentativeAlternativeInvestmentsPercent = ReadDecimal(row, "drap"),
            RepresentativeFixedPropertyPercent = ReadDecimal(row, "drfp"),
            RepresentativeOffshorePercent = ReadDecimal(row, "drop"),
            BankDetailRaw = Read(row, "bank_detail"),
            WillDetailRaw = Read(row, "will_detail"),
            OtherGoalsRaw = Read(row, "other_goals"),
            OtherDetailsRaw = Read(row, "other_details")
        };

        AddContact(client, "Email", "Email 1", Read(row, "email01"), true, 10, "email01");
        AddContact(client, "Email", "Email 2", Read(row, "email02"), false, 20, "email02");
        AddContact(client, "Email", "Email 3", Read(row, "email03"), false, 30, "email03");
        AddContact(client, "Email", "Email 4", Read(row, "email04"), false, 40, "email04");
        AddContact(client, "Email", "Email 5", Read(row, "email05"), false, 50, "email05");
        AddContact(client, "Mobile", "Cell", Read(row, "cell"), true, 60, "cell");
        AddContact(client, "Mobile", "Alternative cell", Read(row, "alt_cell"), false, 70, "alt_cell");
        AddContact(client, "Phone", "Home", Read(row, "home_tel"), false, 80, "home_tel");
        AddContact(client, "Phone", "Work", Read(row, "work_tel"), false, 90, "work_tel");
        AddContact(client, "Fax", "Fax", Read(row, "fax"), false, 100, "fax");
        AddContact(client, "Phone", "Offshore telephone", Read(row, "offshore_tel_no"), false, 110, "offshore_tel_no");
        AddContact(client, "Mobile", "Offshore mobile", Read(row, "offshore_cell_no"), false, 120, "offshore_cell_no");
        AddContact(client, "Email", "Offshore email", Read(row, "offshore_email_address"), false, 130, "offshore_email_address");

        AddAddress(client, "Physical", Read(row, "physical_address"), 10, "physical_address");
        AddAddress(client, "Postal", Read(row, "postal_address"), 20, "postal_address");
        AddAddress(client, "Offshore", Read(row, "offshore_address"), 30, "offshore_address");

        AddSpouse(client, row);
        AddFamilyContact(client, row);

        client.LegacySnapshots.Add(new ClientLegacySnapshot
        {
            Client = client,
            SourceTable = "tbl_client",
            SourceId = legacyId,
            PayloadJson = JsonSerializer.Serialize(
                new SortedDictionary<string, string?>(row.ToDictionary(item => item.Key, item => item.Value), StringComparer.OrdinalIgnoreCase),
                SnapshotJsonOptions),
            ImportedAtUtc = importedAtUtc
        });

        return client;
    }

    public static void ApplyUpdatedGraph(Client target, Client source)
    {
        target.KanaanId = source.KanaanId;
        target.Title = source.Title;
        target.Initials = source.Initials;
        target.FullName = source.FullName;
        target.SurnameOrEntityName = source.SurnameOrEntityName;
        target.DisplayName = source.DisplayName;
        target.Language = source.Language;
        target.ClientFolder = source.ClientFolder;
        if (ClientCategoryInference.CanApplyInferredCategory(target))
        {
            target.ClientCategory = source.ClientCategory;
            target.ClientCategorySource = source.ClientCategorySource;
            target.ClientCategoryReason = source.ClientCategoryReason;
            target.ClientCategoryUpdatedAtUtc = DateTime.UtcNow;
            target.ClientCategoryUpdatedBy = source.ClientCategoryUpdatedBy;
        }
        target.IsActive = source.IsActive;
        target.UpdatedAtUtc = DateTime.UtcNow;

        ReplaceOneToOne(target, source);
        ReplaceCollection(target.ContactPoints, source.ContactPoints);
        ReplaceCollection(target.Addresses, source.Addresses);
        ReplaceCollection(target.Relationships, source.Relationships);
        ReplaceCollection(target.LegacySnapshots, source.LegacySnapshots);
    }

    private static void ReplaceOneToOne(Client target, Client source)
    {
        if (source.PersonalProfile is not null)
        {
            target.PersonalProfile ??= new ClientPersonalProfile { Client = target };
            target.PersonalProfile.SouthAfricanIdNumber = source.PersonalProfile.SouthAfricanIdNumber;
            target.PersonalProfile.Gender = source.PersonalProfile.Gender;
            target.PersonalProfile.MaritalStatus = source.PersonalProfile.MaritalStatus;
            target.PersonalProfile.TaxOffice = source.PersonalProfile.TaxOffice;
            target.PersonalProfile.TaxNumber = source.PersonalProfile.TaxNumber;
            target.PersonalProfile.IsTaxClient = source.PersonalProfile.IsTaxClient;
            target.PersonalProfile.HighestQualification = source.PersonalProfile.HighestQualification;
            target.PersonalProfile.Smoker = source.PersonalProfile.Smoker;
            target.PersonalProfile.WorkdayTravelPercent = source.PersonalProfile.WorkdayTravelPercent;
            target.PersonalProfile.NumberOfDependents = source.PersonalProfile.NumberOfDependents;
            target.PersonalProfile.FamilyDetailRaw = source.PersonalProfile.FamilyDetailRaw;
        }

        if (source.FinancialProfile is not null)
        {
            target.FinancialProfile ??= new ClientFinancialProfile { Client = target };
            target.FinancialProfile.Employer = source.FinancialProfile.Employer;
            target.FinancialProfile.Occupation = source.FinancialProfile.Occupation;
            target.FinancialProfile.GrossMonthlySalary = source.FinancialProfile.GrossMonthlySalary;
            target.FinancialProfile.GrossAnnualSalary = source.FinancialProfile.GrossAnnualSalary;
            target.FinancialProfile.MonthlyExpenses = source.FinancialProfile.MonthlyExpenses;
            target.FinancialProfile.YearlyBonus = source.FinancialProfile.YearlyBonus;
            target.FinancialProfile.OtherIncome = source.FinancialProfile.OtherIncome;
            target.FinancialProfile.RetirementAge = source.FinancialProfile.RetirementAge;
            target.FinancialProfile.PensionFundName = source.FinancialProfile.PensionFundName;
            target.FinancialProfile.EmployerPensionContributionAmount = source.FinancialProfile.EmployerPensionContributionAmount;
            target.FinancialProfile.EmployerPensionContributionPercent = source.FinancialProfile.EmployerPensionContributionPercent;
            target.FinancialProfile.CapitalRequirementPercent = source.FinancialProfile.CapitalRequirementPercent;
            target.FinancialProfile.MinimumRetirementIncomePercent = source.FinancialProfile.MinimumRetirementIncomePercent;
            target.FinancialProfile.ExpectedRetirementIncomePercent = source.FinancialProfile.ExpectedRetirementIncomePercent;
            target.FinancialProfile.PreservationFundLumpSumPercent = source.FinancialProfile.PreservationFundLumpSumPercent;
            target.FinancialProfile.RetirementProvisionTax = source.FinancialProfile.RetirementProvisionTax;
            target.FinancialProfile.PensionFundTax = source.FinancialProfile.PensionFundTax;
            target.FinancialProfile.RetirementAnnuityTax = source.FinancialProfile.RetirementAnnuityTax;
            target.FinancialProfile.RepresentativeName = source.FinancialProfile.RepresentativeName;
            target.FinancialProfile.RepresentativeEquitiesPercent = source.FinancialProfile.RepresentativeEquitiesPercent;
            target.FinancialProfile.RepresentativeAlternativeInvestmentsPercent = source.FinancialProfile.RepresentativeAlternativeInvestmentsPercent;
            target.FinancialProfile.RepresentativeFixedPropertyPercent = source.FinancialProfile.RepresentativeFixedPropertyPercent;
            target.FinancialProfile.RepresentativeOffshorePercent = source.FinancialProfile.RepresentativeOffshorePercent;
            target.FinancialProfile.BankDetailRaw = source.FinancialProfile.BankDetailRaw;
            target.FinancialProfile.WillDetailRaw = source.FinancialProfile.WillDetailRaw;
            target.FinancialProfile.OtherGoalsRaw = source.FinancialProfile.OtherGoalsRaw;
            target.FinancialProfile.OtherDetailsRaw = source.FinancialProfile.OtherDetailsRaw;
        }
    }

    private static void ReplaceCollection<T>(ICollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            if (item is ClientContactPoint contact)
            {
                contact.Client = null!;
            }
            else if (item is ClientAddress address)
            {
                address.Client = null!;
            }
            else if (item is ClientRelationship relationship)
            {
                relationship.Client = null!;
            }
            else if (item is ClientLegacySnapshot snapshot)
            {
                snapshot.Client = null!;
            }

            target.Add(item);
        }
    }

    private static string BuildDisplayName(string surname, string? fullName, string? initials)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(surname) && !string.IsNullOrWhiteSpace(initials))
        {
            return $"{surname}, {initials}";
        }

        return string.IsNullOrWhiteSpace(surname) ? "Unknown legacy client" : surname;
    }

    private static void AddContact(Client client, string type, string label, string? value, bool primary, int sortOrder, string sourceField)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        client.ContactPoints.Add(new ClientContactPoint
        {
            Client = client,
            ContactType = type,
            Label = label,
            Value = value,
            IsPrimary = primary,
            SortOrder = sortOrder,
            LegacySourceField = sourceField
        });
    }

    private static void AddAddress(Client client, string type, string? value, int sortOrder, string sourceField)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        client.Addresses.Add(new ClientAddress
        {
            Client = client,
            AddressType = type,
            LinesRaw = value,
            SortOrder = sortOrder,
            LegacySourceField = sourceField
        });
    }

    private static void AddSpouse(Client client, IReadOnlyDictionary<string, string?> row)
    {
        var name = Read(row, "spouse_name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        client.Relationships.Add(new ClientRelationship
        {
            Client = client,
            RelationshipType = "Spouse",
            LegacyRelatedClientId = ReadInt(row, "spouse_record"),
            Name = name,
            Initials = Read(row, "spouse_initials"),
            Gender = Read(row, "spouse_gender"),
            BirthDate = ReadDateTime(row, "spouse_birthdate"),
            SouthAfricanIdNumber = Read(row, "spouse_id_number"),
            Email = Read(row, "spouse_email"),
            HomePhone = Read(row, "spouse_home_tel"),
            WorkPhone = Read(row, "spouse_work_tel"),
            MobilePhone = Read(row, "spouse_cell"),
            Employer = Read(row, "spouse_employer"),
            Occupation = Read(row, "spouse_job"),
            HighestQualification = Read(row, "spouse_highest_qualification"),
            GrossMonthlySalary = ReadDecimal(row, "spouse_basic_salary"),
            GrossAnnualSalary = ReadDecimal(row, "spouse_basic_salary_annual"),
            YearlyBonus = ReadDecimal(row, "spouse_yearly_bonus"),
            OtherIncome = ReadDecimal(row, "spouse_other_income"),
            PensionFundName = Read(row, "spouse_pension_fund_name"),
            EmployerPensionContributionAmount = ReadDecimal(row, "spouse_employer_pension_contribution"),
            EmployerPensionContributionPercent = ReadDecimal(row, "spouse_employer_pension_contributionp")
        });
    }

    private static void AddFamilyContact(Client client, IReadOnlyDictionary<string, string?> row)
    {
        var name = Read(row, "family_contact_name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        client.Relationships.Add(new ClientRelationship
        {
            Client = client,
            RelationshipType = "FamilyContact",
            Name = name,
            Occupation = Read(row, "family_contact_rel"),
            Email = Read(row, "family_contact_email"),
            HomePhone = Read(row, "family_contact_home_tel"),
            WorkPhone = Read(row, "family_contact_work_tel"),
            MobilePhone = Read(row, "family_contact_cell")
        });
    }

    private static string? Read(IReadOnlyDictionary<string, string?> row, string name)
    {
        return row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static bool? ReadYesNo(IReadOnlyDictionary<string, string?> row, string name)
    {
        return Read(row, name)?.ToUpperInvariant() switch
        {
            "Y" or "YES" or "TRUE" or "1" => true,
            "N" or "NO" or "FALSE" or "0" => false,
            _ => null
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        if (value is null)
        {
            return null;
        }

        var normalized = value.Replace("R", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal);

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ReadDateTime(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }
}
