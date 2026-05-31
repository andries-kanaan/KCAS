# KCAS Blazor Admin

Modern Blazor rewrite workspace for Kanaan Client Administration System.

## Current Setup

- .NET SDK: local SDK under `.dotnet`
- App: `src/KCAS.Admin`
- Framework: Blazor with ASP.NET Core Identity
- Database: local MySQL configured through `src/KCAS.Admin/appsettings.Development.json`
- Database name: `kcas_blazor`
- EF provider: `MySql.EntityFrameworkCore`

The schema contains ASP.NET Core Identity tables plus the normalized first-pass client import model.

## Run The App

```powershell
.\Start-KCAS.ps1
```

Open `http://localhost:5143` directly, or use the WAMP reverse proxy at `https://kcas.test:8443/`.

The script builds the app and launches `KCAS.Admin.dll` through the local SDK. This avoids a local Windows/OneDrive permission issue where `dotnet run` cannot start the generated `KCAS.Admin.exe`.

## Database

WAMP's MySQL client needs the plugin directory specified on this machine:

```powershell
C:\wamp64\bin\mysql\mysql9.1.0\bin\mysql.exe --plugin-dir=C:\wamp64\bin\mysql\mysql9.1.0\lib\plugin --protocol=tcp --host=127.0.0.1 --port=3307 --user=root
```

The generated initial SQL script is:

```text
src\KCAS.Admin\Data\Migrations\kcas_blazor_initial.sql
```

Apply it to a fresh database with:

```powershell
.\Apply-KCAS-Database.ps1
```

For normal local development after the first setup, apply EF migrations with:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
.\.dotnet\dotnet.exe ef database update --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj
```

## Legacy Client Import

The Yii1 client import is a console tool so imports can run deliberately outside the web UI.

```powershell
$env:KCAS_LEGACY_CONNECTION = '<legacy MySQL connection string>'
$env:KCAS_TARGET_CONNECTION = '<KCAS MySQL connection string>'
.\.dotnet\dotnet.exe tools\KCAS.LegacyImport\bin\Debug\net10.0\KCAS.LegacyImport.dll
```

Add `--dry-run` to validate connection and mapping without writing clients.

## EF Notes

`dotnet-ef` is installed as a local tool in `dotnet-tools.json`.

```powershell
.\.dotnet\dotnet.exe tool restore
.\.dotnet\dotnet.exe tool run dotnet-ef migrations add MigrationName --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output-dir Data\Migrations
.\.dotnet\dotnet.exe tool run dotnet-ef migrations script --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output src\KCAS.Admin\Data\Migrations\MigrationName.sql
```

Automatic `Database.Migrate()` is disabled in `appsettings.json` for production-style startup control. Use explicit EF migration commands or reviewed SQL scripts when changing the schema.
