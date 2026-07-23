# KCAS Blazor Rewrite Plan

Last updated: 2026-07-22

The authoritative staged roadmap for client risk evaluation, the Business Risk Assessment, the RMCP and inspection readiness is [RMCP_BRA_IMPLEMENTATION_PLAN.md](RMCP_BRA_IMPLEMENTATION_PLAN.md).

## Current Goal

Rewrite the legacy Yii1 `kanaanclients` application into the modern Blazor project in this repository (`kcas` / `KCAS.Admin`) with a corrected database design, modern authentication, role-based permissions, and a staged migration path from the legacy MySQL data.

## Current State and Next Step

- PR #14, `Fix client list filters`, has been merged into `main`.
- The database deployment schema fix and client notes shortcut have been merged through PR #13.
- Core v1 operational workflows are implemented for clients, notes, relationships, KYC policies, KYC recommendations, investment accounts, investment transactions, fund summary review, and KYC copy/transfer.
- Stable non-client Yii reference tables are carried forward as modern KCAS reference data, not as Yii table clones.
- `tbl_feed` and `tbl_feedtopic` are intentionally excluded because they were an abandoned correspondence experiment.
- The current product phase is acceptance review and production hardening, not another foundational rewrite slice.
- The next functional goal is browser acceptance review of client operations, investment account/transaction workflows, fund summaries, KYC recommendations, KYC copy/transfer, and security/admin flows against the deployed production-style environment.
- Existing legacy imports are a historical data baseline, not disposable seed data.
- Later legacy refreshes use scan-first incremental reconciliation: new legacy IDs can be added, identical rows are skipped, and changed or missing rows are staged for review without silently overwriting KCAS work.

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
- GitHub Actions runs `dotnet publish` and produces the immutable, framework-dependent `win-x64` release; the live server uses its existing repository-local .NET host and does not compile.
- GitHub remote is `https://github.com/andries-kanaan/KCAS.git`.
- Pull requests to `main` should pass the GitHub Actions PR checks before merge.
- The PR check workflow builds the solution and runs the test suite against an isolated MySQL service database.
- Local integration tests use `kcas_blazor_test` and recreate it for deterministic test runs.
- Production deployment direction is explicit and documented in `docs/WINDOWS_DEPLOYMENT.md`:
  - Deploy a checksum-verified package tied to one full Git commit.
  - Install releases in versioned directories and switch the `current` junction only after backup and migration controls.
  - Run the published app through Kestrel.
  - Use Apache/WAMP as the reverse proxy.
  - Preserve the existing Scheduled Task identity for the first transition, then consider Windows Service conversion separately.
  - Keep production secrets out of the repository, preferably in environment variables or non-committed production configuration.

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
- Preserve existing imported data as the historical baseline and refresh it through explicit, repeatable reconciliation runs.
- Match by stable legacy primary ID; automatically add only new IDs; require review before any changed legacy value can replace an existing KCAS value.
- Preserve raw source snapshots, fingerprints, field-level differences and import decisions so every refresh is auditable and idempotent.
- KCAS-owned records must use KCAS-owned identifiers. Legacy row IDs must not become KCAS primary keys or KCAS business identifiers.
- Kanaan ID is a current internal administration identifier used to track family units. Multiple clients can intentionally share the same Kanaan ID when they belong to the same family unit.
- Remaining non-client Yii tables are explicitly classified:
  - Stable reference data: `tbl_companyproduct`, `tbl_lispname`, `tbl_fundname`, `tbl_mainclass`, `tbl_subclass`, `tbl_miscinfo`.
  - Excluded/obsolete: `tbl_feed`, `tbl_feedtopic`, `tbl_user`, `tbl_finplanpar`, `tbl_kyc_recommend`.
  - Calculation-only: capital-gain behavior should be rebuilt as Blazor calculation/report logic if needed later.

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
    - Initially deferred note create/edit/finalize/delete workflow until after read/import review; that workflow was later implemented in the KYC/policy seed import slice.
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
  - Imported `tbl_investmentaccount` and `tbl_investmenthistory` as the initial historical baseline.
  - Added read-only investment account summaries and collapsible recent transaction history to client detail pages.
  - Verified local import of 1,524 investment accounts and 6,150 investment history rows with 0 skipped and 0 failed.
