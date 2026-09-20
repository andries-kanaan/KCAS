using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
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
    options.AddPolicy(KcasPolicies.AdministratorOnly, policy =>
        policy.RequireRole(KcasRoles.Administrator));
    foreach (var permission in KcasPermissions.All)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireClaim(KcasClaimTypes.Permission, permission));
    }
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySQL(connectionString));
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseMySQL(connectionString), ServiceLifetime.Scoped);
builder.Services.AddScoped(provider =>
    new ClientSearchService(provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>()));
builder.Services.AddScoped<ClientCodeGenerator>();
builder.Services.AddScoped<ClientOperationsService>();
builder.Services.AddScoped<InvestmentSummaryService>();
builder.Services.AddScoped<InvestmentReconciliationService>();
builder.Services.AddScoped<ClientReviewTransferService>();
builder.Services.AddScoped<ClientDuplicateReviewTransferService>();
builder.Services.AddScoped<ClientReviewFamilyTransferService>();
builder.Services.AddScoped<ComplianceService>();
builder.Services.AddScoped<ClientEvidenceReadinessService>();
builder.Services.AddScoped<ClientEntityOwnershipService>();
builder.Services.AddScoped<ClientRiskAssessmentService>();
builder.Services.AddScoped<ClientOperationalVerificationService>();
builder.Services.AddScoped<ClientComplianceReviewService>();
builder.Services.AddScoped<ClientAdviceService>();
builder.Services.AddScoped<ClientAdviceTransferService>();
builder.Services.AddSingleton<DocumentPathDisplayService>();
builder.Services.AddScoped<BusinessRiskAssessmentService>();
builder.Services.AddScoped<RmcpService>();
builder.Services.AddScoped<ComplianceWorkService>();
builder.Services.AddScoped<InspectionService>();
builder.Services.AddScoped<GoAmlDailyCheckService>();
builder.Services.AddScoped<GoAmlTransferService>();
builder.Services.AddScoped<ComplianceProgrammeTransferService>();
builder.Services.AddSingleton<ClientEvidenceScanCoordinator>();
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
await ComplianceFoundationSeeder.SeedAsync(app.Services);
await FscaInspectionSeeder.SeedAsync(app.Services);

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

app.MapGet("/client-evidence/items/{id:int}/file", async Task<IResult> (int id, ApplicationDbContext db, CancellationToken cancellationToken) =>
{
    var item = await db.ClientEvidenceItems.AsNoTracking()
        .Where(item => item.Id == id)
        .Select(item => new
        {
            item.SourcePath,
            item.RelativePath,
            item.FileName,
            item.Client.ClientFolder
        })
        .SingleOrDefaultAsync(cancellationToken);
    var activeServerRoot = await db.ClientEvidenceScanRoots.AsNoTracking()
        .Where(root => root.IsActive)
        .OrderByDescending(root => root.Id)
        .Select(root => root.RootPath)
        .FirstOrDefaultAsync(cancellationToken);
    var resolvedPath = item is null
        ? null
        : ClientEvidenceFileResolver.ResolveExistingPath(
            item.SourcePath, item.RelativePath, item.FileName, item.ClientFolder, activeServerRoot);
    if (resolvedPath is null)
    {
        return Results.NotFound();
    }

    var contentTypeProvider = new FileExtensionContentTypeProvider();
    if (!contentTypeProvider.TryGetContentType(resolvedPath, out var contentType))
    {
        contentType = "application/octet-stream";
    }

    return Results.File(File.OpenRead(resolvedPath), contentType, enableRangeProcessing: true);
}).RequireAuthorization(KcasPermissions.ComplianceView);

app.MapGet("/compliance/evidence/{id:int}/file", async Task<IResult> (int id, ApplicationDbContext db, CancellationToken cancellationToken) =>
{
    var evidence = await db.ComplianceEvidence.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (evidence is null || string.IsNullOrWhiteSpace(evidence.Location) || !File.Exists(evidence.Location))
    {
        return Results.NotFound();
    }

    var contentTypeProvider = new FileExtensionContentTypeProvider();
    if (!contentTypeProvider.TryGetContentType(evidence.Location, out var contentType))
    {
        contentType = "application/octet-stream";
    }

    return Results.File(File.OpenRead(evidence.Location), contentType, enableRangeProcessing: true);
}).RequireAuthorization(KcasPermissions.ComplianceView);

app.MapGet("/compliance/goaml/checks/{id:int}/evidence", async Task<IResult> (
    int id,
    GoAmlDailyCheckService goAml,
    CancellationToken cancellationToken) =>
{
    var evidence = await goAml.OpenEvidenceAsync(id, cancellationToken);
    return evidence is null
        ? Results.NotFound()
        : Results.File(evidence.Stream, evidence.ContentType, evidence.FileName, enableRangeProcessing: true);
}).RequireAuthorization(KcasPermissions.ComplianceView);

