# KCAS Employee Compliance Workflow

Status: implemented and verified locally on 2 October 2026. Live deployment and
actual employee checks/management decisions remain pending.
The sourced baseline is not screening clearance, an appointment or an approval.
Execution handoff added on 2 October 2026: employee evidence review may continue in
a separate authorised Codex session on live when management supplies the folders.

## Controlling Documents

Paths are relative to the private `Compliance` repository, not GitHub attachments.

- RMCP working revision 1.8 KCAS employee workflow, sections 19.1-19.4.
- Internal Compliance Function Policy 2026, section 5.2.
- Compliance Policy and Monitoring Methodology 2026, Appendix A8.
- Fit and Proper Policy 2026, section 3 and Appendices A-B, for actual regulated roles.
- Cybersecurity Strategy and Framework 2026, sections 5.1 and 6.3, for access/change controls.
- Controlled employee role-risk schedule under
  `FSCA inspections/2026/22Sep2026FeedbackRemediation/03 Evidence follow-up/Employee compliance`.

Private personnel names, IDs, evidence, live permissions and individual findings belong in
the restricted compliance store, not this source-control document or fixture data.
The private schedule starts from the six current employees confirmed by management;
former staff in older submissions are excluded from the active population.

## Basis and Scope

FIC Directive 8, paragraphs 2.3-2.7, requires a risk-based periodic competence/integrity
method, TFS scrutiny and records of method/outcomes. PCC55 supplies the interpretation:
paragraphs 2.2-2.10 and 2.12-2.16 address role extent and periodic screening;
paragraphs 3.1-3.6 address all-employee TFS and list-change triggers.
Use the retained official references; do not treat every optional example as compulsory.

This is a small compliance register and guided review, not payroll or a full HR system.
Employee role exposure is not a client risk band, a personal misconduct finding or a
second numerical BRA. Appointments, employment, employee review and account access are
different records. The absence of a login never excludes an employee from TFS coverage.

## Current Baseline

The pre-implementation read-only inspection found governance assignments, Identity users/roles, compliance
tasks/evidence/audit, client screening and controlled documents. No employee table or
dedicated employee review workflow was found. The new employee register supplements
these records. `/security` manages accounts and roles; that page does not establish
screening, legal authority or external-system permissions.
Governance roles can supply sourced duties, not fabricated review outcomes.

Reuse existing task/audit/evidence conventions where they meet personnel confidentiality.
Do not reuse client IDs, client-risk scoring or client/family transfer visibility as an
employee-record authorization shortcut. Do not reintroduce client folder scans.

## Workflow Contract

| Stage | Required behaviour | Evidence |
|---|---|---|
| Identify | One employee identity; legal/display names and aliases; current/prospective/inactive status; optional linked accounts and appointments. | Actual identifier source and dates; no synthetic employment start date. |
| Duties/access | Prefill sourced responsibilities; deliberate update; approved scope separate from actual KCAS/external access. | Source, system, access scope, verifier, date and change confirmation. |
| Role assessment | Reasoned combined-role exposure and proportionate check selection; approved review interval. | ML/TF/PF rationale, selected extent and schedule version. |
| Checks | Reuse relevant existing evidence; distinguish historical evidence and current-cycle requirements. | Check type, applicability/reason, finding, source/date, performer and limitations. |
| TFS | All current staff; prospective staff before appointment; list updates create current-population coverage work. | Identifiers, official source/list version, actual time, results and match resolution. |
| Decide | One uninvolved authorised decision; no self-approval; relevant management escalation. | Decision, reason, reviewer, evidence versions and restrictions/follow-up. |
| Access | Validate prerequisites for new sensitive duties/grants; record actual technical action separately. | KCAS permission audit or external responsible-owner confirmation. |
| Review/leave | Due and event tasks; preserve completed history; inactive leavers removed from future batches. | Tasks, outcomes, access removal and appropriate retention/legal hold. |

## Permissions and Decision Rules

- Menu: `Compliance -> Employee compliance`; read-only summary first, deliberate
  update and record-check actions second, consistent with other compliance pages.
- Personnel read/edit rights require need-to-know authorization; an ordinary client or
  governance read permission does not confer access to identity/disciplinary evidence.
