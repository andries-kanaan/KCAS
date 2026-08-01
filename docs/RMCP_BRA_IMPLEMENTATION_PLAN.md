# KCAS RMCP and Business Risk Assessment Implementation Plan

Status: Active source of truth  
Plan owner: Kanaan / KCAS  
Last updated: 2026-07-30
Implementation approach: gated, incremental releases

## 1. Purpose

This document is the permanent implementation roadmap for bringing Kanaan's client risk evaluation, Business Risk Assessment (BRA), Risk Management and Compliance Programme (RMCP), and inspection-readiness administration into KCAS.

It exists so that work may be paused for unrelated priorities and resumed without relying on conversation history. When work resumes, read this document, check the status table, inspect the repository and database state, and continue only from the recorded resume point.

The external working material that informs this plan is under:

`C:\Download\_kanaan\Compliance\FSCA inspections\2026`

That folder remains source and working evidence. KCAS will become the controlled operational system for the underlying client records, assessments, approvals, monitoring and audit evidence. It will not silently replace signed source documents or regulatory records.

## Source reference map

The inspection folder is a required detailed reference library, not merely background reading. Before designing or changing a compliance workflow, inspect the relevant source documents below and record any assumptions that are not resolved by them.

### FSCA inspection scope

Folder: `FSCA Notice`

- `Notice of Inspection -Kanaan Trust.pdf` is the primary source for the inspection mandate, requested scope and formal context.
- `FSP 528_Information Document.pdf` is the primary source for the information requested from Kanaan and the structure of the inspection response.
- `Apenndix A - TC Analysis_ Kanaan Trust FSP 528.docx` and the section 42 analysis must be consulted when translating inspection questions into KCAS evidence and reporting requirements.

Use these documents particularly in Phase 7 and whenever an acceptance criterion is intended to satisfy a specific inspection request.

### Readiness decisions and unresolved questions

Folder: `Readiness prep`

- `01 Readiness Plan.docx` records the overall preparation approach.
- `02 Document Readiness Register.docx` identifies expected documents, readiness and evidence gaps.
- `03 Update Checklist.docx` identifies required updates and preparation actions.
- `04 Questions and Decisions.docx` is the first place to look for Kanaan-specific decisions, unresolved questions and required management input.

These documents guide priorities across all phases. Items marked unresolved must not be silently converted into system rules.

### Existing approved and historical Kanaan position

Folder: `RMCP and Policy Approval\01 Source copies`

- `Kanaan RMCP 2025 - source.docx` is the historical RMCP source for current wording, procedures and responsibilities.
- `Policy Board Resolution 2024 - signed.pdf` is the authoritative evidence of that approval; it takes precedence over the editable copy if they differ.
- `Policy Board Resolution 2024 - editable source.docx` is a drafting aid, not stronger evidence than the signed PDF.
- `Company Organogram 2023 - source.pdf` records the earlier organisational structure and must be reconciled with later governance material.
- `FSCA Appendix A - section 42 analysis.docx` informs the RMCP structure and coverage analysis.

Use these sources in Phases 1, 5 and 6 to understand how Kanaan actually allocates duties and operates controls. Historical wording must not automatically be treated as the final 2026 position.

### Regulatory references

Folder: `RMCP and Policy Approval\02 Regulatory references`

- `FIC Revised Guidance Note 7A - 1 September 2025.pdf` informs the risk-based approach and relevant RMCP/BRA interpretation.
- `FIC goAML message board daily notice - 25 August 2025.pdf` informs operational goAML and related procedures.

These dated copies explain the basis used during the 2026 preparation. Before implementing a rule that depends on current law, guidance, forms, reporting mechanics or sanctions/TFS procedure, verify whether a newer authoritative regulatory source applies. Record the source and effective date in the relevant phase decision.

### Review findings, evidence extracts and intended changes

Folder: `RMCP and Policy Approval\03 Review and gap analysis`

- `RMCP Review and Board Approval Plan.docx` informs the planned review and approval workflow.
- `RMCP Section 42 Review and Change Record.docx` identifies required RMCP coverage and proposed changes.
- `Kanaan 2026 Policy Suite Rationalisation Review.docx` informs policy boundaries, duplication and consolidation decisions.
- `2026 Dashboard extract.txt`, `Kanaan source extracts.txt`, `Second-pass evidence extracts.txt` and `Training Register extract.txt` provide supporting operational evidence and known data sources.

These are the main references when deciding what KCAS must capture, calculate, monitor or export. Gap-analysis conclusions should be tested against the underlying source and approved before becoming mandatory system behaviour.

### 2026 target-state working drafts

Folder: `RMCP and Policy Approval\04 Working drafts`

- `Kanaan Business Risk Assessment 2026 - working draft.docx` is the principal starting point for the Phase 4 BRA structure, risks, methodology and evidence requirements.
- `Kanaan RMCP 2026 - revised working draft.docx` is the preferred current draft for Phase 5 requirements unless a later approved version exists.
- `Kanaan RMCP 2026 - working draft.docx` is retained for comparison and change history.
- `Kanaan Governance and Organisational Structure 2026.docx` informs roles, ownership, reporting lines and approval routing.

These documents are starting points only. They must not be represented in KCAS as approved or effective until the prescribed review and approval evidence exists. The 30 July 2026 management review supersedes their report-style presentation, draft callouts, provisional conclusions and unresolved wording as described in the dated finalisation decision below.

### Client-file operational evidence

Folder: `C:\Download\_kanaan\Clients`

- Client folders, application forms, source-of-funds declarations and supporting records evidence how Kanaan performs client onboarding and records source-of-funds information in practice.
- The client folder is read-only source evidence during the controlled review. A document filename, extracted value or Codex observation does not by itself verify a client fact in KCAS.
- The 30 July 2026 review confirmed that Kanaan always establishes and records source of funds. The record may be an application-form entry, a client declaration or other client-file information. Independent documentary corroboration is obtained where reasonably available or proportionate to the identified risk; it is not a universal prerequisite for every ordinary client.
- Historic inheritance, pension or similar sources may be supported by a sufficiently detailed declaration and plausibility assessment where original documents are no longer reasonably available. Inconsistency, implausibility, unusual activity or higher risk triggers further enquiry and enhanced measures.

### Approval and training evidence

Folder: `RMCP and Policy Approval\05 Board approval pack`

- `Kanaan 2026 RMCP Board Approval Resolution - draft for signature.docx` informs the Phase 5 approval record and expected signed evidence.
- The `2026 FIC Act Training` presentations, assessment and register inform the training controls, knowledge evidence and inspection pack in Phases 5–7.

A draft resolution does not prove approval. KCAS must distinguish draft, approved metadata and attached signed evidence.

### Source precedence and conflict rule

When documents conflict, use this order as a decision aid:

1. Current binding legislation and authoritative regulatory material, verified for currency.
2. The FSCA notice and formal information request for the inspection scope.
3. Signed and effective Kanaan approvals.
4. Formally recorded Kanaan decisions in the readiness and approval process.
5. Historical approved source documents.
6. Review and gap-analysis documents.
7. Revised working drafts, then earlier working drafts.
8. Extracts, generated renders and scripts as supporting evidence only.

Do not resolve a material conflict merely by choosing the newest filename. Record the conflict, identify its operational effect and obtain Kanaan/compliance approval. The approval and source used must be traceable in KCAS or the phase decision record.

## 2. Confirmed design decisions

