using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientOperationsServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task SaveClientAsync_creates_and_updates_normalized_client_details()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-1",
            FullName = "Operational",
            SurnameOrEntityName = "Client",
            DisplayName = "Operational Client",
            SouthAfricanIdNumber = "8001015009087",
            Employer = "Kanaan",
            ContactPoints =
            [
                new() { ContactType = "Email", Value = "ops@example.com" },
                new() { ContactType = "Mobile", Value = "0820000000" }
            ],
            Addresses =
            [
                new() { AddressType = "Physical", LinesRaw = "1 Main Road" }
            ],
            Relationships =
            [
                new()
                {
                    RelationshipType = "Spouse",
                    Name = "Spouse Client",
                    LegacyRelatedClientId = 43,
                    Email = "spouse@example.com",
                    GrossMonthlySalary = 20000m
                },
                new()
                {
                    RelationshipType = "FamilyContact",
                    Name = "Emergency Contact",
                    MobilePhone = "0830000000"
                },
                new()
                {
                    RelationshipType = "",
                    Name = " "
                }
            ]
        });

        var editModel = await service.LoadClientAsync(clientId);
        editModel.DisplayName = "Updated Operational Client";
        editModel.ContactPoints[0].Value = "updated@example.com";
        Assert.Contains(editModel.Relationships, relationship =>
            relationship.RelationshipType == "Spouse" &&
            relationship.LegacyRelatedClientId == 43 &&
            relationship.Name == "Spouse Client");

        var spouse = editModel.Relationships.Single(relationship => relationship.RelationshipType == "Spouse");
        spouse.Name = "Updated Spouse";
        spouse.Email = "updated-spouse@example.com";
        editModel.Relationships.RemoveAll(relationship => relationship.RelationshipType == "FamilyContact");
        editModel.Relationships.Add(new ClientRelationshipEditModel
        {
            RelationshipType = "Child",
            Name = "Child Client",
            BirthDate = new DateTime(2010, 1, 2),
            SouthAfricanIdNumber = "1001020000000"
        });
        await service.SaveClientAsync(editModel);

        var saved = await db.Clients
            .AsNoTracking()
            .Include(client => client.PersonalProfile)
            .Include(client => client.FinancialProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.Addresses)
            .Include(client => client.Relationships)
            .SingleAsync(client => client.Id == clientId);

        Assert.Equal("Updated Operational Client", saved.DisplayName);
        Assert.Equal("OPS-1", saved.KanaanId);
        Assert.Null(saved.LegacyClientId);
        Assert.Equal("8001015009087", saved.PersonalProfile?.SouthAfricanIdNumber);
        Assert.Equal("Kanaan", saved.FinancialProfile?.Employer);
        Assert.Contains(saved.ContactPoints, contact => contact.Value == "updated@example.com" && contact.IsPrimary);
        Assert.Contains(saved.Addresses, address => address.AddressType == "Physical");
        Assert.Contains(saved.Relationships, relationship =>
            relationship.RelationshipType == "Spouse" &&
            relationship.LegacyRelatedClientId == 43 &&
            relationship.Name == "Updated Spouse" &&
            relationship.Email == "updated-spouse@example.com");
        Assert.Contains(saved.Relationships, relationship =>
            relationship.RelationshipType == "Child" &&
            relationship.BirthDate == new DateTime(2010, 1, 2) &&
            relationship.SouthAfricanIdNumber == "1001020000000");
        Assert.DoesNotContain(saved.Relationships, relationship => relationship.RelationshipType == "FamilyContact");
        Assert.Equal(2, saved.Relationships.Count);
    }

    [Fact]
    public async Task SaveClientAsync_generates_kanaan_id_for_native_clients()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstClientId = await service.SaveClientAsync(new ClientEditModel
        {
            SurnameOrEntityName = "Generated",
            DisplayName = "Generated Client"
        });

        var secondClientId = await service.SaveClientAsync(new ClientEditModel
        {
            SurnameOrEntityName = "Generated Two",
            DisplayName = "Generated Client Two"
        });

        var saved = await db.Clients
            .AsNoTracking()
            .Where(client => client.Id == firstClientId || client.Id == secondClientId)
            .OrderBy(client => client.Id)
            .ToListAsync();

        Assert.Equal(2, saved.Count);
        Assert.All(saved, client =>
        {
            Assert.StartsWith("KCAS-", client.KanaanId);
            Assert.Null(client.LegacyClientId);
        });
        Assert.NotEqual(saved[0].KanaanId, saved[1].KanaanId);
    }

    [Fact]
    public async Task SaveClientAsync_allows_shared_kanaan_id_for_family_units()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstClientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "FAMILY-001",
            SurnameOrEntityName = "Original",
            DisplayName = "Original Client"
        });

        var secondClientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "FAMILY-001",
            SurnameOrEntityName = "Spouse",
            DisplayName = "Spouse Client"
        });

        var saved = await db.Clients
            .AsNoTracking()
            .Where(client => client.Id == firstClientId || client.Id == secondClientId)
            .ToListAsync();

        Assert.Equal(2, saved.Count);
        Assert.All(saved, client => Assert.Equal("FAMILY-001", client.KanaanId));
    }

    [Fact]
    public async Task Notes_can_be_created_edited_finalized_and_soft_deleted_without_legacy_id()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientId = await service.SaveClientAsync(new ClientEditModel
        {
            KanaanId = "OPS-2",
            SurnameOrEntityName = "Notes",
            DisplayName = "Notes Client"
        });

        var noteId = await service.SaveNoteAsync(new ClientNoteEditModel
        {
            ClientId = clientId,
            NoteDate = new DateOnly(2026, 5, 31),
            Title = "Draft note",
            Details = "Initial details"
        }, "tester");

        var noteModel = await service.LoadNoteAsync(clientId, noteId);
        noteModel.Details = "Updated details";
        await service.SaveNoteAsync(noteModel, "tester");
        await service.FinalizeNoteAsync(clientId, noteId, "tester");
        await service.DeleteNoteAsync(clientId, noteId, "tester");

        var saved = await db.ClientNotes.AsNoTracking().SingleAsync(note => note.Id == noteId);
        Assert.Null(saved.LegacyClientNoteId);
        Assert.Equal("Updated details", saved.Details);
        Assert.True(saved.IsFinal);
        Assert.True(saved.IsDeleted);
        Assert.Equal("tester", saved.UpdatedBy);
    }
}
