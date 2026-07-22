# KCAS RMCP and Business Risk Assessment Implementation Plan

Status: Active source of truth  
Plan owner: Kanaan / KCAS  
Last updated: 2026-07-22  
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

These documents describe the intended target state but remain working drafts. They must not be represented in KCAS as approved or effective until the prescribed review and approval evidence exists.

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
| 0A | Safe scan and add-new reconciliation foundation | Code complete; not deployed to the working database | Deploy and complete controlled acceptance run |
| 0B | Reviewed field-by-field merge and reconciliation closure | Not started | Demonstrate review, approval, rejection and audit trail |
| 1 | Compliance foundation and controlled configuration | Not started | Configuration/versioning and permissions accepted |
| 2 | Client profile and evidence readiness | Not started | Pilot clients pass completeness and evidence checks |
| 3 | Client risk assessment workflow | Not started | Pilot assessments reproduce approved methodology |
| 4 | Business Risk Assessment | Not started | BRA approved from traceable evidence and methodology |
| 5 | RMCP control and approval management | Not started | Approved RMCP version links risks, controls and evidence |
| 6 | Monitoring, reviews and remediation | Not started | End-to-end review and escalation cases pass |
| 7 | Inspection readiness, reporting and rollout | Not started | Inspection pack, security, recovery and rollout accepted |

Current resume point: **Phase 0A controlled deployment and acceptance.**

No Phase 1 work should begin until both Phase 0A and Phase 0B have passed their gates.

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
- Migration and targeted deployment SQL.
- Automated reconciliation, recorder and protected-route tests.
- Isolated real-source rehearsal proving add-new followed by an idempotent scan.

#### Still required for acceptance

1. Back up the working KCAS database.
2. Review and approve the Phase 0 migration.
3. Apply the migration to the working database.
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

#### Deliverables

- Review queue grouped by run, source table, client and severity.
- Side-by-side baseline, incoming source and current KCAS values.
- Field decisions: keep KCAS, accept source, manually resolve, or defer.
- Mandatory reason for manual resolution and sensitive-field decisions.
- Record-level approval after field decisions are complete.
- Separation between reviewer and approver where configured.
- Optimistic concurrency so a review cannot overwrite newer KCAS work.
- Transactional application of approved changes.
- New accepted source snapshot only after successful approval/application.
- Rejection, reopening and superseded-review handling.
- Missing-from-source resolution without automatic deletion.
- Complete audit trail and reconciliation closure report.
- Permission set for viewing, reviewing, approving and administering imports.

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

### Acceptance gate 1

- A new methodology version can be drafted, reviewed, approved and activated.
- Existing records retain their original methodology version.
- Unauthorised users cannot change configuration or approvals.
- Every configuration and status change is auditable.

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

### Acceptance gate 2

- Representative natural-person, company and trust profiles can be completed.
- Mandatory evidence varies correctly by client type.
- Missing and expired items are visible and actionable.
- Relationship and beneficial-ownership information is traceable.
- A risk assessment cannot be finalised when blocking evidence is absent unless an authorised, reasoned exception is recorded.

## 9. Phase 3 — Client risk assessment workflow

### Objective

Produce consistent, explainable, reviewable and repeatable client risk assessments from the actual client profile and evidence.

### Risk dimensions

- Client and legal-form risk.
- Ownership/control complexity and transparency.
- Geographic and jurisdiction exposure.
- Product and service exposure.
- Delivery channel and non-face-to-face risk.
- Transaction/activity profile.
- Source of funds and source of wealth.
- PEP/prominent-influential-person exposure.
- Sanctions/TFS and adverse-information indicators.
- Other Kanaan-approved risk indicators.

### Deliverables

- Draft assessment created against a fixed methodology version.
- Automatic evidence-derived answers where reliable, with source links.
- Required assessor answers and narrative justification.
- Inherent risk, relevant controls and residual risk.
- Explainable calculation showing factor scores, weight/rule and result.
- Low, medium and high risk classification using approved thresholds and mandatory high-risk rules.
- Authorised override with mandatory reason and approval.
- EDD requirements and actions for elevated risk.
- Maker/reviewer approval workflow.
- Effective date, next review date, supersession and trigger-event reassessment.
- Frozen assessment snapshot so later client edits do not rewrite history.
- Client risk report and portfolio distribution dashboard.

### Acceptance gate 3

- An agreed pilot sample is scored manually and in KCAS with matching results.
- Every result is explainable from recorded evidence and methodology.
- High-risk mandatory rules cannot be neutralised by ordinary weighting.
- Overrides and exceptions require permission, reason and approval.
- Historical assessments remain unchanged after methodology or client-data updates.

## 10. Phase 4 — Business Risk Assessment

### Objective

Create and approve Kanaan's entity-wide BRA using traceable operational evidence and explicit management judgement.

### Deliverables

- Versioned BRA period, scope, methodology, preparer, reviewers and approvers.
- Risk universe covering clients, products/services, channels, geography, transactions/activity, delivery model and relevant external threats.
- Quantitative portfolio evidence drawn from approved client assessments.
- Qualitative business risk statements and supporting evidence.
- Inherent likelihood/impact or other approved risk method.
- Existing controls, control effectiveness and residual risk.
- Risk appetite/tolerance comparison.
- Required treatment actions, owners and due dates.
- Management judgement and limitations section.
- Review comments, approval workflow and immutable approved version.
- Comparison against the prior BRA and change explanations.
- Export matching the approved Kanaan BRA format.

The BRA must not be a simple average of client scores. Client results are evidence inputs; concentration, products, channels, external threats, controls and management judgement remain independently assessed.

### Acceptance gate 4