- Existing Administrator/ComplianceAdministrator/ComplianceApprover responsibilities
  do not authorize self-approval or override a legal prohibition.
- An appointed uninvolved management reviewer receives a narrowly scoped employee
  decision capability. Do not grant broad ComplianceApprover rights to every KI.
- Resolve multiple accounts to the same employee before self-approval checks. Technical
  account administration must not bypass this rule; preserve audit/evidence versions.
- Generic admin access can modify software outside the application. Application checks
  do not certify independence against a server/DB administrator; privileged-change
  monitoring and uninvolved review remain necessary.
- Verify permission, current prerequisites, conflicts, evidence version and task state
  in services/endpoints, not only page buttons. Handle concurrent changes explicitly.
- A possible name match is not a confirmed designation or disciplinary finding; a
  failed source is not no match. Restriction/reporting follows RMCP sections 11 and 14.
- Actual salaries, employment action and external-system permissions remain outside
  this module unless explicitly integrated; record required action, not false execution.

## Reviews, Notifications and Evidence

- Periodic dates use the approved role interval and actual completed review date.
  The private draft proposes annual heightened-role and 24-month standard support-role
  review, subject to approval. Do not activate those dates from the drafting date.
- TFS list-change coverage is independent of periodic competence/integrity dates.
  Detect a change or accept a controlled manual notice with source/version; queue a
  batch snapshot of all current staff, track each result, show failure/incomplete state.
- Daily automated screening is not promised. A scheduler or Codex-run job cannot
  claim success if the source failed, an identifier is insufficient or someone is omitted.
- New/due/role/access/integrity/list events create idempotent tasks for the authorized
  Compliance audience, plus an uninvolved reviewer decision where relevant.
- Show task-created/failed feedback; retain actual recipients/creation/acknowledgement.
  Do not label an in-app task as email sent. Do not invent dates or completed recipients.
- Screening performer is Manual/person or Codex; saving/authorizing human audit actor
  is separate. Preserve actual time, source URL/list version, scope, result and evidence.
- Restricted evidence paths must follow cross-platform resolution and local/live mapping
  contracts. Authorize the file endpoint itself. No private evidence in public reports,
  ordinary client/advice/family transfers or GitHub.
- Employee transfers use their own confidential, Administrator-only package scope;
  personnel records are not added to client/family/advice transfers.
- Retention follows the relevant personnel/FAIS/privacy/legal-hold category, not a
  blanket client-termination clock. Inactive staff history is not silently deleted.

## Implementation Sequence

- [x] Read the submissions, current policies and actual read-only KCAS baseline.
- [x] Document the procedure and private six-person role/evidence schedule as drafts.
- [ ] Obtain the actual schedule/role decisions and replacement policy approvals.
- [x] Design minimal employee/identity/account joins, versioned review/check/decision
  records, restricted evidence/access confirmations and list-update batches.
- [x] Implement one guided review using existing task/audit patterns and separate
  decision permissions; retain visible next action and genuine completed summaries.
- [x] Implement service-level gates, due/event/list work and personnel file authorization.
- [x] Import only sourced baselines locally as pending, never completed/approved screenings.
- [x] Verify the local additive migration, focused/shared-contract tests, cross-platform
  evidence paths and desktop/mobile UI.
- [ ] Deploy to live, verify its private evidence root and retain an operating demonstration
  and staff briefing. Complete the actual checks and uninvolved decisions.

## Verification Cases

| Case | Required outcome |
|---|---|
| Employee without login | Included in review and TFS population; no automatic account creation. |
| Aliases or two logins | One employee; screening includes verified aliases; both accounts barred from self-approval. |
| Former employee | Excluded from current TFS batches, retained historical records and actual access follow-up. |
| Proposed schedule/historical training | No synthetic screened/approved status or last-review date. |
| Manual versus Codex check | Performer and actual timestamp/source distinct from saving user. |
| All checks satisfied | One uninvolved authorized decision permitted; exact evidence/version retained. |
| Wrong role/self-approval/direct endpoint | Denied, including a second linked account. |
| Restricted evidence/client export | Personnel evidence inaccessible to unauthorized users and excluded from client transfers. |
| Missing applicable competence/integrity/TFS | New sensitive duty/grant not silently approved; proportionate existing-staff follow-up recorded. |
| Similar name | Unresolved pending identifiers or evidence-supported false-positive decision, not automatic no-match. |
| Confirmed designation | Relevant prohibition/response retained; ordinary override cannot clear it. |
| Source unavailable or partial batch | Failure/remaining employees shown; no false completion. |
| Approved due interval | Actual completed date used; tasks idempotent, no invented past due review. |
| Role/access/list change | Relevant new checks and tasks; list changes cover all current staff, not just heightened roles. |
| Concurrent evidence/permission change | Stale approval rejected or explicitly re-reviewed against current prerequisites. |
| External-access checklist | Not proof of a technical change; actual responsible-owner action retained. |
| Upgrade or mapping | Historical decisions/evidence preserved; mapped file endpoint authorized on Windows and Linux. |