1. Existing KCAS data is historical operational data, not disposable seed data.
2. A legacy refresh must be scan-first and repeatable.
3. Genuinely new source records may be added through an explicit operation.
4. Identical records are skipped.
5. Changed records are reviewed before any merge.
6. Records missing from a later source extract are flagged, never automatically deleted.
7. KCAS changes must never be silently overwritten by legacy values.
8. Every accepted data change, assessment, override, approval and document version must be attributable and time-stamped.
9. Client risk assessments, the enterprise/business BRA and the RMCP are separate controlled records with explicit links:
   - client assessments measure individual client risk;
   - the BRA assesses Kanaan's exposure across clients, products, services, channels, geography and operating environment;
   - the RMCP records the approved controls, procedures, responsibilities and monitoring response.
10. Development proceeds phase by phase. A phase starts only after the prior phase's acceptance gate passes.
11. The final RMCP is Kanaan's own policy and operating programme. It uses present-tense Kanaan-owned wording, not external-review, audit-report or implementation-status language.
12. Kanaan onboards most clients face to face and also uses direct telephone or Zoom engagement. Email alone is not the client engagement or identity-verification process. Remote engagement requires reliable independent verification and channel-appropriate controls but is not automatically High risk.
13. Kanaan always establishes and records source of funds. A declaration or recorded client explanation is acceptable where credible and proportionate; documentary corroboration is risk-based rather than mandatory in every file.
14. The entity-wide BRA must be concise, proportional and tailored to Kanaan. It must distinguish inherent risk from residual risk, consolidate overlapping scenarios and avoid treating offshore exposure, remote contact or a possible severe regulatory consequence as automatically High risk.
15. The BRA and RMCP require formal Board adoption. Any KI review or KCAS workflow approval is a preparatory governance control and does not replace the Board resolution or signed final document.
16. Final 2026 documents will be placed under `RMCP and Policy Approval\05 Board approval pack\RMCP and Business Risk Assessment` as `Kanaan RMCP 2026.docx` and `Kanaan Business Risk Assessment 2026.docx`, with matching stable PDFs and exact Board-resolution references when approved.

## 3. Scope boundary

### In scope

- Incremental legacy-to-KCAS reconciliation.
- Client profile and evidence completeness.
- Client money-laundering, terrorist-financing and proliferation-financing risk assessments.
- Configurable risk methodology and controlled methodology versions.
- Business Risk Assessment preparation, review, approval and version history.
- RMCP controls, ownership, review, approval and traceability to risks.
- Periodic reviews, trigger events, remediation tasks and evidence.
- Inspection requests, readiness registers, evidence indexes and exports.
- Role-based access, segregation of duties and audit history.

### Not automatically in scope

- Autonomous regulatory decisions or filings.
- Automatic suspicious transaction reporting without authorised human review.
- Replacing signed board resolutions or externally issued regulatory documents with editable database records.
- Treating a score as a substitute for documented professional judgement.
- Importing or merging changed legacy data without review.

## 4. Status and resume point

| Phase | Deliverable | Status | Gate |
|---|---|---|---|
| 0A | Safe scan and add-new reconciliation foundation | Complete for delivery; live acceptance evidence remains operational follow-up | Controlled scan/apply-new workflow available |
| 0B | Reviewed field-by-field merge and reconciliation closure | Complete for delivery; live acceptance evidence remains operational follow-up | Review, apply, rejection, deferral and audit trail available |
| 1 | Compliance foundation and controlled configuration | Complete for delivery; browser acceptance evidence remains operational follow-up | Configuration/versioning and permissions accepted |
| 2 | Client profile and evidence readiness | Foundation delivered; operational population deferred to Phase 8 | Pilot clients pass completeness and evidence checks |
| 3 | Proportional client risk assessment workflow | Technical delivery complete; operational acceptance deferred to Phase 8 | Pilot assessments reproduce approved methodology |
| 4 | Business Risk Assessment | Technical foundation delivered and browser-checked by the user on 2026-07-26; production BRA v1.0 signed and Board-approved on 2026-07-31; population reconciliation continues in Phase 8 | BRA approved from traceable evidence and methodology |
| 5 | RMCP control and approval management | Technical foundation delivered and browser-checked by the user on 2026-07-26; production RMCP v1.0 signed, Board-approved and effective on 2026-07-31; KCAS control/evidence reconciliation continues in Phase 8 | Approved RMCP version links risks, controls and evidence |
| 6 | Monitoring, reviews and remediation | Technical foundation delivered and browser-checked by the user on 2026-07-26; operational acceptance is deferred to Phase 8 | End-to-end review and escalation cases pass |
| 7 | Inspection readiness, reporting and rollout | Technical foundation delivered and browser-checked by the user on 2026-07-26; the requested document pack was uploaded to the FSCA and confirmed by email on 2026-07-31; onsite operational acceptance continues in Phase 8 | Inspection pack, security, recovery and rollout accepted |
| 8 | Controlled operational population and verification | In progress: control foundation delivered and first live Badenhorst pilot completed; resume the remaining population client by client | Every current client is reviewed, verified and assessed |

Current resume point: **resume Phase 8 client by client under explicit user approval using the completed Prof Philip Nel Badenhorst pilot as the operational pattern, while continuing the operating-effectiveness and retrieval evidence required for the 22 September 2026 onsite inspection.**

The inspection-readiness work does not replace or cancel Phase 8. It creates two controlled horizons:

1. The 3 August 2026 document-response horizon was completed ahead of deadline on 31 July 2026: the numbered final pack, signed explanatory/factual records, signed and Board-approved BRA/RMCP and training/monitoring evidence were uploaded to the FSCA, and the upload was confirmed to the FSCA by email. The sent email and any portal receipt or screenshot should be retained with the submission evidence.
2. Before the onsite inspection on 22 September 2026, continue the KCAS current-client population, TFS/screening coverage, representative control testing, goAML continuity evidence and mock inspection/retrieval exercise.

Phase 0 remains available for operational import acceptance and final data switch-over evidence, but it is no longer the active development blocker.

Phase 1 remains available for browser acceptance and live workflow evidence, but its foundation code has been delivered and is no longer the active development blocker.

## 5. Delivery rules for every phase

Each phase must follow the same small-release loop:

1. Confirm the precise requirements and source documents for that phase.
2. Define the data model, permissions, audit events and acceptance tests.
3. Implement the smallest usable vertical slice.
4. Add database migration and automated tests.
5. Test against an isolated database.
6. Demonstrate the workflow with representative Kanaan data.
7. Record unresolved issues and obtain acceptance.
8. Deploy only after backup and rollback arrangements are confirmed.
9. Update this document's status and resume point.

A phase is not complete merely because pages or tables exist. Its business workflow, audit trail, permissions, reporting and acceptance evidence must work together.

For Phases 3–7, distinguish technical delivery from operational acceptance. Technical delivery is proven with automated and synthetic data so the complete toolset can be built first. Live methodology activation, client population, representative client assessment, production BRA/RMCP approval and final inspection evidence are deliberately completed together in Phase 8.

## 6. Phase 0 — Historical data reconciliation gate

### Phase 0A: safe scan and add-new foundation

#### Objective

Make repeated legacy imports safe while preserving all KCAS operational changes.

#### Implemented

- Default scan mode that changes reconciliation metadata only.
- Explicit add-new mode.
- Canonical source payloads and fingerprints.
- Persistent import runs, row states, accepted source snapshots and field differences.
- Classifications for new, unchanged, changed, missing, invalid and orphaned records.
- Client reconciliation status.
- Protected administrator reconciliation page.
- Baseline import reset guarded by `LegacyImport:AllowResetImportedData`; this remains temporary while KCAS is not yet the operational system of record.
- Migration and targeted deployment SQL.
- Automated reconciliation, recorder and protected-route tests.
- Isolated real-source rehearsal proving add-new followed by an idempotent scan.

