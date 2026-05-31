# KCAS Blazor Rewrite Plan

Last updated: 2026-05-31

## Current Goal

Rewrite the legacy Yii1 `kanaanclients` application into the modern Blazor project in this repository (`kcas` / `KCAS.Admin`) with a corrected database design, modern authentication, role-based permissions, and a staged migration path from the legacy MySQL data.

## Local Development Setup

- Repository: `C:\Users\andriesvt\OneDrive\KCAS`
- Blazor project: `src\KCAS.Admin`
- Local Kestrel backend: `http://127.0.0.1:5143`
- WAMP/Apache local test site:
  - `http://kcas.test:8080`
  - `https://kcas.test:8443`
- Hosts entry already configured by user:
  - `127.0.0.1 kcas.test`
- Apache vhost file:
  - `C:\wamp64\bin\apache\apache2.4.62.1\conf\extra\httpd-vhosts.conf`
- Professional local pattern chosen:
  - Keep Kestrel as the .NET app server.
  - Use WAMP/Apache as the local reverse proxy for the friendly `kcas.test` host and TLS behavior.
  - This mirrors production more closely than trying to make Apache directly execute the Blazor Server app.

## Deployment Direction

- Use Git/GitHub for source control and collaboration.
- Use `dotnet publish` for actual deployable build output.
- GitHub is not a replacement for `dotnet publish`; GitHub can store source and can later run CI/CD that performs `dotnet publish`.
- GitHub remote is `https://github.com/andries-kanaan/KCAS.git`.
- Pull requests to `main` should pass the GitHub Actions PR checks before merge.
- The PR check workflow builds the solution and runs the test suite against an isolated MySQL service database.
- Local integration tests use `kcas_blazor_test` and recreate it for deterministic test runs.

## Legacy Yii1 App

- Path: `C:\wamp64\www\yii\demos\kanaanclients`
- Legacy database: MySQL `kanaanclients`
- Legacy tables identified:
  - `tbl_client`
  - `tbl_clientnote`
  - `tbl_companyproduct`
  - `tbl_feed`
  - `tbl_feedtopic`
  - `tbl_finplanpar`
  - `tbl_fund`
  - `tbl_fundname`
  - `tbl_investmentaccount`
  - `tbl_investmenthistory`
  - `tbl_kyc`
  - `tbl_kyc_recommend`
  - `tbl_lispname`
  - `tbl_mainclass`
  - `tbl_miscinfo`
  - `tbl_subclass`
  - `tbl_user`
- Approximate legacy row counts already found:
  - clients: 453
  - notes: 11670
  - funds: 710
  - investment accounts: 1524
  - investment history: 6156
  - KYC records: 1089
  - users: 9

## Database Design Decision

The new Blazor database should not blindly copy the legacy schema. Where the old Yii schema has over-wide tables, duplicated concepts, weak relationships, or mixed responsibilities, the new schema should be normalized and redesigned.

Current decision:

- Continue using MySQL locally because it fits the existing WAMP setup and the current data already lives in MySQL.
- Design modern EF Core entities and migrations for the new app.
- Migrate/import legacy data into the corrected schema through an explicit importer later.
- Preserve traceability to legacy IDs during migration with legacy reference columns or import mapping tables where useful.

## Security/RBAC Plan

Security is being implemented first because the rest of the app should be built behind proper authorization.

Authentication approach:

- Email/password login remains available.
- Windows/Negotiate login is added for local/intranet-style convenience.
- Old Yii users are ignored and will not be imported as active application users.
- A Windows login that does not match an existing user creates a pending account with no rights.
- An administrator must approve users and assign roles.

Authorization approach:

- Use ASP.NET Core Identity users, roles, and claims.
- Use role claims for permissions.
- Do not build custom RBAC tables unless the built-in Identity model becomes insufficient.

Planned roles:

- `Administrator`
- `Advisor`
- `Operations`
- `Reports`
- `ReadOnly`

Initial permissions:

- `Clients.View`
- `Clients.Manage`
- `Notes.Manage`
- `Investments.View`
- `Investments.Manage`
- `Kyc.View`
- `Kyc.Manage`
- `Reports.View`
- `Security.Manage`

## Work Completed So Far

Project and infrastructure:

- Created/initialized the Blazor app structure in `src\KCAS.Admin`.
- Configured local MySQL connection via appsettings.
- Kept real local secrets in ignored `appsettings.Development.json`.
- Committed the initial project as:
  - `9bb1206 Initial KCAS Blazor admin project`
- Configured WAMP/Apache reverse proxy for `kcas.test`.
- Fixed/commented broken Apache vhost entries found during validation.
- WAMP restart was performed by the user and the proxy setup appeared to work.
- Added `Start-KCAS.ps1` for local startup.

Security implementation already edited into the working tree:

- Added `Microsoft.AspNetCore.Authentication.Negotiate`.
- Expanded `ApplicationUser` with:
  - `DisplayName`
  - `WindowsAccountName`
  - `IsApproved`
  - `CreatedAtUtc`
  - `ApprovedAtUtc`
  - `ApprovedByUserId`
- Changed the EF Identity context to support `IdentityRole`.
- Added security constants and seeder under `src\KCAS.Admin\Security`.
- Added role/permission seeding.
- Added first-user promotion to approved administrator.
- Added `/Account/WindowsLogin` endpoint.
- Added pending approval page.
- Added security admin page for approving users and changing roles.
- Added policy protection to the clients page.
- Added Security nav item visible only to users with `Security.Manage`.
- Added supporting CSS.

