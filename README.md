# KCAS Blazor Admin

Modern Blazor rewrite workspace for Kanaan Client Administration System.

## Current Setup

- .NET SDK: local SDK under `.dotnet`
- App: `src/KCAS.Admin`
- Framework: Blazor with ASP.NET Core Identity
- Database: local MySQL configured through `src/KCAS.Admin/appsettings.Development.json`
- Database name: `kcas_blazor`
- EF provider: `MySql.EntityFrameworkCore`

The schema contains ASP.NET Core Identity tables plus the normalized client operations model.

## Run The App

```powershell
.\Start-KCAS.ps1
```

Open `http://localhost:5143` directly, or use the WAMP reverse proxy at `https://kcas.test:8443/`.

The script builds the app and launches `KCAS.Admin.dll` through the local SDK. This avoids a local Windows/OneDrive permission issue where `dotnet run` cannot start the generated `KCAS.Admin.exe`.

After stopping Kestrel for builds or verification, restart and verify the local proxy with:

```powershell
.\Restart-KCAS.ps1
```

## Database

WAMP's MySQL client needs the plugin directory specified on this machine:

```powershell
C:\wamp64\bin\mysql\mysql9.1.0\bin\mysql.exe --plugin-dir=C:\wamp64\bin\mysql\mysql9.1.0\lib\plugin --protocol=tcp --host=127.0.0.1 --port=3306 --user=root
```

The generated full schema script for a fresh database is:

```text
src\KCAS.Admin\Data\Migrations\kcas_blazor_schema.sql
```

Apply it to a fresh database with:

```powershell
.\Apply-KCAS-Database.ps1
```

`Apply-KCAS-Database.ps1` derives the latest migration from `src\KCAS.Admin\Data\Migrations`, checks `__EFMigrationsHistory`, and then either:

- applies the full schema to an empty database, or
- applies a reviewed targeted script from `src\KCAS.Admin\Data\Migrations\Scripts`.

For production WAMP on `D:\wamp64`:

```powershell
.\Apply-KCAS-Database.ps1 -MySqlBasePath 'D:\wamp64\bin\mysql\mysql9.1.0' -Port 3306
```

The same settings can be supplied through environment variables:

```powershell
$env:KCAS_MYSQL_BASE_PATH = 'D:\wamp64\bin\mysql\mysql9.1.0'
$env:KCAS_MYSQL_PORT = '3306'
.\Apply-KCAS-Database.ps1
```

For an existing database, do not run the fresh schema script over the existing tables. Generate a targeted migration script from the database's current migration to the latest migration, review it, commit it under `src\KCAS.Admin\Data\Migrations\Scripts`, then apply it during a controlled migration window:

```powershell
.\.dotnet\dotnet.exe tool run dotnet-ef migrations script FromMigrationName ToMigrationName --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output src\KCAS.Admin\Data\Migrations\Scripts\FromMigrationName_to_ToMigrationName.sql
```

For normal local development after the first setup, apply EF migrations with:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
.\.dotnet\dotnet.exe ef database update --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj
```

## Legacy Client Import

The Yii1 client import is a console tool so imports can run deliberately outside the web UI. During development, imported rows are seed data for building and validating KCAS against realistic records. They are disposable and can be cleared before the future final import from the latest `kanaanclients` data.

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