#### Still required for acceptance

1. Back up the working KCAS database.
2. Confirm the latest deployed build and migrations are active on the live server.
3. Temporarily set `LegacyImport__AllowResetImportedData=true` on live only while reset imports remain acceptable.
4. Run `--scan` first; do not begin with `--apply-new`.
5. Reconcile totals by source table and inspect representative clients, KYC, notes, accounts, transactions, valuations and reference data.
6. Investigate every invalid and orphaned item.
7. Approve the list of genuinely new records.
8. Run `--apply-new` once.
9. Run another scan and prove that accepted records are unchanged and were not reapplied.
10. Record acceptance evidence and remaining changed/missing items.

#### Acceptance gate 0A

- No existing business value was overwritten.
- No missing source row caused a deletion.
- New records were added once only.
- A second scan is idempotent.
- Counts reconcile to the source tables.
- Reconciliation details are visible only to authorised users.
- Backup and rollback were tested or formally confirmed.

### Phase 0B: reviewed merge and reconciliation closure

#### Objective

Allow authorised users to resolve changed source records without sacrificing KCAS changes or auditability.

#### Implemented

- Review queue grouped by run, source table, client and severity.
- Side-by-side baseline, incoming source and current KCAS values.
- Field decisions: retain KCAS, apply incoming source, manually resolve, defer, or reject.
- Mandatory reason for each review decision and mandatory manual resolution value/note.
- Transactional application of approved changes.
- New accepted source snapshot only after successful approval/application.
- Rejection and deferral handling.
- Missing-from-source resolution without automatic deletion.
- Audit trail with reviewer, review time, decision and reason.
- Permission set for viewing, reviewing, approving and administering imports.

#### Still required for acceptance

1. Exercise retain KCAS, apply incoming, manual, defer and reject decisions on representative live review rows.
2. Confirm review actions are limited to authorised users and import apply/reset actions remain administrator-only.
3. Confirm applied decisions become the next comparison baseline in a verification scan.
4. Confirm deferred and rejected rows remain visible and traceable.
5. Capture unresolved 0B gaps as follow-up slices before Phase 1 begins.

#### Acceptance gate 0B

- A changed representative client can be reviewed field by field.
- Keeping KCAS preserves the current value.
- Accepting source changes only the approved field.
- A concurrent edit blocks stale approval.
- Rejected and deferred items remain traceable.
- Applied decisions form the next comparison baseline.
- Re-running the same source produces unchanged results for resolved records.

## 7. Phase 1 — Compliance foundation and controlled configuration

### Objective

Create the common governance structures required by all later compliance modules.

### Deliverables

- Compliance administration area and navigation.
- Legal entity/FSP profile, accountable institution details and responsible roles.
- Configurable client types, products/services, delivery channels, countries/geographies and risk bands.
- Versioned risk-factor definitions, weights, thresholds and mandatory rules.
- Controlled document register with document type, owner, effective date, review date and status.
- Governance register for responsible persons, MLCO/compliance roles and delegated approvers.
- Generic task, comment, evidence attachment and approval components.
- Immutable audit-event service covering old value, new value, user, timestamp and reason.
- Permissions for preparer, reviewer, approver, compliance administrator, read-only inspector and system administrator.
- Effective-date handling so historical assessments retain the methodology used at the time.

### Implemented in current Phase 1 branch

- Compliance dashboard and management routes for profile, governance, documents, references, methodologies, tasks, evidence and audit.
- FSP/accountable-institution profile, governance role assignments and controlled document register.
- Compliance reference values for controlled categories such as client types, products/services, delivery channels, geographies, evidence types and task categories.
- Versioned risk methodology records with factors, options, risk bands and status transitions.
- Compliance tasks, evidence records, approvals and immutable audit events.
- Reason-required service layer for compliance mutations.
- Methodology submit, approve, reject, activate and supersede workflow.
- Compliance view/manage/approve/audit permissions.

### Acceptance gate 1

- A new methodology version can be drafted, reviewed, approved and activated.
- Existing records retain their original methodology version.
- Unauthorised users cannot change configuration or approvals.
- Every configuration and status change is auditable.

### Still required for Phase 1 acceptance evidence

1. Browser-check `/compliance`, `/compliance/settings`, methodology approval/activation, compliance permissions and audit log on the deployed environment.
2. Record any live-only defects as follow-up slices rather than reopening the foundation build.

## 8. Phase 2 — Client profile and evidence readiness

### Objective

Make KCAS the reliable evidence base from which client risk can be assessed.

### Deliverables

- Required-information matrix by natural person, legal person, trust and other applicable client type.
- Identification and verification evidence register with issue, receipt, verification and expiry dates.
- Address, contact, tax/residency, occupation/business activity and source-of-funds/source-of-wealth information.
- Ownership, control, trustees, beneficiaries, authorised persons and related-party structures.
- Product/service, investment relationship, delivery channel and geographic exposure.
- PEP/prominent-influential-person, sanctions/TFS and adverse-information check records, including provider, date, result and reviewer.
- Data-quality and document-completeness dashboard.
- Missing, expired, inconsistent and unverified evidence tasks.
- Evidence provenance and links to the underlying client record.
- Refresh dates and event-driven review triggers.

### Implemented in current Phase 2 branch

- Client evidence requirement matrix seeded from the current audit-readiness/BRA needs.
- Per-client evidence readiness page at `/clients/{id}/evidence`.
- Global client evidence dashboard and server-side scan workflow at `/compliance/client-evidence`.
- Server document-root configuration for the live machine, with recursive scan runs that link metadata/path records without copying sensitive documents into KCAS.
- Deterministic client matching from Kanaan ID, existing legacy client folder metadata, folder names and client names.
- Unmatched and ambiguous scan files retained for review instead of being auto-linked.
- Client evidence items with verification, expiry and reviewer metadata.
- Approved evidence exceptions with review dates so specific blockers can be temporarily cleared with audit traceability.
- Evidence-gap task creation through the existing compliance task model.
- Computed “ready for risk assessment” status; it is not manually editable.
- Shared physical client-folder support with explicit per-client and joint aliases.
- Conservative shared-folder ownership matching: only explicit aliases auto-assign; generic or conflicting files require review.
- Audited evidence ownership confirmation, multi-client assignment and exclusion without deleting scan history.
- Ownership state is enforced by readiness and verification so unresolved or excluded files cannot satisfy requirements.
- Evidence-path category inference no longer treats ordinary estate or asset-distribution filenames as proof of client legal form.
- Dedicated ownership and related-party register for trust and legal-person clients.
- Entity profiles capture legal form, registration details, establishment date and business or trust purpose.
- Multi-role related parties capture founders, trustees, beneficiaries, directors, members/shareholders, beneficial owners, controllers, authorised persons and senior-managing-official fallbacks.
- Party-specific identity, authority and ownership/control evidence links, with related-party screening traceability.
- Audited ownership/control completion requires a documented conclusion, next review date and verified supporting evidence; percentage ownership alone does not prove control.
- Trust and legal-person readiness now includes structured ownership, party evidence, screening and review-date blockers.

### Current Phase 2 acceptance position

