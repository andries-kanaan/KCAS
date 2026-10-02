# KCAS Operations and Recovery Guide

Status: working guide, not yet proven by an independent recovery exercise
Owner: Kanaan's authorised KCAS operating function
Scope: local development, reviewed production releases, service interruption, data recovery and future maintenance

## Purpose and authority

This guide helps an authorised operator keep KCAS usable and restore its client and compliance functions. It is the KCAS-specific operating guide intended for reference by the Business Continuity Plan (BCP) and Disaster Recovery Plan (DRP). Those plans set business priorities and authorise activation; this guide covers the application and its data. It is also written so an operator with basic Codex skills can ask Codex to inspect the repository, follow its tested scripts and report verifiable results.

Codex is an assistant, not the source of authority. It cannot grant access, assume a backup is good, invent a password, approve a data-loss window or declare a restore successful without checks. An authorised person decides which environment and recovery point may be used. Keep an accessible copy of this guide outside the KCAS server; the repository copy alone is not available if the server and internet are both down.

**Source of truth:** for commands and deployment mechanics, read the current scripts and [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md) at the deployed Git commit. This guide gives the operating sequence, not a replacement for the scripts' current parameters. If they disagree, stop and reconcile before changing production.

Suggested BCP/DRP reference: "For KCAS operation, technical recovery, verification and Codex-assisted handoff, use the current *KCAS Operations and Recovery Guide* in the KCAS repository (`docs/KCAS_OPERATIONS_AND_RECOVERY_GUIDE.md`) and its separately held continuity copy."

## 1. What must work

| Function to restore | Minimum observable result |
| --- | --- |
| Authorised access | Expected users can sign in with their assigned permissions; unauthorised access is not broadened. |
| Client and investment work | A representative client can be found and its current investments and source records are coherent. |
| Compliance work | Existing client assessments, evidence links, screening records and audit history are accessible. |
| Advice and controlled documents | Existing advice records and a representative controlled document open. |
| Document evidence | A representative client evidence file opens from its mapped server folder; a database restore alone is insufficient. |
| Data updates | An import or transfer is performed only through its reviewed workflow and preserves KCAS-specific changes. |

If KCAS is unavailable, the BCP governs approved temporary work through product-provider channels, email and controlled logs. Reconcile that work into KCAS after service returns. Do not treat temporary records as a reason to overwrite later KCAS data.

## 2. System map and access

| Component | Known arrangement | Confirm before use |
| --- | --- | --- |
| Source | This Git repository, with `main` as reviewed deployment branch; GitHub Actions build and test releases. | `git remote -v`, branch, commit and release manifest. Do not assume a workstation checkout is production. |
| Local development | `C:\wamp64\www\KCAS`; `.\Restart-KCAS.ps1` builds, restarts and checks direct and proxied login endpoints. | Local SDK, WAMP/MySQL, working tree and URL `https://kcas.test:8443/`. |
| Production application | `D:\Deploy\KCAS`; the `KCAS` Scheduled Task runs the active immutable release through `publish` -> `current\app`. | Task identity, task action, active junction and `current\deployment-manifest.json`. |
| Production web path | Apache/WAMP TLS reverse proxy -> Kestrel, normally `http://127.0.0.1:5000`; external name normally `https://kcas:8443/`. | Actual proxy name, certificate and both health paths. |
| Database | MySQL database `kcas_blazor`, outside the immutable release. | Current host, schema, migration history, last verified backup and permitted recovery point. |
| Production settings | `D:\Deploy\KCAS\shared\appsettings.Production.json` or protected environment variables. | Actual source of settings; never print a connection string or password. |
| Data Protection keys | `D:\Deploy\KCAS\shared\DataProtectionKeys`, linked into the active release. | Keys exist, are backed up and have correct task-identity permissions. |
| Client documents | Server-side client root and KCAS evidence mappings, outside Git and the release. | Actual configured root and representative file availability. Do not assume workstation `Z:` equals server `E:` or local `C:`. |
| Review packages and logs | `D:\Deploy\KCAS\shared\client-review-packages`; deployment logs under `shared\deployment-logs`. | Current configured storage root, retention and access. Package passphrases are not stored by KCAS. |
| Database backups | `D:\Deploy\KCAS\shared\database-backups` is the deployment-script backup location. | Also identify any independent backup layers, their scope, age and verified restore results. |

The current scripts use a framework-dependent .NET release, MySQL, Windows Authentication/Identity and filesystem-backed Data Protection keys. Consult `src/KCAS.Admin/Program.cs` and the current configuration for any changed dependencies. The code repository, MySQL backup, Data Protection keys and client-document backup are **different recovery assets**; possession of one does not imply possession of the others.

