using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientEditServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Authenticated_edit_page_renders_choices_identity_fields_and_existing_values()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"client-edit-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, KcasRoles.Administrator)).Succeeded);
        var id = await scope.ServiceProvider.GetRequiredService<ClientOperationsService>().SaveClientAsync(new()
        { SurnameOrEntityName = "Synthetic form client", Language = "Dutch", Title = "Advocate", PassportNumber = "SYNTHETIC123", PassportCountry = "United Kingdom" });
        try
        {
            var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user);
            var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            var ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(principal,
                new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, IdentityConstants.ApplicationScheme));
            using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={ticket}");
            var response = await client.GetAsync($"/clients/{id}/edit");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            foreach (var expected in new[] { "<select", "value=\"Male\"", "value=\"Female\"", "value=\"Afrikaans\"", "Dutch (recorded)",
                "Advocate (recorded)", "Passport number", "Passport issuing country", "Passport expiry date", "client-save-bar", "client-edit-nav" })
                Assert.Contains(expected, html);
        }
        finally
        {
            db.ComplianceTasks.RemoveRange(await db.ComplianceTasks.Where(task => task.ClientId == id).ToListAsync());
            db.Clients.Remove(await db.Clients.SingleAsync(row => row.Id == id));
            await db.SaveChangesAsync();
            Assert.True((await users.DeleteAsync(user)).Succeeded);
        }
    }

    [Fact]
    public async Task Passport_details_round_trip_are_searchable_and_change_screening_scope()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var passport = $"P-{Guid.NewGuid():N}";
        var id = await service.SaveClientAsync(new() { SurnameOrEntityName = "Synthetic passport client", PassportNumber = passport,
            PassportCountry = "United Kingdom", PassportExpiryDate = new DateOnly(2030, 1, 31), UpdatedBy = "synthetic@example.test" });
        var loaded = await service.LoadClientAsync(id);
        Assert.Equal(passport, loaded.PassportNumber);
        Assert.Equal("United Kingdom", loaded.PassportCountry);
        Assert.Equal(new DateOnly(2030, 1, 31), loaded.PassportExpiryDate);
        Assert.Null(loaded.SouthAfricanIdNumber);
        Assert.Contains(await new ClientSearchService(db).SearchAsync(passport), client => client.Id == id);
        var before = Assert.Single(await ClientSanctionsCoverageService.PopulationAsync(db, id));
        using (var identity = JsonDocument.Parse(before.IdentitySummary))
            Assert.Equal(passport, identity.RootElement.GetProperty("PassportNumber").GetString());
        loaded.PassportNumber += "-2";
        await service.SaveClientAsync(loaded);
        var after = Assert.Single(await ClientSanctionsCoverageService.PopulationAsync(db, id));
        Assert.NotEqual(before.ScopeHash, after.ScopeHash);
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Invalid_save_does_not_mutate_the_tracked_client_or_create_a_new_one()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var id = await service.SaveClientAsync(new() { SurnameOrEntityName = "Before validation", SouthAfricanIdNumber = "8001015009087" });
        var model = await service.LoadClientAsync(id);
        model.SurnameOrEntityName = "Must not be applied";
        model.SouthAfricanIdNumber = "8001015009088";
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveClientAsync(model));
        Assert.Equal("Before validation", db.Clients.Local.Single(client => client.Id == id).SurnameOrEntityName);
        var marker = "Rejected-" + Guid.NewGuid();
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveClientAsync(new() { SurnameOrEntityName = marker, PassportNumber = "P123" }));
        Assert.DoesNotContain(db.Clients.Local, client => client.SurnameOrEntityName == marker);
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Saving_other_details_preserves_imported_invalid_ids_and_unknown_choices()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var service = scope.ServiceProvider.GetRequiredService<ClientOperationsService>();
        var client = new Client { SurnameOrEntityName = "Synthetic imported", DisplayName = "Synthetic imported", Title = "Advocate", Language = "Dutch",
            PersonalProfile = new() { SouthAfricanIdNumber = "unknown", Gender = "M", MaritalStatus = "Imported description" } };
        client.Relationships.Add(new() { RelationshipType = "Spouse", Name = "Synthetic spouse", SouthAfricanIdNumber = "unknown" });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var before = Assert.Single(await ClientSanctionsCoverageService.PopulationAsync(db, client.Id), item => item.SubjectKey == $"C:{client.Id}");
        Assert.DoesNotContain("PassportNumber", before.IdentitySummary);
        var model = await service.LoadClientAsync(client.Id);
        model.OtherDetailsRaw = "Updated planning note";
        await service.SaveClientAsync(model);
        var loaded = await service.LoadClientAsync(client.Id);
        Assert.Equal("unknown", loaded.SouthAfricanIdNumber);
        Assert.Equal("unknown", Assert.Single(loaded.Relationships).SouthAfricanIdNumber);
        Assert.Equal("Dutch", loaded.Language);
        Assert.Equal("Advocate", loaded.Title);
        var after = Assert.Single(await ClientSanctionsCoverageService.PopulationAsync(db, client.Id), item => item.SubjectKey == $"C:{client.Id}");
        Assert.Equal(before.ScopeHash, after.ScopeHash);
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
    }
}