- Portfolio totals reconcile to approved client assessments as at a stated date.
- Every BRA conclusion links to evidence, methodology and recorded judgement.
- Control effectiveness and residual risk are separately visible.
- Treatment actions flow into the remediation workflow.
- The approved export is reproducible from the frozen BRA version.

## 11. Phase 5 — RMCP control and approval management

### Objective

Manage the RMCP as a controlled, approved programme linked to assessed risks and operational evidence.

### Deliverables

- Versioned RMCP record with scope, effective date, owner, status and review cycle.
- Structured sections aligned to the approved document structure and section 42 review analysis.
- Risk-to-control mapping from BRA risks to RMCP clauses and procedures.
- Control owner, frequency, evidence requirement, monitoring method and escalation path.
- Linkage to client-risk rules, EDD, screening, record keeping, reporting, training, governance and review procedures.
- Gap register between approved requirements and implemented controls.
- Draft, internal review, compliance review, board approval, effective and superseded states.
- Review comments and tracked change rationale.
- Board resolution metadata and signed-document attachment.
- Controlled RMCP export and version comparison.
- Review-date reminders and change-trigger workflow.

### Acceptance gate 5

- Every material BRA risk maps to one or more controls or an approved treatment action.
- Every control has an owner, evidence expectation and monitoring frequency.
- Approval stages and signed resolution are traceable.
- The effective RMCP cannot be edited; revisions create a new version.
- The generated document reconciles with the approved KCAS record.

## 12. Phase 6 — Monitoring, reviews and remediation

### Objective

Turn approved assessments and controls into recurring operational compliance work.

### Deliverables

- Periodic client-review schedule based on risk and policy.
- Trigger-event reviews for material client, ownership, product, geography, screening or activity changes.
- EDD case workflow and evidence checklist.
- Screening review and escalation records.
- Control-test schedule and sample/evidence results.
- BRA and RMCP treatment-action register.
- Findings, remediation actions, owner, due date, escalation and closure approval.
- Training assignment/evidence links where relevant to controls.
- Exception, breach and management-decision registers.
- Human-authorised suspicious/unusual activity case and decision record where later approved for scope; no autonomous regulatory filing.
- Overdue, high-risk and unresolved-item dashboards and notifications.

### Acceptance gate 6

- A periodic review and a trigger-event review complete end to end.
- A high-risk/EDD case shows evidence, decisions and approvals.
- An overdue remediation item escalates correctly.
- Control testing links results to the relevant RMCP control and BRA risk.
- Closure cannot occur without required evidence and authorised approval.

## 13. Phase 7 — Inspection readiness, reporting and controlled rollout

### Objective

Produce a defensible inspection evidence pack and put the complete solution into controlled operational use.

### Deliverables

- Inspection/case record for the FSCA notice and future reviews.
- Request register mapped to notice items, owner, due date, status and evidence.
- Document readiness register and gap dashboard.
- Evidence index linking clients, assessments, BRA versions, RMCP versions, approvals, training, monitoring and remediation.
- As-at-date reporting and reproducible exports.
- Read-only inspector/auditor access where approved.
- Access review, segregation-of-duties review and sensitive-data controls.
- Audit-log completeness and tamper-resistance verification.
- Backup, restore, rollback, retention and disaster-recovery rehearsal.
- Performance and volume testing using representative data.
- User acceptance testing with compliance and operational users.
- Pilot rollout, training, support procedure and staged production rollout.
- Post-implementation review and unresolved-risk register.

### Acceptance gate 7

- A mock inspection request can be answered from KCAS with a complete evidence index.
- Reports reproduce the approved records as at the selected date.
- Permissions and segregation of duties pass review.
- Backup restore and rollback are demonstrated.
- Pilot users approve the workflows and training.
- Production rollout and support ownership are formally accepted.

## 14. Cross-cutting requirements

These apply to every phase:

- Least-privilege permissions and explicit separation of preparation and approval where required.
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

## 15. Definition of done for the full programme

The programme is complete only when:

1. Historical and later legacy data can be reconciled repeatedly without overwriting KCAS work.
2. Client records contain sufficient, traceable evidence for risk assessment.
3. Approved client risk assessments are explainable, versioned and reviewable.
4. The BRA is supported by frozen portfolio evidence and documented management judgement.
5. The RMCP maps risks to approved controls, owners, monitoring and evidence.
6. Reviews, EDD, control testing and remediation operate in KCAS.
7. An inspection evidence pack can be reproduced for a stated date.
8. Security, audit, backup, recovery, training and operational ownership are accepted.

## 16. How to pause and resume

Before pausing:

1. Update the status table.
2. Record the last completed acceptance gate.
3. Set the current resume point.
4. List open decisions, blockers and any database migration not yet deployed.
5. Commit or otherwise preserve the work according to the user's instruction.

When resuming, use this instruction:

> Resume the KCAS RMCP/BRA implementation from `docs/RMCP_BRA_IMPLEMENTATION_PLAN.md`. Verify the repository and database state against the recorded resume point before changing anything.

Do not infer completion from code alone. Recheck the acceptance evidence and deployment state first.

## 17. Current open decisions

These decisions are deliberately deferred until their phase starts:

- The precise authorised roles and whether reviewer/approver separation is mandatory for every workflow.
- The approved client-risk factors, weights, mandatory rules, bands and review frequencies.
- The evidence-storage approach for sensitive client documents.
- The final BRA scoring methodology and risk appetite/tolerance formulation.
- The exact structured RMCP section model and generated document template.
- Which external screening or compliance systems, if any, will integrate with KCAS.
- Notification channels and escalation timeframes.
- Retention periods and read-only inspector access policy.

These are not blockers for Phase 0. They must be resolved before the relevant later phase's design is accepted.
