# Sanctions Coverage and Complaints Register

Authorised: 3 October 2026. Advice/instruction execution workflow is explicitly deferred.

## Scope

- Add client/related-party TFS list-update coverage at `/compliance/sanctions`.
- Reuse actual client evidence, Codex/manual attribution and the compliance work register.
- Include hidden, historical and zero-balance records in the controlled population;
  a visibility filter or balance is not evidence of relationship termination.
- Record official source/version, population snapshot, current checks, unresolved
  matches, supported exclusions and changes requiring fresh coverage. Batch creation
  is not a screening run; no automatic Codex launch or fictitious clear result.
- Add a structured complaint case/register at `/compliance/complaints`, with intake,
  evidence/communications, uninvolved handling, decisions, delivery, escalation/Ombud,
  actual payments and controlled closure. Preserve source spreadsheet entries.
- Use current database permissions and client visibility at the service boundary,
  additive migrations, optimistic concurrency and immutable events/audit history.

## Verification and Documentation

Run affected service/security tests, migration/build checks, and desktop/mobile UI
checks. Apply the local additive migration without resetting client records. Align
RMCP section 11, complaints register references and Monitoring/TCF references with
verified local behaviour. Keep approval/signatures, real screenings, actual complaint
decisions and live deployment separate from implementation. No advice/instruction
workflow change is part of this task.

## Progress

- [x] Inspect current evidence, employee TFS batches, tasks, permissions and policies.
- [x] Read-only database inventory: three expected schemas; no Review items.
- [x] Implement and verify sanctions coverage (37 affected service tests pass).
- [x] Implement and verify complaints register and source preservation.
- [x] Apply local additive migration, restart and verify pages.
- [x] Align affected policies and update the inspection progress/checkpoint.

## Using Sanctions Coverage

1. Open `Compliance -> Client sanctions coverage` (`/compliance/sanctions`).
2. Record a real official FIC/UN list update: exact version, HTTPS source URL,
   publication time and reason. This creates shared in-app Compliance follow-up
   tasks, not screenings or emails. Hidden, historical and zero-balance clients
   remain in the controlled population, together with relevant named parties.
3. Open the batch and use **Copy brief** for the separate Codex review. Actual
   screening must retain the subject, identifiers, performer, time, source/list
   version, result and limitations in the existing evidence records.
4. Use **Record coverage** to select the supported current screening. Previous
   list results, failed checks and changed evidence do not count as clear. Changing
   identifiers requires a fresh check, even when the displayed name is unchanged.
5. Refresh the population after client/party changes. Supported exclusions require
   authorised approval, a reason and reference; a zero balance is not an exclusion.
   Possible/confirmed concerns remain escalated, not cleared by task closure.

The Home follow-up section shows outstanding latest-list coverage to actual approved
Compliance Administrators/Approvers. Existing acceptance-gated KCAS operations
check these holds, including existing clients. This is not an external platform
freeze or an automatic list-polling/Codex execution service. Employee TFS coverage
continues through the separate employee workflow.

## Using the Complaints Register

1. Open `Compliance -> Complaints register` (`/compliance/complaints`).
2. **Record complaint** with actual receipt/contact/allegation, classification and
   an uninvolved approved handler. Case creation is not acknowledgement delivery.
3. Record actual activity and evidence; **Copy brief** supports Codex preparation.
   Codex-prepared evidence is labelled separately from the logged-in saving actor.
   It cannot stand in for actual communications, decisions or payments.
4. An authorised uninvolved decision-maker records the reasoned outcome/remedy.
   Record actual client outcome delivery with recourse details, remedy completion
   and compensation/goodwill payments before controlled closure. Reopening retains
   chronology and avoids treating an award as money already paid.
5. Filter by received dates/status/client/Kanaan ID and export CSV. Counts use case
   receipt dates; paid totals use actual payment events on the selected cases.
6. Administrators may preview/import the original complaints XLSX. Imported rows
   retain original values/source coordinates and start as **NeedsReview**; missing
   facts and historical outcomes are not invented. Repeating the same import does
   not duplicate entries; changed source rows require review rather than overwrite.

The existing source workbook was previewed read-only: three rows, none imported.
Home follow-up and the shared work register link to open cases. Generic task closure
cannot bypass either feature's controlled workflow.

## Documents and Deployment

- New RMCP: `Compliance/FSCA inspections/2026/22Sep2026FeedbackRemediation/02 Working BRA and RMCP revisions/Kanaan RMCP 2026 - working revision 1.10 sanctions coverage.docx`.
- Updated specialist drafts: Complaints Resolution Procedure 2026 and Compliance
  Policy and Monitoring Methodology 2026, at their existing 2026 policy paths.
- All 44 Word-rendered pages inspected: RMCP 32, complaints 5, monitoring 7;
  existing genuine legal footnotes/source lists retained. Signed originals unchanged.
- Migration `20261003180457_AddClientSanctionsCoverageAndComplaints` adds five
  tables without resetting clients or inserting private operational records.
  Applied locally and to the existing dedicated test database; EF reports no
  pending model changes. Incremental and fresh-schema SQL are updated.
- Live needs the reviewed code and additive migration deployed. No dedicated batch
  or complaint transfer package is added by this task. Real coverage, original
  complaint classification, decisions and policy approval remain operating steps.
- Advice/instruction execution tracking remains deferred, not rewritten as complete.

## Final Verification

- All 37 targeted sanctions/complaints, client-acceptance, operations and compliance
  work tests pass. These include exact URL/version boundaries (not a shared prefix),
  changed identifiers, failed/stale checks, match holds, hidden-client access,
  revoked permissions, concurrency, source preservation and actual closure/payment.
- All 16 fully loaded desktop/mobile views pass layout and action checks, including
  copyable briefs, due-date display and Home reminders. Synthetic browser fixtures
  were removed from the dedicated test database; no real screening or complaint
  decision was inserted into the local operational database.
- Build/publish and EF model checks pass. Existing Word footnote XML parts are
  byte-for-byte unchanged against the preserved predecessor/source backups.
- Restarted with `Restart-KCAS.ps1`; both Kestrel and
  `https://kcas.test:8443/Account/Login` return HTTP 200.
- Database inventory: three expected schemas; no Review or unexpected Monitor
  items. No schema was deleted or reset.
