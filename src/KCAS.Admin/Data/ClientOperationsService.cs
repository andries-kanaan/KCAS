using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientOperationsService(ApplicationDbContext db)
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

        client.KanaanId = Normalize(model.KanaanId);
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

    private async Task<Client> LoadClientAggregateAsync(int clientId)
    {
        return await db.Clients
            .Include(client => client.PersonalProfile)
            .Include(client => client.FinancialProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.Addresses)
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
