# KCAS Blazor Admin

Modern Blazor rewrite workspace for Kanaan Client Administration System.

Production releases use the immutable native Windows process documented in [docs/WINDOWS_DEPLOYMENT.md](docs/WINDOWS_DEPLOYMENT.md). GitHub builds the self-contained `win-x64` package; the live server no longer pulls source or compiles the application.

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

## Incremental Legacy Reconciliation

The Yii1 data already in KCAS is a historical baseline. The console tool is scan-first and non-destructive: it matches records by legacy primary ID, adds no duplicates, never updates or deletes an existing business row automatically, and records field-level source differences for review.

For the recurring operator workflow, download a fresh `kanaanclients.sql` export and run one command:

```powershell
.\deploy\windows\Import-KCAS-Legacy.cmd "C:\path\to\kanaanclients.sql"
```

The command reads the same protected database connection configuration as KCAS; it does not ask for or display the password. Review the displayed scan at `/imports`, then apply only its safe new records with:

```powershell
.\deploy\windows\Import-KCAS-Legacy.cmd -ApplyNew <reviewed-scan-run-id>
```

The lower-level commands below are intended for development and troubleshooting.

For local development, set both connection strings and identify the exact source snapshot with its SHA-256:

```powershell
$env:KCAS_LEGACY_CONNECTION = '<staged legacy MySQL connection string>'
$env:KCAS_TARGET_CONNECTION = '<KCAS MySQL connection string>'
$snapshotHash = (Get-FileHash '.\kanaanclients.sql' -Algorithm SHA256).Hash
.\.dotnet\dotnet.exe tools\KCAS.LegacyImport\bin\Debug\net10.0\KCAS.LegacyImport.dll --scan --source-snapshot-sha256 $snapshotHash --source-snapshot-file-name 'kanaanclients.sql'
```

After reviewing the resulting run at `/imports`, apply only the exact new IDs approved by that scan:

```powershell
.\.dotnet\dotnet.exe tools\KCAS.LegacyImport\bin\Debug\net10.0\KCAS.LegacyImport.dll --apply-new --approved-scan-run <run-id> --source-snapshot-sha256 $snapshotHash --source-snapshot-file-name 'kanaanclients.sql'
```

`--scan` is the default; the former `--dry-run` flag remains an alias. Apply mode refuses a different source database, snapshot hash, changed source fingerprint, or incomplete scan. Changed and missing rows remain pending review. Neither mode overwrites existing KCAS values, clears child collections, or deletes rows missing from the source.

Legacy `tbl_fund` valuations and `tbl_kyc` policies are scan-only because their legacy replacement workflows can create new primary IDs for replacement records. They are excluded from `--apply-new` until KCAS has reviewed stable identities and merge rules.

The immutable Windows package includes scripts that restore a SQL export into a checksum-bound staging database, run scans, back up KCAS before apply, and retain audit logs. See `docs/WINDOWS_DEPLOYMENT.md`.

## EF Notes

`dotnet-ef` is installed as a local tool in `dotnet-tools.json`.

```powershell
.\.dotnet\dotnet.exe tool restore
.\.dotnet\dotnet.exe tool run dotnet-ef migrations add MigrationName --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output-dir Data\Migrations
.\.dotnet\dotnet.exe tool run dotnet-ef migrations script --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output src\KCAS.Admin\Data\Migrations\MigrationName.sql
```

Automatic `Database.Migrate()` is disabled in `appsettings.json` for production-style startup control. Use explicit EF migration commands or reviewed SQL scripts when changing the schema.