## Implemented Operation

1. Open `Compliance -> Employee compliance` (`/compliance/employees`). Search the
   register by employee or alias and open the read-only profile. Deliberate profile
   changes, check recording, access confirmation and decisions use separate pages.
2. Select **Start review**. Record actual TFS, identity, competence and integrity
   findings, plus the selected applicable training/regulated-role/additional checks.
   Linked source documents are available but never count as completed checks by themselves.
3. Confirm actual KCAS and external-system access against authorised duties. The
   service freezes the actual KCAS account approval/role/permission snapshot; external
   confirmation records the responsible owner's action rather than claiming software
   executed a change.
4. An uninvolved, employee-linked authorised reviewer records the management decision.
   The `EmployeeReviewer` role provides narrow personnel review rights and
   `Compliance.View`, not broad client-compliance approval. Assign it deliberately
   under `Security` to an appropriate existing manager; no individual role assignment
   was made by the baseline import. Self-approval and preparer approval are prohibited,
   including a second account linked to the same person.
5. Approval freezes the profile, evidence and decision, calculates the next date from
   actual completion and the approved interval, and closes applicable review tasks.
   Restrictions/follow-up remain visible and do not manufacture a completed review.

New, changed and due work appears in **Home** and **Employee compliance -> Review
tasks** for currently authorised personnel users. Task creation, actual in-app
recipients and acknowledgement are retained; acknowledgement is not completion and
no email is claimed. The hourly reminder job and page/login refresh queue due work
idempotently. Dates do not start from a draft or imported historical evidence.

**TFS coverage** accepts a controlled notice with the actual official source/list
version and snapshots all current staff, including those without a login. Actual
checks must be recorded against that source/version. This release does not fetch
list changes or perform automatic web searches. Source failures and unresolved
matches remain pending; a confirmed designation cannot be cleared by an ordinary
new review or administrator override.

Sensitive new grants/account activation for linked employees check prerequisites in
the service. Existing rights are not automatically revoked. Narrow reviewer-role
assignment to an identified current manager is the explicit transition exception
to avoid circular initial approvals; it does not grant appointment authority or
permit self-approval. Role/access changes create fresh verification work.

## Local Verification and Deployment

- Additive migration: `20261002105448_AddEmployeeCompliance`, ten new tables. The
  fresh-install schema remains a separate artifact; the existing database was not
  rebuilt and client/investment data was not replaced.
- Local private baseline: six current profiles, four verified account links and
  29 existing source-document links. All reviews remain **Not started**, with
  initial review tasks. There are no imported screening results, access clearances
  or management approvals. Names/identifiers/documents are not fixture or seed data
  in this repository.
- Verification: successful application build/restart; 27 employee, security and
  compliance-task tests passed; ten desktop/mobile page checks passed, including
  alias search. Tests cover service permissions, protected endpoints, stale evidence,
  review independence, preserved adverse findings, due tasks and portable evidence
  mapping. This is not a claim that the full test suite or live operating review ran.
- Personnel authorization rechecks approved account and current database permissions,
  including file endpoints. Ordinary client/governance readers do not receive access.
- Configure `EmployeeCompliance:EvidenceRoot` for the server's private Compliance
  directory where necessary. Windows defaults resolve the existing local Compliance
  root or the live `E:\Userdata\Kanaan Trust\Compliance` root. Relative paths and
  supported local/live/remote Compliance paths map within that root; traversal,
  outside-root and reparse-point paths are rejected. Changed file hashes require
  fresh evidence review rather than silently presenting the old approved source.