- The first live pilot scans completed successfully without unmatched client folders or scan errors.
- Philip Nel and Jacob Benade were restored to natural-person classification after false evidence-path inference; the correction is audited.
- The shared Bodenstein dossier is configured for wife, husband and joint aliases.
- Explicitly named files were reconciled; 195 generic shared-folder documents remain queued for ownership review.
- Representative legal-person scanning, evidence verification, screening review, exceptions/tasks and final natural-person/trust/legal-person acceptance remain outstanding.
- The entity-ownership register is deployed without inferred live parties; Tomora Trust remains deliberately blocked until its reviewed registration, party, evidence and screening facts are captured.
- No accessible legal-person document folder is currently available under the downloaded pilot root, and the legacy `Z:` folders are unavailable; the company/CC pilot remains pending a reviewed accessible folder.

### Acceptance gate 2

- Representative natural-person, company and trust profiles can be completed.
- Mandatory evidence varies correctly by client type.
- Missing and expired items are visible and actionable.
- Relationship and beneficial-ownership information is traceable.
- A risk assessment cannot be finalised when blocking evidence is absent unless an authorised, reasoned exception is recorded.
- A live server client document root can be scanned without copying files, and repeated scans do not duplicate unchanged evidence links.
- Unmatched or ambiguous files remain visible for manual review.

## 9. Phase 3 — Proportional client risk assessment workflow

### Objective

Produce consistent, explainable and repeatable client risk assessments suited to Kanaan's small operating structure: two KIs, three representatives, an accountant and an administrator. The workflow must preserve the statutory evidence chain without imposing bank-scale role, case-management or approval structures.

### Risk dimensions

- Client and legal-form risk.
- Ownership/control complexity and transparency.
- Geographic and jurisdiction exposure.
- Product and service exposure.
- Delivery channel, including face-to-face, direct telephone and Zoom engagement. Email alone is not onboarding; remote engagement is assessed on the effectiveness of verification and authentication controls and is not automatically High risk.
- Transaction/activity profile.
- Source of funds and source of wealth. Source of funds is always established and recorded, while documentary corroboration is proportionate to risk and availability. A credible client declaration or recorded explanation may satisfy the ordinary evidence requirement; higher-risk, inconsistent, unusual or implausible cases require further enquiry and stronger corroboration.
- PEP/prominent-influential-person exposure.
- Sanctions/TFS and adverse-information indicators.
- Other Kanaan-approved risk indicators.

### Deliverables

- One compact assessment against a fixed methodology version. A submitted version may be used provisionally while operational KI sign-off is pending; every assessment remains pinned to that exact version.
- Verified client and evidence facts presented for confirmation, with source links and required assessor explanations.
- Explainable factor calculation and Low, Standard or High classification.
- Kanaan's ordinary controls recorded in the methodology; only client-specific control failures, EDD and exceptions are repeated on an assessment.
- Sanctions/TFS concerns stop finalisation and require escalation; PEP and designated elevated-risk cases require EDD.
- A representative or KI may finalise ordinary Low or Standard assessments.
- One authorised KI approves High-risk clients, overrides, material evidence exceptions and acceptance of escalated relationships; the other KI is the backup or conflict alternative.
- Effective date, next review date, supersession and event-triggered reassessment.
- Frozen assessment snapshot so later methodology, evidence or client edits do not rewrite history.
- A population-complete client-risk assessment register covering every Current client, with proportionate coverage, readiness, classification, workflow, EDD and review-date oversight rather than bank-scale analytics.

### Implemented in current Phase 3 branch

- Audited client assessments pinned to an immutable submitted, approved or active methodology version.
- Controlled Kanaan starter methodology created only on request. Drafts cannot be used; submission places a complete version into provisional operational use so client work is not blocked.
- One active Key Individual recorded in the governance register provides the later operational methodology sign-off. The Compliance Officer may prepare, submit and use the methodology but cannot supply the KI sign-off.
- Approval and activation remain visible governance milestones. Rejected, Draft or Superseded methodologies cannot be used to finalise assessments.
- Six-factor 1–3 starter scale, configurable weighted score, Low/Standard/High bands and configurable review periods.
- Compact per-client assessment page and formal client-risk register. The register includes assessed, in-progress and entirely unassessed Current clients so apparent coverage cannot be created by omitting outstanding clients.
- Evidence-readiness finalisation gate and links to current, confirmed, verified client evidence.
- Representative/KI routine finalisation for Low or Standard assessments.
- Mandatory escalation for High risk, PEP, adverse information and overrides, with one authorised KI approval and the other KI retained as the backup or conflict alternative.
- Sanctions/TFS concerns block finalisation, and mandatory High triggers cannot be overridden downward.
- Frozen assessment snapshot, effective and next-review dates, supersession and audited return-to-draft workflow.
- Full read-only methodology review showing factors, options, weights, mandatory triggers, bands and review periods.
- Structured reassessment triggers linked to the prior assessment, with copied answers remaining unconfirmed until reviewed again.
- Coverage, readiness, EDD, due, overdue, status and rating filters plus a printable frozen assessment/history record.
- Current-population totals, assessment coverage percentage, Low/Standard/High distribution, pending-KI/EDD, blocker and review-due counts.
- Permission-controlled as-at CSV and full-population inspection JSON exports retaining client IDs, methodology version/status, workflow, readiness, preparer/finaliser and review dates for audit sampling and inspection-pack indexing. The inspection export deliberately ignores screen filters so an apparent complete register cannot be produced by omitting clients.

### Operational acceptance gate 3 — deferred to Phase 8

- An agreed pilot sample is scored manually and in KCAS with matching results.
- Every result is explainable from recorded evidence and methodology.
- High-risk mandatory rules cannot be neutralised by ordinary weighting.
- High-risk decisions, overrides and material exceptions contain one authorised KI approval and reason.
- Provisional methodology use is clearly labelled, does not block ordinary client assessments and retains the exact version used; one designated KI can sign it off later.
- Historical assessments remain unchanged after methodology or client-data updates.

## 10. Phase 4 — Business Risk Assessment

### Objective

Create one concise annual Kanaan entity-wide BRA using traceable operational evidence and explicit management judgement.

### Deliverables

- One versioned annual record covering clients, products/services, channels, geography, activity and relevant external threats.
- Simple portfolio counts and concentrations drawn from approved client assessments.
- A compact 3-by-3 likelihood/impact risk table using a small set of material, consolidated Kanaan-specific risk themes. It must define inherent risk as exposure arising from Kanaan's actual business profile before mitigating controls and residual risk as the exposure remaining after operating controls.
- An overall risk conclusion in Kanaan's own voice. Do not use an audit-style management dashboard, confirmation-status columns or evidence-readiness ratings as substitutes for the business risk assessment.
- Proportionate ratings that reflect Kanaan's predominantly natural-person, established client base, no-cash/no-custody model, known products and direct client contact. Offshore exposure, remote engagement and severe possible regulatory consequences do not automatically make inherent or residual risk High.
- Management judgement, limitations and risk tolerance in the BRA. Operational actions, owners and due dates remain in the linked compliance work register rather than making the signed BRA read like a remediation report.
- KI review followed by formal Board approval and an immutable approved version.
- Reproducible export matching the approved Kanaan BRA format.

The BRA must not be a simple average of client scores. Client results are evidence inputs; concentration, products, channels, external threats, controls and management judgement remain independently assessed.

### Implemented technical foundation

- Separate Business Risk workspace, annual version list and six-category Kanaan starter draft. The starter is a technical foundation, not a requirement to preserve the wording, dashboard or ratings of the July working draft.
- Proportional 3-by-3 likelihood/impact matrix with visible inherent score and rating.
- Separately recorded controls, control effectiveness, residual rating, rationale, treatment decision, owner and due date.
- As-at portfolio evidence snapshot using approved client assessments plus investment-account administrator and product concentrations.
- Draft, KI review, Board-approved, effective and superseded states with reasoned audit events. The existing two-KI technical review workflow is preparatory and cannot by itself mark the BRA formally approved without the Board resolution and final approved document.
- Frozen approved JSON record and reproducible printable view.
- No live BRA was created, submitted, approved or activated during technical delivery.

