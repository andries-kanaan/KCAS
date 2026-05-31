# KCAS Blazor Rewrite Plan

Last updated: 2026-05-31

## Current Goal

Rewrite the legacy Yii1 `kanaanclients` application into the modern Blazor project in this repository (`kcas` / `KCAS.Admin`) with a corrected database design, modern authentication, role-based permissions, and a staged migration path from the legacy MySQL data.

## Current State and Next Step

- PR #6, `Add client relationship editing`, has been merged into `main`.
- The current product-development phase is `Investments Read Model`.
- The next functional goal is to review the imported investment display and then decide whether to add investment editing, fund summaries, or KYC workflow next.
- Current legacy imports are development seed data. They help design and test KCAS against realistic records, but they are disposable.
- The final production import will happen later, from the latest `kanaanclients` data, once KCAS is ready for switch-over. At that point current seed/imported data can be cleared and replaced.

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
- Legacy Yii database config:
  - `C:\wamp64\www\yii\demos\kanaanclients\protected\config\main.php`
  - Do not copy legacy database passwords into committed docs or PR descriptions.
- Legacy client workflow source:
  - Controllers: `C:\wamp64\www\yii\demos\kanaanclients\protected\controllers`
  - Models: `C:\wamp64\www\yii\demos\kanaanclients\protected\models`
  - Client screens: `C:\wamp64\www\yii\demos\kanaanclients\protected\views\client`
  - Note screens: `C:\wamp64\www\yii\demos\kanaanclients\protected\views\clientnote`
- Current domain analysis document:
  - `docs\LEGACY_DOMAIN_ANALYSIS.md`
- Legacy SQL dumps available in the app root:
  - `kanaanclients.sql`
  - `kanaanclientsmonthly.sql`
  - `kanaanclientsweekly.sql`
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
  - investment history: 6150
  - KYC records: 1089
  - users: 9

## Database Design Decision

The new Blazor database should not blindly copy the legacy schema. Where the old Yii schema has over-wide tables, duplicated concepts, weak relationships, or mixed responsibilities, the new schema should be normalized and redesigned.

Current decision:

- Continue using MySQL locally because it fits the existing WAMP setup and the current data already lives in MySQL.
- Design modern EF Core entities and migrations for the new app.
- Use current legacy imports as development seed data only.
- Migrate/import legacy data into the corrected schema through an explicit importer for testing now and for the final switch-over later.
- Preserve only enough traceability to validate mapping and reconciliation. Do not design KCAS around preserving today's seed rows permanently.
- KCAS-owned records must use KCAS-owned identifiers. Legacy row IDs must not become KCAS primary keys or KCAS business identifiers.
- Kanaan ID is a current internal administration identifier used to track family units. Multiple clients can intentionally share the same Kanaan ID when they belong to the same family unit.

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

Security/RBAC implementation completed and merged:

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
- Merged through PR #1, `feature/security-and-kanaan-branding`.

Database migration status:

- Added migration:
  - `src\KCAS.Admin\Data\Migrations\20260531080449_AddSecurityRbac.cs`
- Applied migration to local MySQL successfully.
- Migration adds approval/profile fields to `AspNetUsers` and an index for `WindowsAccountName`.

Build status:

- A build succeeded after the registration and Identity role changes.
- A test project exists under `tests\KCAS.Admin.Tests`.
- Baseline tests cover app smoke routes, static branding assets, security role seeding, stale permission cleanup, and first-user admin promotion.
- Added the first client rewrite slice and merged it through PR #3:
  - Replaced the placeholder `Clients` columns with a normalized client aggregate.
  - Added `ClientPersonalProfiles`, `ClientContactPoints`, `ClientAddresses`, `ClientRelationships`, `ClientFinancialProfiles`, and `ClientLegacySnapshots`.
  - Added a searchable client register with separate Name and Surname columns, per-column filters, clickable sort headers, and links to detail pages.
  - Added a read-only client detail page.
  - Added a console importer under `tools\KCAS.LegacyImport`.
  - Imported the local legacy `tbl_client` data into the new schema.
  - Preserved raw legacy client rows as JSON snapshots.
  - Added mapper and client-search tests.
  - Surfaced more legacy client-section fields on the detail page, including physical/postal addresses, family detail, qualifications, spouse employer/income, goals, will/bank details, and representative fields.
  - Left life and disability cover for the KYC/policy import because it is derived from the legacy `tbl_kyc` classification data, not just `tbl_client`.
  - Added read-only client notes import and display:
    - Added `ClientNotes` with legacy note IDs, final/deleted status, audit fields, and raw snapshots.
    - Extended the console importer to import `tbl_clientnote` after clients.
    - Imported 11,856 local legacy notes with 0 skipped and 0 failed.
    - Added a paged, searchable Notes section to client detail pages so large note histories do not render as one long list.
    - Deferred note create/edit/finalize/delete workflow until after read/import is reviewed.
- Added the KYC/policy seed import slice and merged it through PR #4:
  - Added `ClientKycPolicies`.
  - Imported KYC policy data from `tbl_kyc` for development review.
  - Surfaced life/disability cover and current KYC policy summaries on client detail pages.
  - Added note create/edit/finalize/delete workflow and client edit pages.
  - Added operational tests for client notes and KYC import mapping.
