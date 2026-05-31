# KCAS Blazor Admin

Modern Blazor rewrite workspace for Kanaan Client Administration System.

## Current Setup

- .NET SDK: local SDK under `.dotnet`
- App: `src/KCAS.Admin`
- Framework: Blazor with ASP.NET Core Identity
- Database: WAMP MySQL on `127.0.0.1:3307`
- Database name: `kcas_blazor`
- EF provider: `MySql.EntityFrameworkCore`

The initial schema contains ASP.NET Core Identity tables and a starter `Clients` table for the migration workspace.

## Run The App

```powershell
.\Start-KCAS.ps1
```

Open `http://localhost:5143`.

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

## EF Notes

`dotnet-ef` is installed as a local tool in `dotnet-tools.json`.

```powershell
.\.dotnet\dotnet.exe tool restore
.\.dotnet\dotnet.exe tool run dotnet-ef migrations add MigrationName --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output-dir Data\Migrations
.\.dotnet\dotnet.exe tool run dotnet-ef migrations script --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj --output src\KCAS.Admin\Data\Migrations\MigrationName.sql
```

Automatic `Database.Migrate()` is disabled in `appsettings.json` because the current Oracle MySQL EF provider fails against this WAMP MySQL 9.1 setup when acquiring EF's migration lock. SQL script application works reliably.