### Operational acceptance gate 4 — deferred to Phase 8

- Portfolio totals reconcile to approved client assessments as at a stated date.
- Every BRA conclusion links to evidence, methodology and recorded judgement.
- Control effectiveness and residual risk are separately visible.
- Ratings are recalculated from the approved methodology and actual Kanaan profile rather than inherited from the July working draft.
- Treatment actions flow into the Phase 6 remediation workflow.
- Board approval evidence, exact final document version and KI review history are separately traceable.
- The approved export is reproducible from the frozen BRA version.

## 11. Phase 5 — RMCP control and approval management

### Objective

Manage the RMCP as a controlled document and concise risk/control register linked to Kanaan's BRA and operational evidence.

### Deliverables

- Versioned RMCP record with scope, effective date, owner, review cycle and signed Word/PDF source.
- One concise register mapping BRA risks to RMCP controls, owners, frequency, evidence, monitoring and escalation.
- Coverage for client risk, CDD, EDD, screening, records, reporting, training, governance and review procedures.
- Kanaan-owned present-tense policy wording. Remove report-style history, management-confirmation prose, outstanding-question callouts, yellow/orange draft notes and language that instructs Kanaan from an external perspective.
- Directly state Kanaan's actual onboarding channels and risk-based source-of-funds practice.
- A short accurate section 42 applicability statement in the RMCP. A separate Annexure D is not required; the inspection-pack Appendix A/page-mapping remains a separate controlled evidence item.
- Gap or treatment actions recorded in the common compliance work register.
- Draft, KI review, Board-approved, effective and superseded states, with change reason and exact Board approval evidence.
- The signed document remains authoritative; KCAS will not become a custom word processor or bank-scale clause-management system.

### Implemented technical foundation

- Separate RMCP workspace with controlled draft, KI review, approved, effective and superseded versions.
- Required references to an approved/effective BRA, signed Word/PDF source and signed approval resolution.
- Concise starter register covering client risk, CDD, EDD, screening, records, reporting, training, governance and review.
- BRA-risk links plus owner, frequency, expected evidence, monitoring and escalation for every control.
- Submission blocks incomplete domain coverage, incomplete controls and unmapped material BRA risks.
- Identified control gaps create linked treatment items in the existing compliance work register.
- KI review, Board approval evidence, reasoned audit events, frozen approved JSON and printable control record. Any existing two-KI review step is preparatory and does not replace formal Board adoption.
- No live RMCP, BRA or client-assessment record was created, submitted, approved or activated during technical delivery.

### Operational acceptance gate 5 — deferred to Phase 8

- Every material BRA risk maps to one or more controls or an approved treatment action.
- Every control has an owner, evidence expectation and monitoring frequency.
- KI review, Board approval, the signed resolution and the exact signed document are separately traceable.
- The effective RMCP cannot be edited; revisions create a new version.
- The controlled document reconciles with the approved KCAS record.

## 12. Phase 6 — Monitoring, reviews and remediation

### Objective

Turn approved assessments and controls into a single manageable compliance worklist.

### Deliverables

- Extend the existing compliance task model into one work register with task types for periodic review, trigger review, EDD, screening escalation, control test, treatment action, finding, training, exception and remediation.
- Record owner, due date, evidence, outcome, closure reason and linked client/BRA/RMCP/control.
- Calculate client review dates from approved risk and create work for material changes in client, ownership, product, geography, screening or activity.
- Provide simple overdue, High-risk and unresolved filters.
- Keep suspicious/unusual activity decisions human-authorised; KCAS performs no autonomous regulatory filing.

### Implemented technical foundation

- One Monitoring work register with periodic review, trigger review, EDD, screening escalation, unusual-activity review, control test, treatment, finding, training, exception and remediation types.
- Explicit links to client, client assessment, BRA, RMCP version and RMCP control while retaining compatibility with the existing compliance task links.
- Search plus overdue, High-priority, unresolved, type and status filters.
- Due periodic client assessments generate idempotent review items; starting a controlled client reassessment automatically creates the matching periodic, trigger, screening or unusual-activity work item.
- Escalation records High priority, responsible user, timestamp, reason and audit event.
- Closure requires recorded evidence, outcome and closure reason before approval.
- Ordinary closure requires one authorised approval; High-priority, EDD, screening and unusual-activity closure requires two distinct approvals.
- RMCP gaps continue to generate linked treatment items in this common register.
- Unusual-activity screens explicitly state that KCAS neither decides nor submits a regulatory report.
- No live work item was created, escalated or closed during technical delivery.

### Operational acceptance gate 6 — deferred to Phase 8

- A periodic review and a trigger-event review complete end to end.
- A high-risk/EDD case shows evidence, decisions and approvals.
- An overdue remediation item escalates correctly.
- Control testing links results to the relevant RMCP control and BRA risk.
- Closure cannot occur without required evidence and authorised approval.

## 13. Phase 7 — Inspection readiness, reporting and controlled rollout

### Objective

Produce a defensible inspection evidence pack and put the proportional Kanaan solution into controlled operational use.

### Deliverables

- One inspection workspace with request items, owner, due date, status and evidence.
- A reproducible evidence index linking clients, assessments, BRA, RMCP, approvals, training, monitoring and remediation.
- As-at-date reports and controlled exports rather than a permanent external-auditor portal.
- Access, audit-log, sensitive-data, backup, restore and rollback verification.
- Proportionate performance testing and a single Kanaan acceptance/training exercise rather than staged enterprise rollout.

### Implemented technical foundation

- Separate permission-controlled inspection workspace with reference, authority, scope, coordinator, as-at date, due date and status.
- Request-item register with category, owner, due date, evidence title/location, optional entity link, review notes and readiness status.
- Reproducible frozen evidence index covering clients, client assessments, BRA, RMCP, approvals, training, monitoring/remediation, controlled documents, compliance evidence and audit-event count.
- Protected printable pack and exact JSON export of the frozen as-at snapshot; no external-auditor portal.
- Explicit readiness checks for permissions, audit log, sensitive data, backup, restore, rollback, performance and user acceptance/training/support.
- A pack cannot freeze until every request is ready/not applicable and every readiness check has actual passing evidence and test notes.
- Frozen and closed inspection packs are immutable and reason-audited.
- Dedicated inspection view/manage/export permissions are assigned proportionately to existing compliance roles.
- No live inspection case, request item or readiness result was created during technical delivery.

### Operational acceptance gate 7 — in progress during Phase 8 rollout

- A mock inspection request can be answered from KCAS with a complete evidence index.
- Reports reproduce the approved records as at the selected date.
- Permissions and the Kanaan-specific approval rules pass review.
- Backup restore and rollback are demonstrated.
- Kanaan's operational users approve the workflows, training and support ownership.

## 14. Phase 8 — Controlled operational population and verification

### Objective

Return deliberately to the client information and evidence population work only after the Phase 3–7 tools provide a destination and control for every relevant fact.

### Deliverables

- Validate the 490 imported records first and classify each as current, closed, deceased, duplicate or historical.
- Preserve historical records, but fully populate and assess the confirmed current population.
- Work client by client: select the folder, scan and link documents, compare the documents with KCAS, and record Codex findings and recommendations.
- Apply no recommended change until the user explicitly accepts it.
- Mark assisted or imported values as awaiting human verification and show the outstanding count on the client record.
- Let an authorised Kanaan user verify or reject each item in KCAS with source, user, date and reason.
- Complete the real client assessment only after its blocking information is verified.
- After the current population is complete, finalise the production BRA, effective RMCP, monitoring baseline and inspection evidence pack.

