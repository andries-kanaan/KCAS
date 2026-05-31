using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientSearchServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Search_finds_imported_clients_by_legacy_identity_and_contact_details()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new ClientSearchService(db);

        var client = new Client
        {
            LegacyClientId = 500,
            KanaanId = "123",
            SurnameOrEntityName = "Botha",
            DisplayName = "Botha, C",
            PersonalProfile = new ClientPersonalProfile { SouthAfricanIdNumber = "7901015009088" },
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "client@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0820000000", IsPrimary = true, SortOrder = 20 }
            }
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        Assert.Contains(await service.SearchAsync("123"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("Botha"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("7901015009088"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("client@example.test"), result => result.Id == client.Id);
        Assert.Contains(await service.SearchAsync("0820000000"), result => result.Id == client.Id);
    }

    [Fact]
    public async Task Search_supports_column_filters_and_sorting()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new ClientSearchService(db);

        db.Clients.Add(new Client
        {
            LegacyClientId = 501,
            KanaanId = "900",
            SurnameOrEntityName = "Zulu",
            DisplayName = "Zulu, Z",
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "zulu@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0830000000", IsPrimary = true, SortOrder = 20 }
            }
        });
        db.Clients.Add(new Client
        {
            LegacyClientId = 502,
            KanaanId = "100",
            SurnameOrEntityName = "Alpha",
            DisplayName = "Alpha, A",
            ContactPoints =
            {
                new ClientContactPoint { ContactType = "Email", Value = "alpha@example.test", IsPrimary = true, SortOrder = 10 },
                new ClientContactPoint { ContactType = "Mobile", Value = "0840000000", IsPrimary = true, SortOrder = 20 }
            }
        });
        await db.SaveChangesAsync();

        var filtered = await service.SearchAsync(new ClientSearchRequest(Email: "zulu@example.test"));
        Assert.Contains(filtered, result => result.KanaanId == "900");
        Assert.DoesNotContain(filtered, result => result.KanaanId == "100");

        var sorted = await service.SearchAsync(new ClientSearchRequest(SortColumn: "kanaanId", SortDescending: true));
        Assert.True(sorted.FindIndex(result => result.KanaanId == "900") < sorted.FindIndex(result => result.KanaanId == "100"));
    }

    [Fact]
    public async Task Client_can_load_imported_notes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var client = new Client
        {
            LegacyClientId = 503,
            KanaanId = "503",
            SurnameOrEntityName = "Notes",
            DisplayName = "Notes Client"
        };
        client.Notes.Add(new ClientNote
        {
            LegacyClientNoteId = 9001,
            NoteDate = new DateOnly(2026, 5, 31),
            Title = "Imported note",
            Details = "Imported details",
            IsFinal = true,
            IsDeleted = false,
            PayloadJson = "{}"
        });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var clientId = client.Id;

        var loaded = await db.Clients
            .Include(client => client.Notes)
            .SingleAsync(client => client.Id == clientId);

        Assert.Contains(loaded.Notes, note => note.LegacyClientNoteId == 9001);
    }
}