- Added the fund current-value seed import refinement:
  - Added `ClientFundValuations` for current values from legacy `tbl_fund`.
  - Added EF migration `20260531204111_AddClientFundValuations`.
  - Imported 710 local legacy fund valuation rows with 0 skipped and 0 failed.
  - Updated the client Investments section to prefer matched fund current values by account number, falling back to the latest captured investment-history balance where no fund value is available.
  - Kept full fund summary reports, fee calculations, and report exports deferred; this slice only brings current values into the client investment review surface.
- Added the investment display review refinement:
  - Added a client investment summary strip with total current value, valuation-matched account count, history-fallback account count, unmatched fund valuation count, and latest fund valuation date.
  - Kept the refinement read-only; no schema or import behavior changed.
- Added the native KYC policy workflow slice:
  - Made `ClientKycPolicy.LegacyKycId` nullable so KCAS-created policy rows can coexist with imported legacy seed rows.
  - Added native KYC policy create/edit/delete behavior behind `Kyc.Manage`.
  - Allowed imported KYC rows to be edited while retaining `LegacyKycId` as traceability metadata; later source changes now require reconciliation rather than unconditional replacement.
  - Added an operational KYC policy edit page and client detail actions for native rows.
  - Added focused tests for native and imported KYC policy create/edit/delete behavior.
- Added the outstanding workflow completion slice:
  - Added investment account create/edit/delete behavior behind `Investments.Manage`.
  - Added investment transaction create/edit/finalize/soft-delete behavior behind `Investments.Manage`.
  - Made investment legacy IDs nullable so KCAS-native investment rows can coexist with imported seed rows.
  - Added fund summary review page with matched valuations, history fallback balances, unmatched valuations, filtering, and totals.
  - Added KYC recommendations and KYC copy/transfer behavior behind `Kyc.Manage`.
  - Added focused service tests for investment editing, fund summaries, recommendations, and KYC copy/transfer.
- Added the KCAS favicon update and merged it through PR #12:
  - Replaced the default Blazor favicon with the KCAS `K` icon.
  - Added a favicon cache-busting query string.
- Added the production database schema script fix:
  - Replaced the stale first-migration-only `kcas_blazor_initial.sql` with `kcas_blazor_schema.sql`.
  - Updated `Apply-KCAS-Database.ps1` to use the full fresh-database schema script.
  - Added guardrails so the fresh schema script is not applied over a partially migrated or non-empty database.
  - Updated README database deployment guidance.
  - Verified the full schema script against a temporary MySQL database.
- Added the reference-data closure slice:
  - Added modern reference tables for investment administrators/platforms, funds, product types, KYC main classes, KYC sub classes, and market/reference values.
  - Extended the legacy importer to upsert stable reference data before client-linked operational data.
  - Updated KYC and investment edit screens to use reference autocomplete choices while preserving historical text values.
  - Explicitly excluded abandoned Yii feed tables, legacy users, empty planned tables, and capital-gain table concepts from the Blazor data model.

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

Verified locally after the security, import, compliance foundation and client evidence-readiness fixes:

- `dotnet build` succeeds.
- `dotnet test tests\KCAS.Admin.Tests\KCAS.Admin.Tests.csproj` passes.
- `dotnet ef migrations has-pending-model-changes` reports no pending model changes.
- `src\KCAS.Admin\Data\Migrations\kcas_blazor_schema.sql` applies successfully to an empty temporary MySQL database and should record latest migration `20260723130255_AddClientEvidenceCategories`.
- Kestrel starts on `http://127.0.0.1:5143`.
- WAMP HTTPS proxy reaches the app at `https://kcas.test:8443`.
- `https://kcas.test:8443/clients` redirects unauthenticated users to login.
- Apache HTTP vhost redirects `http://kcas.test:8080/security` to HTTPS.
- Roles and permission claims are seeded.
- Administrator role has the full current permission set, including import view/review/apply/admin permissions.
- Seeder removes stale KCAS permission claims when permission names change.
- Approved users land on `/clients` after login when they have `Clients.View`; otherwise they land on `/`.
- Compliance foundation routes, permissions, service workflow and audit events are covered by automated tests.
- Client evidence readiness, server-folder scanner idempotence, unmatched-file handling, evidence verification and exception logic are covered by automated tests.