### Acceptance gate 8

- Every imported record has a confirmed lifecycle classification.
- Every current client has a completed evidence/readiness review and an approved or finalised risk assessment.
- No assisted value is treated as verified merely because it was extracted or proposed by Codex.
- The production BRA reconciles to the verified current-client population.
- The programme is not marked complete while any current client has unresolved blocking verification items.

### Phase 8 implementation sequence

1. Deliver and browser-check the lifecycle classification and assisted/imported fact-verification controls without altering live client facts.
2. Confirm every imported record as current, closed, deceased, duplicate or historical, retaining the reviewer, date, reason and duplicate link where applicable.
3. For each confirmed current client, work collaboratively in Codex: select the client folder, scan/link documents, inspect each relevant document and present differences and recommendations.
4. Queue every assisted/imported fact or proposed replacement as awaiting human verification. Do not apply a replacement until the user explicitly accepts it.
5. Let an authorised Kanaan user verify or reject each queued item in KCAS with the source, decision user, date and reason visible in the register.
6. Complete evidence readiness and the client risk assessment only when lifecycle and blocking verification checks are clear.
7. Reconcile and approve the production BRA, RMCP, monitoring baseline and inspection pack from the verified current population.

### Implemented Phase 8 control foundation

- A portfolio review page shows lifecycle totals and outstanding verification counts before client-by-client work begins.
- The main client register shows the controlled lifecycle separately from investment position and current ZAR value. It can filter current investments, no current investments, historical-only holdings and status corrections; an unreviewed client with no current investment is flagged for lifecycle review but is never closed automatically.
- A portfolio-wide Investment Summary uses the same calculation for all-client and client-specific views. Each unique current fund valuation is counted exactly once; legacy account matches provide context but may neither multiply nor suppress the valuation. It supports client/shared-Kanaan-ID and investment filters, current/historical scope, underlying-fund allocations, SA/offshore totals, native and ZAR values, stale and unmatched valuations, correction indicators and CSV export. A protected Investment reconciliation page separately exposes duplicate account matches, current-valuation/surrender conflicts, unmatched valuations and accounts without a current value, with direct links to correct the underlying client records. This provides the portfolio reconciliation view required during client-by-client verification without automatically changing lifecycle.
- Each client has a controlled lifecycle classification with mandatory reason, reviewer and timestamp; duplicate classification requires a canonical client.
- Assisted/imported facts and Codex recommendations are held in a pending register with current value, proposed value, source, recommendation and blocking flag.
- Accepting a replacement applies only a supported field and fails safely if the underlying value changed after the recommendation was created.
- Verification and rejection require a human decision reason and retain the user, time and compliance audit event.
- Client detail warns when lifecycle review or human verification remains outstanding.
- Final client-risk assessment is blocked unless the client is classified Current and has no blocking verification items.
- Technical delivery creates no lifecycle decisions or verification items for live clients.
- Technical verification through 2026-07-27 passed 161 automated tests with zero failures and no pending EF model changes. Migration `20260726175421_AddClientOperationalVerification` was applied after backup; all 490 imported clients initially remained Unreviewed and the verification register remained empty before controlled live population began.

### First live client pilot — Prof Philip Nel Badenhorst

The first complete client-by-client review was performed collaboratively and re-executed against the restored operational database on 2026-07-27. It establishes the following operational order for later clients:

1. Resolve the correct KCAS and legacy client records, confirm the lifecycle as Current and verify that the selected filesystem folder belongs to that client.
2. Scan and link the folder. Report the linked, skipped, unmatched and ambiguous totals before relying on the scan.
3. Reconcile material imported facts with the legacy source and documents before compliance assessment. For this pilot, the investment totals, underlying funds, historical holdings and offshore original currencies were corrected and presented accurately before the risk review continued.
4. Inspect the linked documents by evidence category. Present the finding, source document, discrepancy and proposed treatment to the user; write or verify nothing until the user explicitly approves it.
5. Record the approved evidence decisions with their source references, reviewer and review dates. The pilot verified identity, address, tax residency, product/service, delivery mandate, source of funds, source of wealth and geography evidence.
   A bank statement, utility bill or non-expiring identity document is point-in-time evidence and is not assigned an artificial expiry date. Current address confirmation is sufficient unless the client's risk, conflicting information or a trigger event calls for refreshed corroborating evidence; the normal ongoing-due-diligence review cycle governs later refresh.
6. Record a reasoned exception where the normal evidence category does not fit the client. The pilot used the proportionate natural-person beneficial-ownership exception after the family/co-policyholder funding context was confirmed.
7. Perform current PEP/PIP, sanctions/TFS and adverse-information screening. Retain the search basis, source names, URLs, access date, finding and risk conclusion in KCAS rather than recording an unsupported checkbox result.
8. Recheck evidence readiness and resolve every blocking item. Do not begin or finalise the assessment while lifecycle or blocking verification checks remain unresolved.
9. Apply the fixed six-factor methodology collaboratively. Present the proposed option and explanation for ownership, geography, product/service, delivery channel, activity and source of funds/wealth, and ask the user to resolve any fact that cannot be established from the evidence. In this pilot the user confirmed that the relationship was established face to face.
10. Save the selected options, evidence links, explanations, screening overlays and overall narrative, then finalise only after user approval. Retain the exact methodology version used, including a visible provisional status if KI sign-off is still pending.

Pilot result:

- Assessment status: Finalised.
- Methodology: `Kanaan proportional client risk methodology Working draft v1`, used provisionally while operational KI sign-off remains pending.
- Factor result: ownership 1, geography 2, product/service 2, delivery 1, activity 1 and source 2.
- Total score and rating: 9, Standard.
- EDD: not required; Kanaan's standard monitoring controls apply.
- Effective date: 2026-07-27.
- Next periodic review: 2029-07-27, subject to an earlier trigger-event reassessment.
- Approval boundary: the Compliance Officer prepared and recorded the review after explicit user decisions. One active governance-register KI may later sign off the operational methodology; formal BRA and RMCP adoption remains a Board of Trustees decision.

For every later client, repeat the same sequence and retain client-specific judgement. The pilot outcome is a workflow precedent, not a template conclusion or automatic risk score.

### Controlled transfer from collaborative review to live KCAS

Client-specific operational decisions are never committed as EF migrations or repository seed data. After a client review is finalised locally, KCAS can export an encrypted `.kcas-review` package containing the stable client identifiers, verified evidence metadata and file hashes, approved exceptions, decided verification items, pinned methodology and completed assessment. Document content is not included.

The protected live `Compliance > Review transfers` workflow decrypts and previews the package, resolves stable client/methodology/factor/option keys, reports new and existing evidence, and blocks client mismatches, methodology drift, existing assessment conflicts and duplicate application. Applying the reviewed preview is atomic, retains the encrypted package and creates a compliance audit event identifying the live importer and approval reason.

A finalised or approved client-risk page exposes a direct `Create review package` action only to users holding `Compliance.Manage`. The link opens Review transfers with that completed client already selected; the destination page and package-download endpoint independently enforce the same permission.

Compliance configuration is divided into focused route-backed tabs for profile, governance, controlled documents, reference values, methodology, tasks, evidence and audit. Each tab displays and saves only its own control area; the required change reason applies solely to the action performed on that tab.

