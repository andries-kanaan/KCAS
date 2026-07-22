# KCAS Immutable Windows Deployment

Last updated: 2026-07-22

## Purpose

KCAS is deployed as an immutable, self-contained Windows release built and tested by GitHub Actions. The production server does not pull source code or compile the application.

This deployment keeps the current architecture for the first transition:

- Windows Server and Windows Authentication;
- Kestrel;
- the existing `KCAS` Scheduled Task and its existing Windows identity;
- Apache/WAMP as reverse proxy and TLS endpoint;
- MySQL outside the application release;
- reviewed, explicit database migration scripts.

Converting the Scheduled Task to a Windows Service is a later infrastructure change and must not be mixed into the first immutable-release deployment.

## Release flow

1. A pull request is built and tested by `.github/workflows/pr-checks.yml`.
2. After merge, run the `Windows release package` workflow against the required `main` commit, or create a reviewed `v*` tag.
3. The workflow independently builds and tests the commit.
4. GitHub publishes a self-contained `win-x64` ZIP and its SHA-256 checksum.
5. The server deploys that exact package into `D:\Deploy\KCAS\releases\<full-commit-sha>`.
6. `D:\Deploy\KCAS\current` is a directory junction pointing to the active release.
7. The Scheduled Task always starts `D:\Deploy\KCAS\current\app\KCAS.Admin.exe`.

Tagged builds are attached to a GitHub Release. Manually dispatched builds are retained as GitHub Actions artifacts for 30 days.

## Package contents

Each ZIP contains:

```text
app\
    KCAS.Admin.exe
    ...self-contained .NET runtime and application files...
database\
    Apply-KCAS-Database.ps1
    Migrations\
tools\
    legacy-import\
        KCAS.LegacyImport.exe
        Stage-KCAS-LegacySnapshot.ps1
        Run-KCAS-LegacyImport.ps1
deployment-manifest.json
```

The manifest pins the application version, full Git commit, runtime and latest EF migration. Production secrets and Data Protection keys are never included.

## One-time live-server preparation

Perform these steps in a controlled window before the first immutable deployment.

### 1. Preserve production configuration

Create:

```text
D:\Deploy\KCAS\shared\appsettings.Production.json
```

If the current deployment already has an untracked `appsettings.Production.json`, copy it into this shared location. Restrict its NTFS permissions to administrators and the identity used by the `KCAS` Scheduled Task.

If all production settings are supplied through machine-level or Scheduled Task environment variables, the shared file may be omitted. Confirm that at least the following reaches the application:

```text
ConnectionStrings__DefaultConnection
```

Never commit the production connection string or password.

### 2. Preserve existing Data Protection keys

Before the first deployment, copy the contents of the current key directory, if it exists:

```text
D:\Deploy\KCAS\publish\App_Data\DataProtectionKeys
```

to:

```text
D:\Deploy\KCAS\shared\DataProtectionKeys
```

This preserves Identity cookies and protected tokens across releases. Restrict the directory to the Scheduled Task identity and administrators.

### 3. Install the deployment tools

Copy these reviewed files to `D:\Deploy\KCAS`:

```text
deploy\windows\Deploy-KCAS-Release.ps1
deploy\windows\Rollback-KCAS-Release.ps1
```

Create an inbox for downloaded release files:

```text
D:\Deploy\KCAS\inbox
```

### 4. Confirm the existing Scheduled Task

The first deployment deliberately preserves the task's existing principal, triggers and settings. Confirm:

- task name is `KCAS`;
- its principal is the expected Windows account;
- that account can read and execute under `D:\Deploy\KCAS`;
- it can modify `shared\DataProtectionKeys`;
- the existing Windows Login works before deployment;
- Apache currently proxies to `http://127.0.0.1:5143`.

The first deployment uses `-UpdateScheduledTaskAction` to replace only the task action with:

```text
Executable: D:\Deploy\KCAS\current\app\KCAS.Admin.exe
Arguments:  --urls http://127.0.0.1:5143
Working directory: D:\Deploy\KCAS\current\app
```

## Creating a release

### Manual reviewed release

1. Merge the approved pull request into `main`.
2. Open GitHub Actions.
3. Select `Windows release package`.
4. Choose `Run workflow` on `main`.
5. Optionally enter a version label.
6. Wait for the independent build, tests, migration check and packaging to pass.
7. Download the `kcas-windows-<full-sha>` artifact.

### Tagged release

Create and push an approved tag matching `v*`, for example `v1.1.0`. The same workflow runs and attaches the ZIP and checksum to the corresponding GitHub Release.

Use release tags only for commits already reviewed and merged. Do not move or reuse a production tag.

## First deployment

Copy both downloaded files to the server inbox:

```text
KCAS-<version>-win-x64.zip
KCAS-<version>-win-x64.zip.sha256
```

Open an elevated PowerShell session. Supply database deployment credentials through the process environment or secure server configuration:

```powershell
$env:KCAS_MYSQL_USER = 'approved_migration_user'
$env:KCAS_MYSQL_PASSWORD = '<enter securely>'
$env:KCAS_DATABASE = 'kcas_blazor'
```

Run:

```powershell
Set-Location D:\Deploy\KCAS

.\Deploy-KCAS-Release.ps1 `
    -PackagePath 'D:\Deploy\KCAS\inbox\KCAS-<version>-win-x64.zip' `
    -UpdateScheduledTaskAction `
    -MySqlBasePath 'D:\wamp64\bin\mysql\mysql9.1.0' `
    -ProxyHealthUrl 'https://<production-kcas-host>/health/ready'
```