- Code deployment applies the new module, not local employee records. Use the
  dedicated encrypted employee transfer workflow below to move those records after
  deployment. The original authorised JSON baseline import remains available for
  initial sourced data, but is not a review-history transfer. Never publish either
  personnel JSON or a decrypted employee package.

The database inventory is checked separately at handoff; no temporary schema or
database deletion is required by this module.

## Employee Transfers

Implemented locally on 2 October 2026 after the user requested the same controlled
transfer capability as the other modules.

Menu: `Compliance -> Employee compliance -> Transfer employees`.
Route: `/compliance/employees/transfers`. Each employee also has a **Transfer
employee** link with that employee preselected. Transfers require an approved
current Administrator at the page, service and download endpoint; the new
`Employees.Transfer` permission is granted only to the standard Administrator role.

1. Select one employee or **Select all employees**. Pending/draft records are
   allowed and remain pending/draft. Enter a reason and matching passphrase of at
   least seven characters, create the encrypted package, then select **Download
   package**. Extension: `.kcas-employees`.
2. On live, open the same page, choose that file, enter its passphrase and select
   **Preview package**. It lists each employee, proposed action, reviews, checks,
   document counts, conflicts and mapping/follow-up notes. Preview is read-only.
3. Confirm the source/employees and enter the import reason. **Apply employee
   package** rechecks current live state before an all-or-nothing database apply.
   Any changed target/identity/file conflicts require a fresh preview or resolution.

The package is AES-256-GCM encrypted with PBKDF2-SHA256 and a random salt/nonce.
The passphrase is not stored. Package limit: 50 MB; individual evidence file limit:
20 MB. It includes the profile, verified account identity links, actual checks,
access confirmations, review/decision history, source audit, tasks, list-batch
references and referenced document bytes. Source performer, time and frozen approval
snapshots are retained. IDs are mapped; usernames/emails match existing live accounts.
Missing accounts are reported and not created. No roles, passwords or access rights
are copied or granted. An approval reviewer must exist on live or be included in
the employee bundle; the service never invents that person.

Existing identical files are mapped to the configured live Compliance root. A
missing linked document is restored under restricted
`Compliance/KCAS employee transfers/Evidence` and its hash verified. A different
existing file is blocked, not overwritten. No folder scan is performed. Imported
actor labels remain available without treating an absent historical actor as a new
live login. Historical task recipients/acknowledgements are retained as source
history; a separate live follow-up task records the actual current live recipients.

Stable employee transfer keys and private per-source receipts support repeat
imports and updates. Reapplying a recorded package is skipped. A newer source
revision may update only an unchanged previously imported employee; changed live
work, older unrecorded packages, altered immutable findings/decisions or missing
history are blocked. An independently created live profile is not silently replaced.

Source approval is retained as source history, not renewed approval of target
access. When review history is imported, the live profile requires a fresh review
against live access/current requirements. Historical checks are not automatically
re-certified as new live screening. No current screening/approval was added to the
six pending local baselines by implementing or testing transfers.

Additive migration: `20261002132242_AddEmployeeTransfers`; adds one private transfer
record table and a unique stable employee transfer key, backfilling only that key for
existing employees. Both employee migrations and matching SQL artifacts are included
for normal deployment; personal employee rows are never migration seed data.

Verification: 33 employee/security/task tests passed, including six transfer cases
using distinct source/live files and re-created target records with different IDs.
Tests cover encrypted integrity, account mapping without grants, evidence restoration,
conflict preview, retained approvals/reviewer identity, idempotence and live-change
protection. Five desktop/mobile transfer screenshots and the real local six-profile
encrypted export/download/preview checks passed. No actual employee package was
applied to the local originals or live server during browser QA.

## Live Codex Evidence Review and Session Handoff

Agreed on 2 October 2026: management will supply access to the actual employee
folders on live. Codex should read and assess their contents, link supporting
evidence and record supportable findings in KCAS, rather than merely list files or
leave all substantive work to manual form entry. Finish the evidence work that can
be supported before reporting the remaining genuine gaps. This agreement does not
authorise fabricated results, impersonated approvals or bypassing KCAS controls.

### Resume State and Prerequisites