Local packages are held outside source control under `backups\client-review-packages\outgoing` and `incoming`. Production packages are held in the release-independent shared area `D:\Deploy\KCAS\shared\client-review-packages`, unless `ClientReviewTransfers:StorageRoot` explicitly selects another protected location. Passphrases must be transferred separately and are never stored by KCAS.

Package filenames use `KCAS-review-C{client ID}-{surname or entity}-{date}-{unique token}.kcas-review`. The database client ID makes the source package unique even where a Kanaan ID is shared. Each text segment is length-limited and reduced to filename-safe letters, digits and hyphens. The encrypted payload continues to use the stable legacy and Kanaan identifiers for cross-environment matching because internal database IDs can differ between installations.

### 2026 FSCA inspection readiness status — 28 July 2026

The external readiness records under `C:\Download\_kanaan\Compliance\FSCA inspections\2026\Readiness prep` were reconciled with the latest working documents on 28 July 2026.

Current prepared position:

- The approved 3 July 2025 RMCP is retained as the previous approved version.
- `Kanaan Business Risk Assessment 2026 - working draft.docx` is version 0.11, a management-review draft with confirmed business and governance facts.
- `Kanaan RMCP 2026 - revised working draft.docx` is version 0.10, a management-confirmed working draft linked to the BRA and KCAS controls.
- `Kanaan Governance and Organisational Structure 2026 - Revised.docx` records Andries below Andre and Gert operationally, with direct unrestricted compliance escalation to the Board.
- The 2026 Board approval resolution and four explanatory/factual letters are prepared but unsigned.
- The FIC Act/RMCP core training deck, MLCO/goAML/TFS operational deck, assessment and register are prepared for the scheduled 29 July 2026 session. The session is not recorded as complete until signed attendance, results and any remediation exist.
- Every-business-day goAML message-board checking commenced on 28 July 2026; Andre's separate backup access is confirmed and must be evidenced and tested.
- KCAS provides the technical evidence, screening, assessment, BRA/RMCP, monitoring and inspection-pack destination. The Badenhorst pilot proves the client workflow; the remaining population and TFS coverage remain operational work.

The submission and onsite dates are controlled separately. Draft preparation is not approval, signature or operating evidence. The final submission must use the exact signed/approved files and retain the delivery receipt, while transparent dated implementation actions may continue toward the onsite inspection.

### RMCP and BRA finalisation direction — 30 July 2026

The 30 July management review and client-file check established the following controlling direction for finalisation:

- The July RMCP and BRA working drafts remain source material but will not be issued as further management-review drafts. Clean final documents will be prepared directly for the Board approval pack.
- The RMCP will read as Kanaan's own adopted programme. Present-tense wording such as "Kanaan maintains", "Kanaan does not establish", "Kanaan takes" and "Kanaan determines" replaces external instructions such as "Kanaan must".
- Draft-only yellow/orange narrative callouts, historical implementation reports, management-confirmation language, outstanding questions and evidence-gap commentary will not appear in the final documents.
- Client engagement is predominantly face to face but also occurs directly by telephone or Zoom. Email alone is not onboarding. Remote engagement uses reliable independent identity verification and appropriate authentication controls and is assessed on its actual risk.
- Kanaan always establishes and records source of funds. The record may be in the application, a client declaration or other client-file information. Supporting documents are obtained where reasonably available or proportionate to risk; an old inheritance, pension or similar source does not fail solely because decades-old original proof is unavailable.
- The client-folder sample under `C:\Download\_kanaan\Clients` supports this operating position: dedicated declarations exist, new/general application forms commonly contain source-of-funds or source-of-wealth fields, and switches or amendments do not necessarily repeat onboarding information.
- The BRA will be Kanaan's self-assessment, not an external confirmation report. The confusing management dashboard will be removed, risk themes consolidated, inherent and residual risk defined plainly, and ratings recalculated proportionately. The outstanding client-by-client assessment programme is an implementation action and does not by itself make entity-wide residual risk High.
- The RMCP's current Annexure D section 42 table will not be retained in its present form because a separate annex is unnecessary and its statutory mapping contains errors. The RMCP will include a concise applicability statement, while the separate FSCA Appendix A/page mapping will still be completed for the inspection pack.
- Kanaan's financial-statement auditor is Stuart Edwards & Company, supported by the 2025 audited financial statements and management confirmation. The dashboard reference to "LGA auditors" is not used as the final fact. Kanaan refers to its external compliance support as The Corporate Counsel.
- The retained training proof covers both the 2024 training cycle under `RMCP and Policy Approval\05 Board approval pack\2026 FIC Act Training\2024 Training` and the signed 2026 training and assessment under `RMCP and Policy Approval\05 Board approval pack\2026 FIC Act Training\Signed 2026 Training`. The RMCP states the continuing training control directly: training before unsupervised affected work, at least annual refresher training and additional training after material legal, risk or procedural change.
- Short tables will be kept together where practicable; longer tables will use repeated headers and controlled row splitting. All final pages will be rendered and visually inspected.
- Final Word documents and matching stable PDFs will be placed in `RMCP and Policy Approval\05 Board approval pack\RMCP and Business Risk Assessment`, and the Board resolution will identify the exact filenames, versions, dates, page counts and hashes.

### Final document preparation status — 30 July 2026

- `Kanaan Business Risk Assessment 2026.docx` and its matching PDF were prepared as version 1.0 in the Board approval pack. The stable PDF is 4 pages. The SHA-256 hashes are `025f1e5dfe370a6d202d73c182e322134253904ef4ac0771173624d1d3615b20` for the Word document and `55d13d7742e165049b533008fdcf366c5a885766ddf18f621cf7719f2f5de880` for the PDF.
- `Kanaan RMCP 2026.docx` and its matching PDF were prepared as version 1.0 in the Board approval pack. The stable PDF is 13 pages. The SHA-256 hashes are `8181c26dc8ea1e275a3544283ced39fee6a609dc667ba2f10e97e711ea448ad3` for the Word document and `9d964a1909e4784d9980e4b4ab8e74ad3d4f2e9232edfff045f49739b00ba0b7` for the PDF.
- Both final documents use Kanaan-owned present-tense wording, contain no yellow/orange draft callouts, comments or tracked changes, and passed structural and full-page visual QA. Short tables are kept together and longer tables do not split individual rows.
- The BRA records a Low to Moderate ordinary business profile and Low entity-wide residual risk using seven consolidated Kanaan-specific themes. The RMCP records direct face-to-face, telephone and Zoom engagement, the agreed source-of-funds practice, one authorised KI approval for High-risk clients, the retained 2024 and 2026 training evidence, Stuart Edwards & Company and The Corporate Counsel.
- At the close of document preparation on 30 July, these files were final for Board approval but were not yet signed or effective. That preparation-stage status was superseded by the signed approval and upload recorded below on 31 July 2026.

### Signed approval and FSCA upload status — 31 July 2026