Access to GitHub, Windows, MySQL, backup media, certificates and any encryption passphrases belongs in a controlled credential/access system. This guide deliberately contains no secrets. Record where an authorised substitute can request access, not the secret itself. If access is absent, use the BCP's temporary business process and escalate; do not bypass permissions.

## 3. First response to an interruption

1. Identify the affected **function**: login, web access, database, client documents, import/transfer, or a wider office/network outage. Record detection time and last known good use.
2. Preserve logs, relevant alerts and existing backup copies. If compromise or corruption is plausible, isolate affected systems and follow the Cybersecurity Strategy before restart or restore. Do not attach recovery media to a suspect system without an approved approach.
3. Check direct service health at `/health/ready`, then the Apache/TLS path. A reachable login page is not proof that data and documents work.
4. Check the `KCAS` Scheduled Task and the active release manifest. A web-proxy failure, stopped task, database failure and missing evidence folder have different remedies.
5. Start the BCP's temporary record of urgent client work and deadlines if the interruption affects a business function. Record who authorised actions and which provider confirmations will later be reconciled.
6. Decide whether this is a routine restart, a reviewed application release/rollback, or a controlled data restore. Record the decision and permitted recovery point. Do not try all three at once.

## 4. Routine operation and controlled changes

### Local development

At the local repository root, `.\Start-KCAS.ps1` builds and runs KCAS in the current shell; `.\Restart-KCAS.ps1` builds, restarts a hidden local process and checks `http://127.0.0.1:5143/Account/Login` plus `https://kcas.test:8443/Account/Login`. The local script is **not** the production restart procedure. Before local database-affecting work or GitHub mutation, follow `AGENTS.md`, including the read-only database inventory.

### Production deployment

Normal reviewed code deployment is `D:\Deploy\KCAS\Deploy-KCAS.bat`, run by an authorised server operator. The one-click launcher requires a clean server checkout on `main`, fast-forwards to reviewed `main`, obtains the corresponding tested immutable release, checks its checksum, backs up MySQL, applies packaged reviewed migrations, switches the release, restarts the existing Scheduled Task and performs health checks. See [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md) for prerequisites and exceptions.

Before deployment, confirm the approved change, relevant backup, maintenance window and rollback compatibility. After deployment, record the commit and migration, then test login, a client record, an evidence file and the changed workflow. Do not compile on the live server, edit files inside an installed release or deploy from a dirty checkout. A failed deployment may automatically roll back the **application**, not the database migration.

### Legacy SQL and reviewed transfer packages

A `kanaanclients.sql` export is a confidential source snapshot, **not** a replacement for the KCAS database. Use the staged comparison/import workflow documented in [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md) and review `/imports`; do not restore the legacy SQL over `kcas_blazor`. KCAS-specific assessments and corrections must remain intact. Client-review, duplicate, programme and advice transfers have separate package validation and permissions; preview and resolve blockers before applying. Preserve source package, result, passphrase channel and audit record according to Kanaan's retention controls.

## 5. Recovery decision and sequence

### A. Service or proxy interruption; data appears intact

Check direct `/health/ready`, proxy/TLS, Scheduled Task state and recent deployment logs. If the application is stopped and there is no compromise or migration uncertainty, an authorised operator may restart the existing `KCAS` Scheduled Task using the established server procedure. Record why the restart was appropriate and test the functions in section 6. Do not run the **local** `Restart-KCAS.ps1` on production.

### B. New release fails; database remains compatible

Inspect `D:\Deploy\KCAS\shared\deployment-logs\deployments.jsonl` and `current\deployment-manifest.json`. The deployment engine may already have switched back to the previous release. If a manual application rollback is required, review the migration and schema compatibility first. Only then use `Rollback-KCAS-Release.ps1` with the full previous commit and its explicit `-DatabaseIsCompatible` acknowledgement as detailed in [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md). That switch does not prove compatibility. Verify the selected release, task health and section 6 functions afterward.

### C. Suspected database or document corruption/loss

This is a controlled recovery, not a normal restart. Identify which asset failed: MySQL, client files, production configuration, Data Protection keys, or several together. Inventory and preserve all candidate copies **before** restoring. For MySQL, record backup provenance, backup time, schema/migration version and the expected data gap. For documents, record the source root, backup time and mapping to KCAS evidence paths. Ask the business owner to authorise the recovery point and treatment of work performed after it.

