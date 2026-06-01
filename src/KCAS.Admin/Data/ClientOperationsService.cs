using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientOperationsService(ApplicationDbContext db, ClientCodeGenerator clientCodeGenerator)
{
    public async Task<ClientEditModel> LoadClientAsync(int? clientId)
    {
        if (clientId is null)
        {
            return new ClientEditModel();
        }

        var client = await LoadClientAggregateAsync(clientId.Value);
        return ClientEditModel.FromClient(client);
    }

    public async Task<int> SaveClientAsync(ClientEditModel model)
    {
        var surname = Normalize(model.SurnameOrEntityName);
        var displayName = Normalize(model.DisplayName);
        var kanaanId = Normalize(model.KanaanId);

        if (string.IsNullOrWhiteSpace(surname))
        {
            throw new ValidationException("Surname or entity name is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Normalize(model.FullName) ?? surname;
        }

        Client client;
        if (model.Id is null)
        {
            client = new Client();
            db.Clients.Add(client);
        }
        else
        {
            client = await LoadClientAggregateAsync(model.Id.Value);
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        client.KanaanId = kanaanId ?? await clientCodeGenerator.GenerateAsync();
        client.Title = Normalize(model.Title);
        client.Initials = Normalize(model.Initials);
        client.FullName = Normalize(model.FullName);
        client.SurnameOrEntityName = surname;
        client.DisplayName = displayName;
        client.Language = Normalize(model.Language);
        client.ClientFolder = Normalize(model.ClientFolder);
        client.IsActive = model.IsActive;

        client.PersonalProfile ??= new ClientPersonalProfile { Client = client };
        client.PersonalProfile.SouthAfricanIdNumber = Normalize(model.SouthAfricanIdNumber);
        client.PersonalProfile.Gender = Normalize(model.Gender);
        client.PersonalProfile.MaritalStatus = Normalize(model.MaritalStatus);
        client.PersonalProfile.TaxNumber = Normalize(model.TaxNumber);
        client.PersonalProfile.TaxOffice = Normalize(model.TaxOffice);
        client.PersonalProfile.HighestQualification = Normalize(model.HighestQualification);
        client.PersonalProfile.NumberOfDependents = model.NumberOfDependents;
        client.PersonalProfile.Smoker = model.Smoker;
        client.PersonalProfile.WorkdayTravelPercent = model.WorkdayTravelPercent;
        client.PersonalProfile.FamilyDetailRaw = Normalize(model.FamilyDetailRaw);

        client.FinancialProfile ??= new ClientFinancialProfile { Client = client };
        client.FinancialProfile.Employer = Normalize(model.Employer);
        client.FinancialProfile.Occupation = Normalize(model.Occupation);
        client.FinancialProfile.GrossMonthlySalary = model.GrossMonthlySalary;
        client.FinancialProfile.MonthlyExpenses = model.MonthlyExpenses;
        client.FinancialProfile.RetirementAge = model.RetirementAge;
        client.FinancialProfile.PensionFundName = Normalize(model.PensionFundName);
        client.FinancialProfile.RepresentativeName = Normalize(model.RepresentativeName);
        client.FinancialProfile.OtherGoalsRaw = Normalize(model.OtherGoalsRaw);
        client.FinancialProfile.BankDetailRaw = Normalize(model.BankDetailRaw);
        client.FinancialProfile.WillDetailRaw = Normalize(model.WillDetailRaw);
        client.FinancialProfile.OtherDetailsRaw = Normalize(model.OtherDetailsRaw);

        ReplaceContacts(client, model.ContactPoints);
        ReplaceAddresses(client, model.Addresses);
        ReplaceRelationships(client, model.Relationships);

        await db.SaveChangesAsync();
        return client.Id;
    }

    public async Task<ClientNoteEditModel> LoadNoteAsync(int clientId, int? noteId)
    {
        if (noteId is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == clientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            return new ClientNoteEditModel
            {
                ClientId = clientId,
                NoteDate = DateOnly.FromDateTime(DateTime.Today)
            };
        }

        var note = await db.ClientNotes
            .AsNoTracking()
            .SingleOrDefaultAsync(note => note.ClientId == clientId && note.Id == noteId.Value)
            ?? throw new InvalidOperationException("Note not found.");

        return ClientNoteEditModel.FromNote(note);
    }

    public async Task<int> SaveNoteAsync(ClientNoteEditModel model, string? userName)
    {
        var title = Normalize(model.Title);
        var details = Normalize(model.Details);

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(details))
        {
            throw new ValidationException("Enter a note title or details.");
        }

        ClientNote note;
        if (model.Id is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == model.ClientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            note = new ClientNote
            {
                ClientId = model.ClientId,
                IsFinal = false,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientNotes.Add(note);
        }
        else
        {
            note = await db.ClientNotes.SingleOrDefaultAsync(note => note.ClientId == model.ClientId && note.Id == model.Id.Value)
                ?? throw new InvalidOperationException("Note not found.");

            if (note.IsFinal)
            {
                throw new InvalidOperationException("Final notes cannot be edited.");
            }
        }

        note.NoteDate = model.NoteDate;
        note.Title = title;
        note.Details = details;
        note.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return note.Id;
    }

    public async Task FinalizeNoteAsync(int clientId, int noteId, string? userName)
    {
        var note = await db.ClientNotes.SingleOrDefaultAsync(note => note.ClientId == clientId && note.Id == noteId)
            ?? throw new InvalidOperationException("Note not found.");

        if (note.IsDeleted)
        {
            throw new InvalidOperationException("Deleted notes cannot be finalized.");
        }

        note.IsFinal = true;
        note.UpdatedBy = Normalize(userName);
        await db.SaveChangesAsync();
    }

    public async Task DeleteNoteAsync(int clientId, int noteId, string? userName)
    {
        var note = await db.ClientNotes.SingleOrDefaultAsync(note => note.ClientId == clientId && note.Id == noteId)
            ?? throw new InvalidOperationException("Note not found.");

        note.IsDeleted = true;
        note.UpdatedBy = Normalize(userName);
        await db.SaveChangesAsync();
    }

    public async Task<ClientKycPolicyEditModel> LoadKycPolicyAsync(int clientId, int? policyId)
    {
        if (policyId is null)
        {
            var clientExists = await db.Clients.AnyAsync(client => client.Id == clientId);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            return new ClientKycPolicyEditModel
            {
                ClientId = clientId,
                IncludeInCalculations = true
            };
        }

        var policy = await db.ClientKycPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.ClientId == clientId && policy.Id == policyId.Value)
            ?? throw new InvalidOperationException("KYC policy not found.");

        return ClientKycPolicyEditModel.FromPolicy(policy);
    }

    public async Task<int> SaveKycPolicyAsync(ClientKycPolicyEditModel model, string? userName)
    {
        var mainClassName = Normalize(model.MainClassName);
        var subClassName = Normalize(model.SubClassName);
        var administrator = Normalize(model.Administrator);
        var product = Normalize(model.Product);
        var policyNumber = Normalize(model.PolicyNumber);
        var description = Normalize(model.Description);
        var fund = Normalize(model.Fund);

        if (string.IsNullOrWhiteSpace(mainClassName) && string.IsNullOrWhiteSpace(subClassName))
        {
            throw new ValidationException("Enter a KYC main class or sub class.");
        }

        if (string.IsNullOrWhiteSpace(administrator) &&
            string.IsNullOrWhiteSpace(product) &&
            string.IsNullOrWhiteSpace(policyNumber) &&
            string.IsNullOrWhiteSpace(description) &&
            string.IsNullOrWhiteSpace(fund) &&
            model.Value is null &&
            model.LifeCover is null &&
            model.DisabilityCover is null &&
            model.DreadDiseaseCover is null &&
            model.CompulsoryContributionValue is null &&
            model.VoluntaryContributionValue is null &&
            model.Debt is null &&
            model.MonthlyPremium is null &&
            model.OnceOffPremium is null &&
            model.MonthlyIncome is null)
        {
            throw new ValidationException("Enter policy details or at least one financial amount.");
        }

        ClientKycPolicy policy;
        if (model.Id is null)
        {
            var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == model.ClientId)
                ?? throw new InvalidOperationException("Client not found.");

            policy = new ClientKycPolicy
            {
                ClientId = model.ClientId,
                KanaanId = client.KanaanId,
                PayloadJson = "{}",
                ImportedAtUtc = DateTime.UtcNow,
                OpenedBy = Normalize(userName)
            };
            db.ClientKycPolicies.Add(policy);
        }
        else
        {
            policy = await db.ClientKycPolicies.SingleOrDefaultAsync(policy => policy.ClientId == model.ClientId && policy.Id == model.Id.Value)
                ?? throw new InvalidOperationException("KYC policy not found.");

        }

        policy.MainClassName = mainClassName;
        policy.SubClassName = subClassName;
        policy.SubClassExtra = Normalize(model.SubClassExtra);
        policy.Administrator = administrator;
        policy.Product = product;
        policy.PolicyNumber = policyNumber;
        policy.Description = description;
        policy.Fund = fund;
        policy.Value = model.Value;
        policy.LifeCover = model.LifeCover;
        policy.DisabilityCover = model.DisabilityCover;
        policy.DreadDiseaseCover = model.DreadDiseaseCover;
        policy.CompulsoryContributionValue = model.CompulsoryContributionValue;
        policy.VoluntaryContributionValue = model.VoluntaryContributionValue;
        policy.Debt = model.Debt;
        policy.MonthlyPremium = model.MonthlyPremium;
        policy.OnceOffPremium = model.OnceOffPremium;
        policy.MonthlyIncome = model.MonthlyIncome;
        policy.CapitalAdequacyRatioPercent = model.CapitalAdequacyRatioPercent;
        policy.TaxPercent = model.TaxPercent;
        policy.IncludeInCalculations = model.IncludeInCalculations;
        policy.SurrenderOrLiquidate = model.SurrenderOrLiquidate;
        policy.IsRetirementAnnuity = model.IsRetirementAnnuity;
        policy.IsPreservationFund = model.IsPreservationFund;
        policy.IsRetrenchmentPackage = model.IsRetrenchmentPackage;
        policy.IsQuote = model.IsQuote;
        policy.ValuationDate = model.ValuationDate;
        policy.UpdatedBy = Normalize(userName);

        await db.SaveChangesAsync();
        return policy.Id;
    }

    public async Task DeleteKycPolicyAsync(int clientId, int policyId)
    {
        var policy = await db.ClientKycPolicies.SingleOrDefaultAsync(policy => policy.ClientId == clientId && policy.Id == policyId)
            ?? throw new InvalidOperationException("KYC policy not found.");

        db.ClientKycPolicies.Remove(policy);
        await db.SaveChangesAsync();
    }

    private async Task<Client> LoadClientAggregateAsync(int clientId)
    {
        return await db.Clients
            .Include(client => client.PersonalProfile)
            .Include(client => client.FinancialProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.Addresses)
            .Include(client => client.Relationships)
            .SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
    }

    private void ReplaceContacts(Client client, IEnumerable<ClientContactPointEditModel> contacts)
    {
        db.ClientContactPoints.RemoveRange(client.ContactPoints);
        client.ContactPoints.Clear();

        var rows = contacts
            .Select(contact => new ClientContactPointEditModel
            {
                ContactType = Normalize(contact.ContactType) ?? "Other",
                Label = Normalize(contact.Label),
                Value = Normalize(contact.Value) ?? "",
                IsPrimary = contact.IsPrimary
            })
            .Where(contact => !string.IsNullOrWhiteSpace(contact.Value))
            .ToList();

        foreach (var group in rows.GroupBy(contact => contact.ContactType, StringComparer.OrdinalIgnoreCase))
        {
            if (!group.Any(contact => contact.IsPrimary))
            {
                group.First().IsPrimary = true;
            }
        }

        var sortOrder = 0;
        foreach (var contact in rows)
        {
            client.ContactPoints.Add(new ClientContactPoint
            {
                ContactType = contact.ContactType,
                Label = contact.Label,
                Value = contact.Value ?? string.Empty,
                IsPrimary = contact.IsPrimary,
                SortOrder = sortOrder++
            });
        }
    }

    private void ReplaceAddresses(Client client, IEnumerable<ClientAddressEditModel> addresses)
    {
        db.ClientAddresses.RemoveRange(client.Addresses);
        client.Addresses.Clear();

        var sortOrder = 0;
        foreach (var address in addresses)
        {
            var lines = Normalize(address.LinesRaw);
            if (string.IsNullOrWhiteSpace(lines))
            {
                continue;
            }

            client.Addresses.Add(new ClientAddress
            {
                AddressType = Normalize(address.AddressType) ?? "Other",
                LinesRaw = lines,
                SortOrder = sortOrder++
            });
        }
    }

    private void ReplaceRelationships(Client client, IEnumerable<ClientRelationshipEditModel> relationships)
    {
        db.ClientRelationships.RemoveRange(client.Relationships);
        client.Relationships.Clear();

        foreach (var relationship in relationships)
        {
            var relationshipType = Normalize(relationship.RelationshipType);
            var name = Normalize(relationship.Name);
            var initials = Normalize(relationship.Initials);
            var gender = Normalize(relationship.Gender);
            var southAfricanIdNumber = Normalize(relationship.SouthAfricanIdNumber);
            var email = Normalize(relationship.Email);
            var homePhone = Normalize(relationship.HomePhone);
            var workPhone = Normalize(relationship.WorkPhone);
            var mobilePhone = Normalize(relationship.MobilePhone);
            var employer = Normalize(relationship.Employer);
            var occupation = Normalize(relationship.Occupation);
            var highestQualification = Normalize(relationship.HighestQualification);
            var pensionFundName = Normalize(relationship.PensionFundName);

            if (string.IsNullOrWhiteSpace(relationshipType) &&
                string.IsNullOrWhiteSpace(name) &&
                string.IsNullOrWhiteSpace(initials) &&
                string.IsNullOrWhiteSpace(gender) &&
                relationship.BirthDate is null &&
                string.IsNullOrWhiteSpace(southAfricanIdNumber) &&
                string.IsNullOrWhiteSpace(email) &&
                string.IsNullOrWhiteSpace(homePhone) &&
                string.IsNullOrWhiteSpace(workPhone) &&
                string.IsNullOrWhiteSpace(mobilePhone) &&
                string.IsNullOrWhiteSpace(employer) &&
                string.IsNullOrWhiteSpace(occupation) &&
                string.IsNullOrWhiteSpace(highestQualification) &&
                relationship.GrossMonthlySalary is null &&
                relationship.GrossAnnualSalary is null &&
                relationship.YearlyBonus is null &&
                relationship.OtherIncome is null &&
                string.IsNullOrWhiteSpace(pensionFundName) &&
                relationship.EmployerPensionContributionAmount is null &&
                relationship.EmployerPensionContributionPercent is null)
            {
                continue;
            }

            client.Relationships.Add(new ClientRelationship
            {
                RelationshipType = relationshipType ?? "Other",
                LegacyRelatedClientId = relationship.LegacyRelatedClientId,
                Name = name,
                Initials = initials,
                Gender = gender,
                BirthDate = relationship.BirthDate,
                SouthAfricanIdNumber = southAfricanIdNumber,
                Email = email,
                HomePhone = homePhone,
                WorkPhone = workPhone,
                MobilePhone = mobilePhone,
                Employer = employer,
                Occupation = occupation,
                HighestQualification = highestQualification,
                GrossMonthlySalary = relationship.GrossMonthlySalary,
                GrossAnnualSalary = relationship.GrossAnnualSalary,
                YearlyBonus = relationship.YearlyBonus,
                OtherIncome = relationship.OtherIncome,
                PensionFundName = pensionFundName,
                EmployerPensionContributionAmount = relationship.EmployerPensionContributionAmount,
                EmployerPensionContributionPercent = relationship.EmployerPensionContributionPercent
            });
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ClientEditModel
{
    public int? Id { get; set; }
    public string? KanaanId { get; set; }
    public string? Title { get; set; }
    public string? Initials { get; set; }
    public string? FullName { get; set; }
    public string? SurnameOrEntityName { get; set; }
    public string? DisplayName { get; set; }
    public string? Language { get; set; }
    public string? ClientFolder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SouthAfricanIdNumber { get; set; }
    public string? Gender { get; set; }
    public string? MaritalStatus { get; set; }
    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? HighestQualification { get; set; }
    public int? NumberOfDependents { get; set; }
    public bool? Smoker { get; set; }
    public decimal? WorkdayTravelPercent { get; set; }
    public string? FamilyDetailRaw { get; set; }
    public string? Employer { get; set; }
    public string? Occupation { get; set; }
    public decimal? GrossMonthlySalary { get; set; }
    public decimal? MonthlyExpenses { get; set; }
    public int? RetirementAge { get; set; }
    public string? PensionFundName { get; set; }
    public string? RepresentativeName { get; set; }
    public string? OtherGoalsRaw { get; set; }
    public string? BankDetailRaw { get; set; }
    public string? WillDetailRaw { get; set; }
    public string? OtherDetailsRaw { get; set; }
    public List<ClientContactPointEditModel> ContactPoints { get; set; } = [];
    public List<ClientAddressEditModel> Addresses { get; set; } = [];
    public List<ClientRelationshipEditModel> Relationships { get; set; } = [];

    public static ClientEditModel FromClient(Client client)
    {
        return new ClientEditModel
        {
            Id = client.Id,
            KanaanId = client.KanaanId,
            Title = client.Title,
            Initials = client.Initials,
            FullName = client.FullName,
            SurnameOrEntityName = client.SurnameOrEntityName,
            DisplayName = client.DisplayName,
            Language = client.Language,
            ClientFolder = client.ClientFolder,
            IsActive = client.IsActive,
            SouthAfricanIdNumber = client.PersonalProfile?.SouthAfricanIdNumber,
            Gender = client.PersonalProfile?.Gender,
            MaritalStatus = client.PersonalProfile?.MaritalStatus,
            TaxNumber = client.PersonalProfile?.TaxNumber,
            TaxOffice = client.PersonalProfile?.TaxOffice,
            HighestQualification = client.PersonalProfile?.HighestQualification,
            NumberOfDependents = client.PersonalProfile?.NumberOfDependents,
            Smoker = client.PersonalProfile?.Smoker,
            WorkdayTravelPercent = client.PersonalProfile?.WorkdayTravelPercent,
            FamilyDetailRaw = client.PersonalProfile?.FamilyDetailRaw,
            Employer = client.FinancialProfile?.Employer,
            Occupation = client.FinancialProfile?.Occupation,
            GrossMonthlySalary = client.FinancialProfile?.GrossMonthlySalary,
            MonthlyExpenses = client.FinancialProfile?.MonthlyExpenses,
            RetirementAge = client.FinancialProfile?.RetirementAge,
            PensionFundName = client.FinancialProfile?.PensionFundName,
            RepresentativeName = client.FinancialProfile?.RepresentativeName,
            OtherGoalsRaw = client.FinancialProfile?.OtherGoalsRaw,
            BankDetailRaw = client.FinancialProfile?.BankDetailRaw,
            WillDetailRaw = client.FinancialProfile?.WillDetailRaw,
            OtherDetailsRaw = client.FinancialProfile?.OtherDetailsRaw,
            ContactPoints = client.ContactPoints
                .OrderBy(contact => contact.SortOrder)
                .Select(contact => new ClientContactPointEditModel
                {
                    ContactType = contact.ContactType,
                    Label = contact.Label,
                    Value = contact.Value,
                    IsPrimary = contact.IsPrimary
                })
                .ToList(),
            Addresses = client.Addresses
                .OrderBy(address => address.SortOrder)
                .Select(address => new ClientAddressEditModel
                {
                    AddressType = address.AddressType,
                    LinesRaw = address.LinesRaw
                })
                .ToList(),
            Relationships = client.Relationships
                .OrderBy(relationship => relationship.RelationshipType)
                .ThenBy(relationship => relationship.Name)
                .Select(relationship => new ClientRelationshipEditModel
                {
                    RelationshipType = relationship.RelationshipType,
                    LegacyRelatedClientId = relationship.LegacyRelatedClientId,
                    Name = relationship.Name,
                    Initials = relationship.Initials,
                    Gender = relationship.Gender,
                    BirthDate = relationship.BirthDate,
                    SouthAfricanIdNumber = relationship.SouthAfricanIdNumber,
                    Email = relationship.Email,
                    HomePhone = relationship.HomePhone,
                    WorkPhone = relationship.WorkPhone,
                    MobilePhone = relationship.MobilePhone,
                    Employer = relationship.Employer,
                    Occupation = relationship.Occupation,
                    HighestQualification = relationship.HighestQualification,
                    GrossMonthlySalary = relationship.GrossMonthlySalary,
                    GrossAnnualSalary = relationship.GrossAnnualSalary,
                    YearlyBonus = relationship.YearlyBonus,
                    OtherIncome = relationship.OtherIncome,
                    PensionFundName = relationship.PensionFundName,
                    EmployerPensionContributionAmount = relationship.EmployerPensionContributionAmount,
                    EmployerPensionContributionPercent = relationship.EmployerPensionContributionPercent
                })
                .ToList()
        };
    }
}

public sealed class ClientContactPointEditModel
{
    public string ContactType { get; set; } = "Email";
    public string? Label { get; set; }
    public string? Value { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class ClientAddressEditModel
{
    public string AddressType { get; set; } = "Physical";
    public string? LinesRaw { get; set; }
}

public sealed class ClientRelationshipEditModel
{
    public string RelationshipType { get; set; } = "Spouse";
    public int? LegacyRelatedClientId { get; set; }
    public string? Name { get; set; }
    public string? Initials { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? SouthAfricanIdNumber { get; set; }
    public string? Email { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? Employer { get; set; }
    public string? Occupation { get; set; }
    public string? HighestQualification { get; set; }
    public decimal? GrossMonthlySalary { get; set; }
    public decimal? GrossAnnualSalary { get; set; }
    public decimal? YearlyBonus { get; set; }
    public decimal? OtherIncome { get; set; }
    public string? PensionFundName { get; set; }
    public decimal? EmployerPensionContributionAmount { get; set; }
    public decimal? EmployerPensionContributionPercent { get; set; }
}

public sealed class ClientNoteEditModel
{
    public int ClientId { get; set; }
    public int? Id { get; set; }
    public DateOnly? NoteDate { get; set; }
    public string? Title { get; set; }
    public string? Details { get; set; }

    public static ClientNoteEditModel FromNote(ClientNote note) => new()
    {
        ClientId = note.ClientId,
        Id = note.Id,
        NoteDate = note.NoteDate,
        Title = note.Title,
        Details = note.Details
    };
}

public sealed class ClientKycPolicyEditModel
{
    public int ClientId { get; set; }
    public int? Id { get; set; }
    public string? MainClassName { get; set; }
    public string? SubClassName { get; set; }
    public string? SubClassExtra { get; set; }
    public string? Administrator { get; set; }
    public string? Product { get; set; }
    public string? PolicyNumber { get; set; }
    public string? Description { get; set; }
    public string? Fund { get; set; }
    public decimal? Value { get; set; }
    public decimal? LifeCover { get; set; }
    public decimal? DisabilityCover { get; set; }
    public decimal? DreadDiseaseCover { get; set; }
    public decimal? CompulsoryContributionValue { get; set; }
    public decimal? VoluntaryContributionValue { get; set; }
    public decimal? Debt { get; set; }
    public decimal? MonthlyPremium { get; set; }
    public decimal? OnceOffPremium { get; set; }
    public decimal? MonthlyIncome { get; set; }
    public decimal? CapitalAdequacyRatioPercent { get; set; }
    public decimal? TaxPercent { get; set; }
    public bool IncludeInCalculations { get; set; }
    public bool SurrenderOrLiquidate { get; set; }
    public bool IsRetirementAnnuity { get; set; }
    public bool IsPreservationFund { get; set; }
    public bool IsRetrenchmentPackage { get; set; }
    public bool IsQuote { get; set; }
    public DateTime? ValuationDate { get; set; }

    public static ClientKycPolicyEditModel FromPolicy(ClientKycPolicy policy) => new()
    {
        ClientId = policy.ClientId,
        Id = policy.Id,
        MainClassName = policy.MainClassName,
        SubClassName = policy.SubClassName,
        SubClassExtra = policy.SubClassExtra,
        Administrator = policy.Administrator,
        Product = policy.Product,
        PolicyNumber = policy.PolicyNumber,
        Description = policy.Description,
        Fund = policy.Fund,
        Value = policy.Value,
        LifeCover = policy.LifeCover,
        DisabilityCover = policy.DisabilityCover,
        DreadDiseaseCover = policy.DreadDiseaseCover,
        CompulsoryContributionValue = policy.CompulsoryContributionValue,
        VoluntaryContributionValue = policy.VoluntaryContributionValue,
        Debt = policy.Debt,
        MonthlyPremium = policy.MonthlyPremium,
        OnceOffPremium = policy.OnceOffPremium,
        MonthlyIncome = policy.MonthlyIncome,
        CapitalAdequacyRatioPercent = policy.CapitalAdequacyRatioPercent,
        TaxPercent = policy.TaxPercent,
        IncludeInCalculations = policy.IncludeInCalculations,
        SurrenderOrLiquidate = policy.SurrenderOrLiquidate,
        IsRetirementAnnuity = policy.IsRetirementAnnuity,
        IsPreservationFund = policy.IsPreservationFund,
        IsRetrenchmentPackage = policy.IsRetrenchmentPackage,
        IsQuote = policy.IsQuote,
        ValuationDate = policy.ValuationDate
    };
}