- All five trustees signed the final four-page `Kanaan Business Risk Assessment 2026` and the final 13-page `Kanaan RMCP 2026` on 31 July 2026. The RMCP states that its effective date is the date of Board approval; it is therefore effective from 31 July 2026.
- The final FSCA response set was assembled under `RMCP and Policy Approval\06 Signed final` as numbered items 5.1–5.8. It contains the signed factual/governance confirmation, revised governance and organisational structure, signed BRA, signed 2026 RMCP, retained approved 2025 RMCP, 2023/2024/2025–2026 training archives, signed internal- and external-audit-position letters, and the monitoring dashboard/supporting records.
- The complete requested response set was uploaded to the FSCA on 31 July 2026. Kanaan sent the FSCA a follow-up email confirming that the upload had been completed.
- This closes the time-critical 3 August document-submission workstream. The submission does not close the separate operating-effectiveness programme for the 22 September onsite inspection.
- For durable delivery evidence, retain the sent confirmation email together with any portal receipt, upload acknowledgement or screenshot. Absence of a separate portal receipt does not change the recorded fact that Kanaan completed the upload and notified the FSCA.
- The signed BRA and RMCP approval tables do not contain a Board resolution/minute reference and retain the preparation-stage footer `Final for Board approval`. The completed trustee signatures and dates evidence the Board approval used for the submission. If a separate minute or written-resolution reference exists, record it in the governance register; this is a recordkeeping follow-up, not an outstanding FSCA upload item.
- The separate Appendix A section 42 page-mapping exercise is no longer treated as a blocker to the completed numbered upload. It remains available as internal inspection-readiness support or for a later FSCA request and must use the final 13-page RMCP if completed.

### goAML daily-check evidence — Phase 1 technical delivery, 31 July 2026

- KCAS now has a dedicated `Compliance > goAML daily checks` workflow at `/compliance/goaml`. It opens the official goAML portal in a separate tab and creates one controlled daily check record per date without storing goAML credentials.
- A Compliance.Manage user records one of three outcomes: no new/actionable messages, a new message requiring action, or goAML unavailable. Successful access requires screenshot evidence. An unavailable result requires either screenshot evidence or an explanation, so notes are optional when the screenshot already shows the access problem. The evidenced unavailable result completes the daily requirement; Kanaan records the FIC-system failure and carries on with its business rather than requiring repeated access attempts that day.
- Browser-selected PNG/JPEG screenshots are converted before upload to a readable JPEG no larger than 1600 × 1000. KCAS stores the filename, protected server path, byte count and SHA-256 hash and serves the image only through a Compliance.View-authorised endpoint.
- The evidence base folder, official portal URL, tracking start date, local due hour and backup checker are editable in KCAS. The default base folder is `C:\Download\_kanaan\Compliance\dailygoAML`; KCAS creates year/month subfolders automatically.
- A new/actionable goAML message automatically creates a High-priority item in the common compliance work register with the message reference, owner and due date. Missing required dates are shown as a red alert on both the goAML page and the main compliance dashboard.
- Settings changes, check start/completion and generated work items create compliance audit events. Every completed daily outcome, including an evidenced FIC-system access failure, is immutable and closes the requirement for that date.
- Migration `20260731203226_AddGoAmlDailyChecks`, its reviewed targeted SQL script and the regenerated fresh-database schema are included. A pre-migration backup was retained at `backups\database\kcas_blazor-pre-goaml-20260731-2245.sql`, the migration was applied to the live KCAS database, and KCAS restarted successfully through Kestrel and the HTTPS proxy. The release build, migration-model check and all 187 automated tests passed; authenticated interactive-browser acceptance remains an operational follow-up because the in-app browser connector was unavailable during delivery.

### goAML laptop-to-live package transfer — Phase 2 technical delivery, 1 August 2026

- KCAS now provides `Compliance > goAML transfers` at `/compliance/goaml/transfers`. A Compliance.Manage user can export a selected range of completed laptop checks and later preview and apply that package on live KCAS without replacing either database.
- The `.kcas-goaml` package is encrypted with AES-256-GCM using a separately communicated passphrase derived with PBKDF2-SHA256. KCAS never stores the passphrase. The encrypted payload carries check metadata and the actual JPEG evidence because laptop evidence paths are not accessible to the live server.
- Export verifies every screenshot against its recorded SHA-256 hash. Import revalidates the payload, evidence type, size and hash before changing data, stores evidence beneath the live configured year/month folder, and retains the encrypted incoming package in protected shared storage.
- Preview is read-only. An identical live check date is skipped, a different live check date is a blocking conflict, and no existing check is overwritten. Package ID and content-hash records prevent duplicate application.
- Action-required checks recreate their High-priority compliance work item on live. Export, each imported check, recreated work items and package application create compliance audit events with the responsible user and mandatory reason.
- Migration `20260801093743_AddGoAmlTransfers`, its targeted upgrade SQL and the regenerated fresh-database schema add the transfer ledger used for idempotency and audit evidence.

## 15. Cross-cutting requirements

These apply to every phase:

- Least-privilege permissions and proportionate approval: representatives, the Compliance Officer or KIs may complete routine work; one designated KI signs off the operational client-risk methodology; one authorised KI approves High-risk clients and material exceptions, with the other KI as backup or conflict alternative. Formal BRA and RMCP adoption remains with Kanaan's Board of Trustees as the documented highest authority and must follow the trust deed, quorum and resolution requirements.
- Server-side authorisation, not menu hiding alone.
- Immutable history for approved or effective records.
- Effective dating and version pinning.
- Concurrency checks before applying decisions.
- Mandatory reasons for overrides, exceptions and sensitive decisions.
- Evidence provenance, secure storage references and retention controls.
- No deletion where supersession or closure is the appropriate audit-preserving action.
- Search, filtering, export and as-at-date reporting.
- Automated unit, integration, authorisation and migration tests.
- Representative business acceptance tests recorded per phase.
- POPIA-conscious handling of personal and special personal information.

## 16. Definition of done for the full programme

The programme is complete only when:

1. Historical and later legacy data can be reconciled repeatedly without overwriting KCAS work.
2. Client records contain sufficient, traceable evidence for risk assessment.
3. Approved client risk assessments are explainable, versioned and reviewable.
4. The BRA is supported by frozen portfolio evidence and documented management judgement.
5. The RMCP maps risks to approved controls, owners, monitoring and evidence.
6. Reviews, EDD, control testing and remediation operate in KCAS.
7. An inspection evidence pack can be reproduced for a stated date.
8. Security, audit, backup, recovery, training and operational ownership are accepted.
9. Every imported client is lifecycle-classified and every confirmed current client has completed the controlled population, verification and assessment workflow.

## 17. How to pause and resume

Before pausing:

1. Update the status table.
2. Record the last completed acceptance gate.
3. Set the current resume point.
4. List open decisions, blockers and any database migration not yet deployed.
5. Commit or otherwise preserve the work according to the user's instruction.

When resuming, use this instruction:

> Resume the KCAS RMCP/BRA implementation from `docs/RMCP_BRA_IMPLEMENTATION_PLAN.md`. Verify the repository and database state against the recorded resume point before changing anything.

Do not infer completion from code alone. Recheck the acceptance evidence and deployment state first.

## 18. Current open decisions

Current open decisions and operational follow-up:

- Retain the FSCA confirmation email and any available portal receipt, upload acknowledgement or screenshot with the signed-final evidence set.
- If a separate Board minute or written-resolution reference exists for the 31 July approval, record that reference in the governance register. The BRA and RMCP themselves were signed by all five trustees on 31 July 2026 and were uploaded as the approved response documents.
- Continue Phase 8 lifecycle classification, human verification, evidence readiness, screening and client-risk assessment across the imported population. Do not infer full client-population or TFS coverage from completion of the document upload.
- Complete representative control testing, goAML continuity evidence, a mock inspection/retrieval exercise and backup/restore evidence before the 22 September onsite inspection.
- The separate Appendix A section 42 page mapping may be completed as supporting inspection evidence if operationally useful or requested by the FSCA; any mapping must use the final 13-page RMCP and must not reintroduce the superseded draft Annexure D analysis.
- External screening integration, notification channels, long-term retention refinements and any inspector-access mechanism remain later operational choices; they do not replace the immediate KCAS and controlled-document evidence.
