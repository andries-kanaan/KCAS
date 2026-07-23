using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KCAS.Admin.Components;
using KCAS.Admin.Components.Account;
using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using KCAS.Admin.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication()
        .AddNegotiate();
}

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in KcasPermissions.All)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireClaim(KcasClaimTypes.Permission, permission));
    }
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySQL(connectionString));
builder.Services.AddScoped<ClientSearchService>();
builder.Services.AddScoped<ClientCodeGenerator>();
builder.Services.AddScoped<ClientOperationsService>();
builder.Services.AddScoped<ComplianceService>();
builder.Services.AddScoped<LegacyImportWebService>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
        options.Stores.MaxLengthForKeys = 64;
        options.User.AllowedUserNameCharacters += "\\";
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();
var webRoot = app.Environment.WebRootPath;

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

await KcasSecuritySeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/kcas.css", () =>
    Results.Text(File.ReadAllText(Path.Combine(webRoot, "app.css")), "text/css"));

app.MapGet("/kcas-bootstrap.css", () =>
    Results.Text(File.ReadAllText(Path.Combine(webRoot, "lib", "bootstrap", "dist", "css", "bootstrap.min.css")), "text/css"));

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (ApplicationDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "Healthy" })
        : Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/Account/WindowsLogin", async (
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole> roleManager) =>
    {
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString },
                [NegotiateDefaults.AuthenticationScheme]);
        }

        var windowsAccountName = context.User.Identity.Name;
        var user = await userManager.Users.SingleOrDefaultAsync(user => user.WindowsAccountName == windowsAccountName);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = windowsAccountName,
                WindowsAccountName = windowsAccountName,
                DisplayName = windowsAccountName,
                IsApproved = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return Results.BadRequest(string.Join("; ", createResult.Errors.Select(error => error.Description)));
            }
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.LocalRedirect(user.IsApproved
            ? await KcasPostLoginRedirects.GetApprovedUserPathAsync(userManager, roleManager, user)
            : "/Account/PendingApproval");
    })
    .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = NegotiateDefaults.AuthenticationScheme });
}

app.Run();

public partial class Program;