Restore into an isolated **test environment first** using approved MySQL and filesystem procedures. The repository does not currently provide a single verified end-to-end command that restores the database, all client files, settings and keys. Therefore do not improvise a production overwrite or claim this guide alone proves recoverability. Compare table/migration state, representative clients, investments, reviews, advice and evidence files. Reconcile post-backup instructions and source records. Only after those checks, authorisation and a recorded cutover plan should production be changed. Keep the previous state until the new state is verified and retention rules permit disposal.

### D. GitHub or Codex unavailable

The active immutable release can continue without GitHub. Use the local deployment logs, release manifest, protected configuration and independent copies of this guide to diagnose. Do not install an unverified package. A skilled Windows/MySQL operator can follow the reviewed scripts and [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md) without Codex; if they cannot, keep the BCP's temporary business functions running and obtain qualified support.

## 6. Acceptance checks and recovery evidence

Record results, not just “system up”. The operator must verify:

1. Active commit/release manifest, Windows task and both direct and proxied `/health/ready` return the expected result.
2. An authorised user signs in; role restrictions still apply. If Data Protection keys changed, document the resulting session/token effects.
3. A representative client, its investment position and a historical transaction/valuation display correctly.
4. A representative completed compliance assessment, its screening proof and audit history open without alteration.
5. A representative evidence file opens from the production server root. Verify another client folder if the incident involved document mapping.
6. An existing advice case and an appropriate controlled document open.
7. MySQL `__EFMigrationsHistory`, application release and restored backup provenance agree. Compare a pre-agreed sample or counts; do not treat counts alone as proof of integrity.
8. Work received after the recovery point is accounted for in a reconciliation log with outstanding items, owners and evidence.
9. The business owner confirms the affected client/compliance functions are usable; unresolved exceptions are recorded before stand-down.

Retain an incident/recovery record with detection and classification times, affected functions, approved recovery point, backups and hashes where available, deployed commit, migration version, actions, test results, data gap, reconciliation, communications and approval to return to service. Material IT/cyber or personal-information notification is assessed under the Cybersecurity Strategy and POPIA policy; a routine outage is not automatically a reportable incident.

## 7. How to use Codex safely

Give Codex the repository location, the **environment** (local, test, or production), the affected function, the permitted scope and the latest observed error. Ask it first to read this guide, `AGENTS.md`, [WINDOWS_DEPLOYMENT.md](WINDOWS_DEPLOYMENT.md), the relevant scripts and the current release manifest. It should distinguish facts observed from hypotheses and ask for approval before irreversible production or data operations. Never paste passwords, client data or package passphrases into a public issue or GitHub repository.

Example requests for an authorised operator:

```text
KCAS is unavailable on the live server. Work read-only first. Read docs/KCAS_OPERATIONS_AND_RECOVERY_GUIDE.md, docs/WINDOWS_DEPLOYMENT.md and the deployment scripts. Identify whether the fault is the proxy, Scheduled Task, application, MySQL or document storage. Report evidence, affected business functions and the least disruptive next action. Do not change production yet.
```

```text
The KCAS database may be corrupt. Inventory candidate backups and their timestamps without changing them. Propose an isolated test restore, checks for assessments, investments and evidence paths, and a reconciliation of work after the backup. Do not overwrite production or choose a data-loss window for us.
```

```text
We have approved a KCAS code fix. Verify the PR checks and the reviewed main commit, then guide an authorised operator through the existing immutable Windows deployment. Record the backup, manifest, migration and acceptance checks. Stop if the server checkout is dirty or a required backup cannot be verified.
```

```text
Assume the original KCAS developer is unavailable. Inspect the repository and this guide, then produce a function-by-function recovery plan for authorised client access, compliance review, document evidence and advice. List missing access or documentation as blockers rather than guessing.
```

Codex should finish with: observations, commands/actions taken, files or data changed, backup and recovery point, test results, residual risks and human approvals still required. If its instructions differ from the current code or deployment scripts, it must surface the discrepancy before execution.

## 8. Known readiness work

This guide records a procedure, not a completed recovery test. Before relying on it as the DRP's operating annex, Kanaan should confirm and record:

- the actual live server's task action, URLs, document roots, protected settings and backup locations;
- that an authorised substitute can access the repository, server, MySQL, release packages, keys, file backups and relevant provider support;
- where an offline/readable copy of this guide and the current contact schedule is held;
- a tested restore of MySQL **and** a representative client-document set into an isolated environment, including KCAS evidence links;
- the recovery-point and recovery-time objectives for the critical functions, informed by a business impact assessment and measured exercises;
- who maintains the controlled operational contact, asset and recovery schedules;
- the process for reconciling temporary client instructions and compliance work after a recovery.

Do not mark these items complete merely because this guide exists. Record the test date, participants, backup used, achieved recovery point/time, exceptions and remediation when they are actually demonstrated.