app.MapGet("/compliance/goaml/transfers/{packageId}/download", async Task<IResult> (
    string packageId,
    ApplicationDbContext db,
    CancellationToken cancellationToken) =>
{
    var record = await db.GoAmlTransferRecords.AsNoTracking()
        .SingleOrDefaultAsync(item =>
            item.Direction == GoAmlTransferDirections.Outgoing &&
            item.PackageId == packageId,
            cancellationToken);
    if (record is null || !File.Exists(record.StoragePath))
    {
        return Results.NotFound();
    }

    return Results.File(
        record.StoragePath,
        "application/vnd.kcas.goaml-transfer",
        record.FileName);
}).RequireAuthorization(KcasPolicies.AdministratorOnly);

app.MapGet("/compliance/programme-transfers/{packageId}/download", async Task<IResult> (
    string packageId,
    ComplianceProgrammeTransferService transfers,
    CancellationToken cancellationToken) =>
{
    var package = await transfers.OpenExportAsync(packageId, cancellationToken);
    if (package is null)
    {
        return Results.NotFound();
    }

    return Results.File(
        package.StoragePath,
        "application/vnd.kcas.compliance-programme",
        package.FileName);
}).RequireAuthorization(KcasPolicies.AdministratorOnly);

app.MapGet("/investments/summary.csv", async Task<IResult> (
    HttpContext context,
    InvestmentSummaryService investments,
    CancellationToken cancellationToken) =>
{
    var query = ParseInvestmentSummaryQuery(context.Request.Query);
    var csv = await investments.ExportCsvAsync(query, cancellationToken);
    return Results.File(
        csv,
        "text/csv; charset=utf-8",
        $"KCAS-investment-summary-{DateTime.Today:yyyy-MM-dd}.csv");
}).RequireAuthorization(KcasPermissions.InvestmentsView);

app.MapGet("/investments/summary.pdf", async Task<IResult> (
    HttpContext context,
    InvestmentSummaryService investments,
    CancellationToken cancellationToken) =>
{
    var query = ParseInvestmentSummaryQuery(context.Request.Query);
    var pdf = await investments.ExportPdfAsync(query, cancellationToken);
    return Results.File(
        pdf,
        "application/pdf",
        $"KCAS-investment-summary-{DateTime.Today:yyyy-MM-dd}.pdf");
}).RequireAuthorization(KcasPermissions.InvestmentsView);

app.MapGet("/advice/{caseId:int}/record.pdf", async Task<IResult> (
    int caseId,
    ClientAdviceService advice) =>
{
    var pdf = await advice.ExportPdfAsync(caseId);
    return Results.File(pdf, "application/pdf", $"KCAS-advice-record-{caseId}-{DateTime.Today:yyyy-MM-dd}.pdf");
}).RequireAuthorization(KcasPermissions.AdviceView);

app.MapGet("/advice/{caseId:int}/draft-preview.pdf", async Task<IResult> (
    int caseId,
    HttpContext context,
    ClientAdviceService advice) =>
{
    var pdf = await advice.ExportDraftPreviewPdfAsync(caseId, context.User.Identity?.Name);
    return Results.File(pdf, "application/pdf", $"DRAFT-KCAS-advice-record-{caseId}-{DateTime.Today:yyyy-MM-dd}.pdf");
}).RequireAuthorization(KcasPermissions.AdvicePrepare);

app.MapGet("/advice/{caseId:int}/review-manifest.json", async Task<IResult> (
    int caseId,
    ClientAdviceService advice) =>
{
    var json = await advice.ExportReviewManifestAsync(caseId);
    return Results.File(json, "application/json", $"KCAS-advice-review-{caseId}.json");
}).RequireAuthorization(KcasPermissions.AdviceView);

app.MapGet("/advice/documents/{id:int}/file", async Task<IResult> (
    int id,
    ApplicationDbContext db,
    CancellationToken cancellationToken) =>
{
    var document = await db.ClientAdviceDocuments.AsNoTracking()
        .Where(item => item.Id == id)
        .Select(item => new { item.SourcePath, item.FileName, item.Client.ClientFolder })
        .SingleOrDefaultAsync(cancellationToken);
    var activeServerRoot = await db.ClientEvidenceScanRoots.AsNoTracking()
        .Where(root => root.IsActive).OrderByDescending(root => root.Id)
        .Select(root => root.RootPath).FirstOrDefaultAsync(cancellationToken);
    var path = document is null ? null : ClientEvidenceFileResolver.ResolveExistingPath(
        document.SourcePath, null, document.FileName, document.ClientFolder, activeServerRoot);
    if (path is null) return Results.NotFound();
    var contentTypes = new FileExtensionContentTypeProvider();
    if (!contentTypes.TryGetContentType(path, out var contentType)) contentType = "application/octet-stream";
    return Results.File(File.OpenRead(path), contentType, enableRangeProcessing: true);
}).RequireAuthorization(KcasPermissions.AdviceView);