Still to verify manually in browser:

- First local or production registered user becomes approved Administrator on a clean database.
- Pending users see `/Account/PendingApproval`.
- `/security` can approve users and assign roles in the deployed browser environment.
- `/Account/WindowsLogin` works in the browser/WAMP and production reverse-proxy environments.
- `/imports` supports scan review decisions and baseline reset only when `LegacyImport:AllowResetImportedData` is enabled.
- `/compliance` and `/compliance/settings` support the full Phase 1 controlled configuration workflow in the deployed browser environment.
- `/compliance/client-evidence` can configure and scan the live server client document root.
- `/clients/{id}/evidence` supports per-client evidence readiness review.
- Client create/edit, contact/address editing, relationship editing, note create/edit/finalize/delete.
- Investment account create/edit/delete and transaction create/edit/finalize/delete.
- Fund summary filtering/totals and unmatched valuation handling.
- KYC policy create/edit/delete, KYC recommendation create/edit/delete, and KYC copy/transfer.

## Remaining Rewrite Phases

1. Browser acceptance review.
   - Verify security flows: first-user promotion, pending approval, role assignment, and Windows login.
   - Verify client operations: client create/edit, contact/address editing, relationship editing, and note create/edit/finalize/delete.
   - Verify investment operations: account create/edit/delete and transaction create/edit/finalize/delete.
   - Verify fund summary review: matched valuations, history fallback balances, unmatched valuations, filtering, and totals.
   - Verify KYC operations: policy create/edit/delete, recommendation create/edit/delete, and copy/transfer.
   - Record refinements found during real browser use as small follow-up slices.

2. Production hardening.
   - Roll out and accept the immutable Windows release process in `docs/WINDOWS_DEPLOYMENT.md`.
   - After several accepted deployments, standardize startup as a Windows Service in a separate change.
   - Keep Apache/WAMP as the reverse proxy to Kestrel.
   - Move production secrets to environment variables or protected server-local configuration.
   - Keep `Database:MigrateOnStartup` disabled for controlled production migration windows.
   - Add backup/restore procedure and a repeatable migration procedure for existing databases.

3. Phase 0 operational acceptance and final production data switch-over.
   - Take a fresh backup/export of the latest legacy `kanaanclients` database.
   - Run a scan-first reconciliation against the latest legacy data.
   - Review run totals and differences, back up KCAS, then apply new legacy IDs only.
   - Resolve representative changed and missing rows with retain KCAS, apply incoming, manual, defer and reject actions.
   - Reconcile counts and spot-check representative clients, notes, KYC, investments, fund valuations, and reference choices.

4. Richer domain modules.
   - Accept the Phase 1 compliance foundation workflow and Phase 2 client evidence readiness workflow in browser before building full client risk assessments.
   - Add reference-data administration screens only if operational users need to maintain values inside KCAS.
   - Build report/export workflows.
   - Continue administration/security refinements after acceptance review.

## Important Resume Notes

- Do not revert unrelated working tree changes unless the user explicitly asks.
- Before continuing feature work, inspect `git status --short --branch`.
- Start new feature work from clean, up-to-date `main` and create a feature branch first.
- The old `feature/investment-read-model` recommendation is complete and no longer the next domain slice.
- Do not commit local secrets, logs, build output, or generated `artifacts/`.
- Before committing feature work, run:
  - `.\.dotnet\dotnet.exe build KCAS.slnx`
  - `.\.dotnet\dotnet.exe test tests\KCAS.Admin.Tests\KCAS.Admin.Tests.csproj`
  - `.\.dotnet\dotnet.exe tool run dotnet-ef migrations has-pending-model-changes --project src\KCAS.Admin\KCAS.Admin.csproj --startup-project src\KCAS.Admin\KCAS.Admin.csproj`
- If Kestrel was stopped to release locked build outputs, run `.\Restart-KCAS.ps1` before handing the app back to the user.
- Before final handoff after local app work, verify `https://kcas.test:8443/clients` returns the app/login page instead of Apache `503`.
- If the app fails at startup, capture logs before changing more code.
- Avoid exposing database passwords in committed files or final responses.