- Added the standalone client operations slice and merged it through PR #5:
  - Added Kanaan ID generation for native client records when left blank.
  - Preserved Kanaan ID as a shared family-unit administration identifier.
  - Added operational tests for native Kanaan ID generation and shared Kanaan IDs.
  - Added a local restart helper and verified clean legacy seed import from an empty client-domain state.
- Added the client relationship editing slice and merged it through PR #6:
  - Added relationship create/edit/delete behavior for spouse, child, dependent, family contact, and other relationship rows.
  - Kept relationship changes behind `Clients.Manage`.
  - Added focused tests for relationship operations.
- Added the legacy domain analysis slice:
  - Reviewed the Yii client, notes, KYC, investment, fund, reference data, and report workflows.
  - Documented the observed business modules in `docs\LEGACY_DOMAIN_ANALYSIS.md`.
  - Recommended investment account/history read import as the next implementation slice.
- Added the investments read model and seed import slice:
  - Added normalized investment account and investment transaction entities.
  - Added EF migration `20260531194916_AddClientInvestments`.
  - Imported `tbl_investmentaccount` and `tbl_investmenthistory` as disposable development seed data.
  - Added read-only investment account summaries and collapsible recent transaction history to client detail pages.
  - Verified local import of 1,524 investment accounts and 6,150 investment history rows with 0 skipped and 0 failed.

## Current Verification Status

The previous WAMP `503` was traced to launching Kestrel with the wrong working directory and without the Development environment. In that launch mode the app could not load the local development connection string.

Correct local startup requirements:

- Working directory must be `src\KCAS.Admin`.
- `ASPNETCORE_ENVIRONMENT` must be `Development` for the ignored local database password to load.
- `Start-KCAS.ps1` already follows this pattern.
- `Restart-KCAS.ps1` stops only the KCAS Kestrel process, rebuilds, restarts it with the correct Development environment, and verifies both Kestrel and the WAMP HTTPS proxy.

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

1. Verify security foundation manually.
   - Manually verify first-user promotion, approval workflow, and Windows login behavior in the browser.
   - Keep automated security seed and smoke tests in place.

2. Client Operations v1.
   - Create and edit normalized client records in KCAS.
   - Generate Kanaan IDs for new records when one is not entered manually.
   - Manage client contact points and addresses from the operational UI.
   - Manage spouse, child, dependent, family contact, and other relationship rows from the operational UI.
   - Add, edit, finalize, and soft-delete KCAS client notes.
   - Treat current imported rows as disposable development seed data, not as permanent production data.
   - Keep import traceability only for mapping checks and final-import reconciliation.
   - Enforce `Clients.Manage` and `Notes.Manage` for operational changes.
   - Status: core v1 client operations are implemented; remaining work here is acceptance review and refinements found during use.

3. Legacy domain analysis.
   - Document current Yii models, controllers, workflows, and screen behavior.
   - Identify actual business concepts behind legacy table/field names.
   - Decide which legacy screens should be rebuilt first.
   - Status: first pass documented in `docs\LEGACY_DOMAIN_ANALYSIS.md`.

4. Investments read model and seed import.
   - Design normalized EF Core entities for investment accounts and investment history.
   - Import `tbl_investmentaccount` and `tbl_investmenthistory` as disposable development seed data.
   - Add read-only investment account/history display to client detail pages.
   - Preserve legacy traceability for reconciliation without making legacy row IDs KCAS identifiers.
   - Add mapper/import tests using realistic legacy rows.
   - Status: implemented; remaining work here is review of the display and follow-up refinement.

5. New schema design.
   - Design normalized EF Core entities for clients, contacts, notes, products, investments, KYC, documents, and reporting.
   - Break up over-wide legacy structures where needed.
   - Add explicit relationships, constraints, indexes, audit fields, and import traceability.

6. Continue the client workflow.
   - Review imported client detail fields against the old Yii screens.
   - Review imported notes display against the old Yii notes grid.
   - Refine client operations after acceptance review.
   - Expand KYC/policy workflow beyond the current imported summaries.

7. Expand functional modules.
   - KYC and recommendations.
   - Funds/products/reference data.
   - Reports.
   - Administration/security.

8. Production readiness.
   - Decide final hosting target.
   - Add CI/CD using GitHub once the remote is created.
   - Use `dotnet publish` in the deployment pipeline.
   - Move secrets to environment variables or hosting secret storage.
   - Add backup/restore and migration procedures.

## Important Resume Notes

- Do not revert unrelated working tree changes unless the user explicitly asks.
- Before continuing feature work, inspect `git status --short --branch`.
- Start new work from clean `main` and create a feature branch first.
- Use `feature/investment-read-model` for the next recommended domain slice unless the user chooses a different priority.
- Do not commit local secrets, logs, build output, or generated `artifacts/`.
- Before committing feature work, run:
  - `.\.dotnet\dotnet.exe build KCAS.slnx`
  - `.\.dotnet\dotnet.exe test tests\KCAS.Admin.Tests\KCAS.Admin.Tests.csproj`
  - `.\.dotnet\dotnet.exe ef migrations has-pending-model-changes --project src\KCAS.Admin\KCAS.Admin.csproj`
- If Kestrel was stopped to release locked build outputs, run `.\Restart-KCAS.ps1` before handing the app back to the user.
- Before final handoff after local app work, verify `https://kcas.test:8443/clients` returns the app/login page instead of Apache `503`.
- If the app fails at startup, capture logs before changing more code.
- Avoid exposing database passwords in committed files or final responses.