- The local baseline contains six current profiles, four account links and 29
  document links. All six full reviews were Not started at this handoff. No current
  integrity/TFS clearance, access confirmation or management decision was produced
  by developing the module. Inspect actual live state; do not assume it matches.
- Confirm the employee module and both additive migrations are deployed. Deployment
  does not copy employee rows. Preview/apply the private pending employee bundle
  through the transfer workflow if it has not already been imported. Do not replace
  subsequent live work or apply the bundle again blindly.
- Read the live repository's `AGENTS.md`, this plan, the private employee role-risk
  schedule and its status record, RMCP sections 19.1-19.4 and the named controlling
  policies. Draft recommendations are not approved appointments or decisions.
- Obtain the precise employee-folder paths and authorised scope from management.
  Keep paths, identifiers and personal findings in restricted records, not this
  repository plan. Start with the requested employee, then the remaining current
  population; exclude former employees from new screening while retaining history.
- Inspect the live evidence-root configuration, actual account links, permissions
  and reviewer identities. Do not treat local account roles as verified live access.
- A personnel folder outside the configured evidence root may be readable by Codex
  but not linkable through KCAS. Use approved restricted evidence storage and retain
  provenance; obtain authorisation for copies where needed. Do not broaden the root
  to an entire drive, expose personnel folders or bypass file-authorisation checks.

### Evidence to Read and Record

The following is the agreed practical review method, not a universal demand for
every certificate or optional background check. Select the extent using actual
duties, role exposure, approved policy and any relevant concerns.

| Area | Supporting material and review | Boundary |
|---|---|---|
| Identity | Read verified personnel/identity records; reconcile legal name, identifiers, aliases and linked accounts. | Do not guess identity or create a second employee for an alias. |
| Duties and authority | Read appointments, governance records and management-confirmed responsibilities; identify permitted actions and limits. | A system role, biography or passed exam does not establish legal appointment. |
| Competence | Assess relevant qualifications, work history, accreditations and recorded management/work-performance evidence against assigned duties. Reuse valid historical qualification evidence. | Document why the evidence supports the actual functions; do not demand unrelated qualifications or infer current competence from length of service alone. |
| AML/RMCP training | Inspect actual completed papers, register, dates and recorded results. Credit the six located July 2026 papers for what they establish. | Preserve blank scores and historical dates. A blank questionnaire is not a completed assessment; 2026 training is not evidence of 2025 attendance. |
| Regulated competence | For actually appointed regulated roles, examine applicable qualifications/exams, scope, training and current-cycle CPD evidence. Research existing Compliance records before asking for more. | Old certificates/DOFA are not proof of current-cycle completion. Support-only roles do not automatically inherit adviser/KI requirements. |
| Integrity | Examine current relevant employee disclosures, available disciplinary/employment/regulatory conduct records and management assessments. Perform and retain proportionate corroborating checks for heightened exposure or concerns. | A dated email may evidence a disclosure, subject to policy formalities. Neither a declaration, family relationship, silence, training nor a clear sanctions result alone establishes integrity. Do not claim a criminal-record check from an internet search. |
| TFS | Perform actual searches using verified identity/aliases against applicable official information. Retain source/list version, search scope, date/time, result and possible-match resolution. | All current employees are covered. Failed/unavailable sources or unresolved identities/matches remain pending, not NoMatch. |
| Access | Inspect actual KCAS account approval, roles and permissions; compare them with authorised duties. Review responsible-owner evidence of external access and action. | Local/imported roles are not live verification. Job title does not prove bank/platform/payment rights; ask for a focused confirmation where external access cannot be verified. |
| Decision and interval | Present supported findings, restrictions and remaining gaps to an uninvolved authorised reviewer; use the actual approved interval. | Codex may prepare the record, not manufacture the employee's disclosure, the owner's confirmation or management approval. No self/preparer approval or backdated decision. |

For integrity screening, distinguish the review of actual conduct/regulatory evidence
from an assertion that no criminal record exists. Police clearance, credit searches
and other additional checks are selected only where justified by the applicable
requirements and role risk. Record the method, extent and limitations; do not impose
every optional example as a blanket requirement or treat all public-source silence
as clearance. Retained Directive 8 paragraphs 2.3-2.7 and PCC55 paragraphs 2.2-2.10
provide the underlying risk-based screening framework.