Remove the process-level password afterward:

```powershell
Remove-Item Env:\KCAS_MYSQL_PASSWORD
```

The script will:

1. obtain an exclusive deployment lock;
2. verify the package SHA-256;
3. validate the release manifest and required files;
4. install the release into a new commit-specific directory;
5. connect the release to persistent Data Protection keys;
6. copy shared production configuration when present;
7. back up MySQL before stopping KCAS;
8. stop only the `KCAS` Scheduled Task;
9. apply the packaged reviewed migration script;
10. switch the `current` junction;
11. update the Scheduled Task action on the first deployment;
12. start KCAS;
13. verify direct database readiness and the optional Apache URL;
14. record the result in `shared\deployment-logs\deployments.jsonl`.

After the script succeeds, manually confirm:

- Windows Login;
- normal username/password login if used;
- client list and representative client detail;
- the release's new or changed workflow;
- Apache TLS access;
- the expected migration in `__EFMigrationsHistory`.

## Incremental legacy data import

Application deployment preserves the existing live `kcas_blazor` data. A current `kanaanclients.sql` export is a separate confidential data artifact and must never be committed or included in a release ZIP.

Transfer the fresh export to a secured server location. The operator command stages the immutable snapshot and runs the comparison in one operation:

```powershell
& 'D:\Deploy\KCAS\current\Import-KCAS-Legacy.cmd' 'D:\SecureTransfer\kanaanclients.sql'
```

If no path is supplied, the command opens a file picker. It reads the same protected `ConnectionStrings:DefaultConnection` setting as KCAS and never displays or copies the password into its state file. The configured database account must have access to `kcas_blazor` and permission to create checksum-named `kcas_legacy_stage_*` databases. Non-secret settings and the latest snapshot are remembered under `shared\legacy-import-operator`.

The command calculates SHA-256, restores the export into `kcas_legacy_stage_<hash-prefix>`, validates the required tables, scans it against KCAS, opens `/imports`, and displays the scan run number. It reuses only a staging database that has the matching manifest and does not delete staging data automatically.

If production uses an external URL, it can be saved with the first scan:

```powershell
& 'D:\Deploy\KCAS\current\Import-KCAS-Legacy.cmd' 'D:\SecureTransfer\kanaanclients.sql' `
    -ReviewUrl 'https://kcas.example/imports'
```

Review the run at `/imports`. Changed, missing, invalid, and orphaned records are review-only. `tbl_fund` and `tbl_kyc` are also review-only because legacy replacement workflows can recreate their primary IDs. To add only the remaining exact new table/legacy-ID/fingerprint combinations from that scan:

```powershell
& 'D:\Deploy\KCAS\current\Import-KCAS-Legacy.cmd' -ApplyNew <reviewed-run-id>
```

Apply mode uses the remembered immutable snapshot, creates another database backup under `shared\database-backups`, rejects mismatched provenance, applies only the scan's safe new records, and automatically performs a verification scan. Output plus `imports.jsonl` is written under `shared\legacy-import-logs`. The staging database is retained until an explicit, separately reviewed cleanup.

## Subsequent deployments

Do not pass `-UpdateScheduledTaskAction` again unless the task action has intentionally changed.

```powershell
.\Deploy-KCAS-Release.ps1 `
    -PackagePath 'D:\Deploy\KCAS\inbox\KCAS-<version>-win-x64.zip' `
    -MySqlBasePath 'D:\wamp64\bin\mysql\mysql9.1.0' `
    -ProxyHealthUrl 'https://<production-kcas-host>/health/ready'
```

The script rejects a commit already installed. Releases are immutable; rebuilds must produce a new reviewed commit.

## Rollback

### Automatic application rollback

If the new release fails its health checks and a previous version exists, deployment switches `current` back to the previous release and restarts the task.

This does not reverse the database migration.

### Manual application rollback

First inspect the migration and confirm that the current database schema remains compatible with the older application. Then identify the full commit under `releases` and run:

```powershell
.\Rollback-KCAS-Release.ps1 `
    -GitCommit '<full-40-character-commit-sha>' `
    -DatabaseIsCompatible
```

The mandatory switch is an explicit acknowledgement; it does not perform the compatibility analysis.

### Database rollback

Database rollback is never automatic. Use one of these controlled paths:

- apply a separately reviewed reverse migration where safe; or
- restore the pre-deployment backup after confirming the permitted data-loss window.

MySQL backups are stored under:

```text
D:\Deploy\KCAS\shared\database-backups
```

Migrations should use an expand-and-contract approach where practical so that the previous application remains compatible during application rollback.

## Operational rules

- Never deploy from a dirty production Git checkout.
- Never compile on the live server.
- Never use a package without its matching checksum.
- Never replace files inside an installed release directory.
- Never delete old releases until backup, audit and rollback retention requirements are agreed.
- Never commit `appsettings.Production.json`, passwords or Data Protection keys.
- Never interpret successful application rollback as successful database rollback.
- Never run `ApplyNew` without the reviewed scan ID and the same staged snapshot manifest.
- Never put a legacy SQL export into Git, a release ZIP, or deployment logs.
- Retain deployment logs with the corresponding backup, Git commit and migration.

## Later improvement: Windows Service

After several successful immutable deployments, convert the Scheduled Task to a dedicated Windows Service in a separate change. Preserve the working task as a rollback path until Windows Login, service recovery, reboot startup and Apache proxy tests pass.