app.MapGet("/advice/transfers/{packageId}/download", async Task<IResult> (
    string packageId,
    ClientAdviceTransferService transfers,
    CancellationToken cancellationToken) =>
{
    var package = await transfers.OpenExportAsync(packageId, cancellationToken);
    return package is null
        ? Results.NotFound()
        : Results.File(package.Path, "application/vnd.kcas.client-advice", package.FileName);
}).RequireAuthorization(KcasPolicies.AdministratorOnly);

app.MapGet("/compliance/client-risk/register.csv", async Task<IResult> (
    HttpContext context,
    ClientRiskAssessmentService risk,
    CancellationToken cancellationToken) =>
{
    var query = ParseClientRiskRegisterQuery(context.Request.Query);
    var csv = await risk.ExportRegisterCsvAsync(query, cancellationToken);
    var asAtDate = query.AsAtDate ?? DateOnly.FromDateTime(DateTime.Today);
    return Results.File(
        csv,
        "text/csv; charset=utf-8",
        $"KCAS-client-risk-register-{asAtDate:yyyy-MM-dd}.csv");
}).RequireAuthorization(KcasPermissions.RiskAssessmentsView);

app.MapGet("/compliance/client-risk/register.json", async Task<IResult> (
    HttpContext context,
    ClientRiskAssessmentService risk,
    CancellationToken cancellationToken) =>
{
    var query = ParseClientRiskRegisterQuery(context.Request.Query);
    var snapshot = await risk.ExportRegisterSnapshotAsync(query, cancellationToken);
    var asAtDate = query.AsAtDate ?? DateOnly.FromDateTime(DateTime.Today);
    return Results.File(
        snapshot,
        "application/json",
        $"KCAS-client-risk-register-{asAtDate:yyyy-MM-dd}.json");
}).RequireAuthorization(KcasPermissions.InspectionsExport);

app.MapGet("/compliance/review-transfers/{packageId}/download", async Task<IResult> (
    string packageId,
    ApplicationDbContext db,
    CancellationToken cancellationToken) =>
{
    var record = await db.ClientReviewTransferRecords.AsNoTracking()
        .SingleOrDefaultAsync(item =>
            item.Direction == ClientReviewTransferDirections.Outgoing &&
            item.PackageId == packageId,
            cancellationToken);
    if (record is null || !File.Exists(record.StoragePath))
    {
        return Results.NotFound();
    }

    var contentType = record.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        ? "application/zip"
        : "application/vnd.kcas.client-review";
    return Results.File(
        record.StoragePath,
        contentType,
        record.FileName);
}).RequireAuthorization(KcasPolicies.AdministratorOnly);

app.MapGet("/compliance/inspections/{id:int}/export.json", async Task<IResult> (int id, InspectionService inspections) =>
{
    var pack = await inspections.LoadPrintableAsync(id);
    var safeReference = string.Concat(pack.Reference.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
    return Results.File(
        Encoding.UTF8.GetBytes(pack.SnapshotJson),
        "application/json",
        $"KCAS-inspection-{safeReference}.json");
}).RequireAuthorization(KcasPermissions.InspectionsExport);

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

static InvestmentSummaryQuery ParseInvestmentSummaryQuery(IQueryCollection values)
{
    var clientId = int.TryParse(values["clientId"], out var parsedClientId)
        ? parsedClientId
        : (int?)null;
    var sortDescending = bool.TryParse(values["sortDescending"], out var parsedSortDescending) &&
                         parsedSortDescending;
    var staleAfterDays = int.TryParse(values["staleAfterDays"], out var parsedStaleAfterDays)
        ? Math.Clamp(parsedStaleAfterDays, 1, 3650)
        : 90;
    var scope = values["scope"].ToString();
    if (scope is not (InvestmentSummaryScopes.Current or InvestmentSummaryScopes.Historical or InvestmentSummaryScopes.All))
    {
        scope = InvestmentSummaryScopes.Current;
    }

    return new InvestmentSummaryQuery(
        ClientId: clientId,
        KanaanId: values["kanaanId"],
        Search: values["search"],
        LifecycleStatus: values["lifecycleStatus"],
        FundName: values["fundName"],
        Administrator: values["administrator"],
        Scope: scope,
        SortColumn: string.IsNullOrWhiteSpace(values["sortColumn"]) ? "client" : values["sortColumn"].ToString(),
        SortDescending: sortDescending,
        StaleAfterDays: staleAfterDays);
}

static ClientRiskRegisterQuery ParseClientRiskRegisterQuery(IQueryCollection values)
{
    var asAtDate = DateOnly.TryParse(values["asAtDate"], out var parsedAsAtDate)
        ? parsedAsAtDate
        : DateOnly.FromDateTime(DateTime.Today);
    bool? requiresEdd = bool.TryParse(values["requiresEdd"], out var parsedRequiresEdd)
        ? parsedRequiresEdd
        : null;
    return new ClientRiskRegisterQuery(
        values["search"],
        values["rating"],
        values["status"],
        values["reviewState"],
        values["coverageState"],
        values["readinessState"],
        requiresEdd,
        asAtDate);
}

public partial class Program;