Database migration status:

- Added migration:
  - `src\KCAS.Admin\Data\Migrations\20260531080449_AddSecurityRbac.cs`
- Applied migration to local MySQL successfully.
- Migration adds approval/profile fields to `AspNetUsers` and an index for `WindowsAccountName`.

Build status:

- A build succeeded after the registration and Identity role changes.
- A test project exists under `tests\KCAS.Admin.Tests`.
- Baseline tests cover app smoke routes, static branding assets, security role seeding, stale permission cleanup, and first-user admin promotion.
- Added the first client rewrite slice:
  - Replaced the placeholder `Clients` columns with a normalized client aggregate.
  - Added `ClientPersonalProfiles`, `ClientContactPoints`, `ClientAddresses`, `ClientRelationships`, `ClientFinancialProfiles`, and `ClientLegacySnapshots`.
  - Added a searchable client register and read-only client detail page.
  - Added a console importer under `tools\KCAS.LegacyImport`.
  - Imported the local legacy `tbl_client` data into the new schema.
  - Preserved raw legacy client rows as JSON snapshots.
  - Added mapper and client-search tests.
  - Added per-column client register filters and clickable sort headers.
  - Surfaced more legacy client-section fields on the detail page, including physical/postal addresses, family detail, qualifications, spouse employer/income, goals, will/bank details, and representative fields.
  - Left life and disability cover for the KYC/policy import because it is derived from the legacy `tbl_kyc` classification data, not just `tbl_client`.
  - Added read-only client notes import and display:
    - Added `ClientNotes` with legacy note IDs, final/deleted status, audit fields, and raw snapshots.
    - Extended the console importer to import `tbl_clientnote` after clients.
    - Imported 11,856 local legacy notes with 0 skipped and 0 failed.
    - Added a Notes section to client detail pages.
    - Deferred note create/edit/finalize/delete workflow until after read/import is reviewed.

## Current Verification Status

The previous WAMP `503` was traced to launching Kestrel with the wrong working directory and without the Development environment. In that launch mode the app could not load the local development connection string.

Correct local startup requirements:

- Working directory must be `src\KCAS.Admin`.
- `ASPNETCORE_ENVIRONMENT` must be `Development` for the ignored local database password to load.
- `Start-KCAS.ps1` already follows this pattern.

Manual command from repo root:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
Start-Process -FilePath "C:\Users\andriesvt\OneDrive\KCAS\.dotnet\dotnet.exe" -ArgumentList "bin\Debug\net10.0\KCAS.Admin.dll","--urls","http://127.0.0.1:5143" -WorkingDirectory "C:\Users\andriesvt\OneDrive\KCAS\src\KCAS.Admin"
```

Verified after the security seed fix:

- `dotnet build` succeeds.
- Kestrel starts on `http://127.0.0.1:5143`.
- WAMP HTTPS proxy reaches the app at `https://kcas.test:8443`.
- `https://kcas.test:8443/clients` redirects unauthenticated users to login.
- Apache HTTP vhost redirects `http://kcas.test:8080/security` to HTTPS.
- Roles and permission claims are seeded.
- Administrator role has all 9 planned permissions.
- Seeder removes stale KCAS permission claims when permission names change.

Still to verify manually in browser:

- First local registered user becomes approved Administrator.
- Pending users see `/Account/PendingApproval`.
- `/security` can approve users and assign roles.
- `/Account/WindowsLogin` works in the browser/WAMP environment.

## Remaining Rewrite Phases

1. Finish security foundation.
   - Commit the security foundation.
   - Manually verify first-user promotion, approval workflow, and Windows login behavior in the browser.

2. Legacy domain analysis.
   - Document current Yii models, controllers, workflows, and screen behavior.
   - Identify actual business concepts behind legacy table/field names.
   - Decide which legacy screens should be rebuilt first.

3. New schema design.
   - Design normalized EF Core entities for clients, contacts, notes, products, investments, KYC, documents, and reporting.
   - Break up over-wide legacy structures where needed.
   - Add explicit relationships, constraints, indexes, audit fields, and import traceability.

4. Continue the client workflow.
   - Review imported client detail fields against the old Yii screens.
   - Review imported notes display against the old Yii notes grid.
   - Add basic client edit screens only after confirming the normalized imported view is correct.
   - Add note create/edit/finalize/delete after the read-only note import is accepted.
   - Import KYC/policy data to populate life and disability cover and related calculated planning sections.

5. Expand functional modules.
   - Investments/accounts/history.
   - KYC and recommendations.
   - Funds/products/reference data.
   - Reports.
   - Administration/security.

6. Production readiness.
   - Decide final hosting target.
   - Add CI/CD using GitHub once the remote is created.
   - Use `dotnet publish` in the deployment pipeline.
   - Move secrets to environment variables or hosting secret storage.
   - Add backup/restore and migration procedures.

## Important Resume Notes

- Do not revert unrelated working tree changes unless the user explicitly asks.
- Before continuing feature work, inspect `git status --short`.
- The current uncommitted work is the security/RBAC implementation.
- If the app fails at startup, capture logs before changing more code.
- Avoid exposing database passwords in committed files or final responses.