### Execute in KCAS, Not Just a Written Report

1. Inspect existing profile, review history and linked evidence. Reuse valid records
   without duplicating employees, rewriting frozen history or erasing adverse facts.
2. Read the actual documents, including relevant pages/annotations and visual content
   where extraction is incomplete. Classify identity, qualifications, training,
   disclosure and appointment evidence by what each document actually establishes.
3. Start or continue the appropriate review and link the supporting files. Retain
   document date/version, relevant page or passage, provenance and stored file hash.
   Do not move, edit or delete original personnel records without authorisation.
4. Record each supported check with actual outcome, performer **Codex**, actual
   review/search date and time, precise sources, identifiers/scope, finding and
   limitations. The authenticated human saving/audit actor remains separate.
   Historical evidence keeps its original date; the present review is dated now.
5. Carry out current screening rather than infer it from the presence of an old
   search. Preserve the actual evidence. Resolve possible matches using identifiers;
   record failure or follow-up honestly if the source or identity is insufficient.
6. Verify what actual access can be observed. Collect only the missing external-owner
   or employee confirmations, with specific questions grounded in the records already
   read. Do not mark an external action executed merely because it was requested.
7. Use KCAS's authenticated workflow or existing application services under the
   authorised actor, preserving permission checks, validation, transactions and audit.
   If an authorised service runner is needed, first inspect the service contracts and
   keep the runner outside the tracked live repository. Do not force database status
   flags, forge claims/approvals or bypass a blocker to make the review appear complete.
8. Submit the supported record for the actual uninvolved management decision. If a
   check or required confirmation is genuinely missing, keep the relevant status
   pending and give the precise evidence/question needed, not a generic refusal to
   review the folders or a demand to repeat existing valid evidence.
9. Verify the saved employee view, source-file access, actor/time attribution, pending
   tasks and next action. Check for duplicate checks/tasks and retained earlier
   findings. Do not describe a draft or source-only approval as current live clearance.

If authorised recording is genuinely unavailable, complete the read-only evidence
research and retain a restricted findings index. Report the exact failed operation
and remaining recording work; do not claim the KCAS records were saved. An inaccessible
browser alone is not proof that all existing authorised application-service routes
are unavailable. Do not create a new privileged endpoint as a workaround without a
separately authorised implementation task.

### Progress and Completion Report

Maintain a restricted `EMPLOYEE_EVIDENCE_EXECUTION_PROGRESS.md` beside the private
employee schedule, not in Git. For each employee record: actual folder(s), evidence
reviewed and linked, check outcomes and sources/times, access findings, outstanding
confirmations, decision state and next action. Keep sensitive findings restricted.
Update after each employee so a later session can resume without repeating work.

The final report should distinguish **evidence complete / checks complete / access
verified / awaiting management decision / approved / follow-up required**. Identify
the actual reviewer needed and genuine gaps. Do not call a completed evidence review
a completed full employee review while approval or required verification is pending.
Follow live `AGENTS.md` for read-only database inventory and any database-affecting
operations; never create temporary schemas or delete data as routine evidence work.

### Moving the Plan to the Other Session

Another Codex session does not automatically inherit this conversation. Supply this
plan, the private schedule/status record and the folder locations, then authorise
execution. If this plan is deployed from Git, read its tracked copy but keep live
working notes outside the repository. If copying it manually, place it outside the
deployment repository, for example under
`Compliance/FSCA inspections/2026/22Sep2026FeedbackRemediation/03 Evidence follow-up/Employee compliance`.
This avoids an untracked/modified repository file blocking the deployment script.
Never include personnel evidence, credentials or decrypted packages in a commit.

Suggested live-session instruction:

> Read the KCAS employee compliance workflow plan, especially Live Codex Evidence
> Review and Session Handoff, and the private employee schedule/status. The authorised
> employee folders are [provide paths]. Check the deployed/live state first, then
> read the actual files, link evidence and record all supportable employee checks in
> KCAS with Codex attribution. Preserve originals and audit history. Research existing
> records before asking for missing information, retain restricted progress, and report
> exactly what remains for owner confirmation or uninvolved management approval.

Writing this handoff did not access live personnel folders, execute screenings,
change employee records or grant permissions. Live execution remains pending.
