# KCAS Client Workflow: Agreed Design and Implementation Record

Updated: 2 October 2026. Status: documentation first; software implementation and deployment not yet performed for this workstream.

## Resume Here

The user agreed that KCAS must guide and enforce the documented client workflow, not merely store a checklist. One authorised Key Individual accepts an ordinary client after the required checks; a separate routine Compliance approval is not required. Compliance provides oversight and handles relevant escalations. The user expressly asked to agree and document this before software implementation, using the appropriate supporting policies rather than duplicating everything in the RMCP.

Next: review the aligned drafts with the user, then implement the gates and prove them against the cases below. Do not silently start application implementation during the documentation pass. Existing unrelated repository changes must be preserved.

## Controlling Documents

Paths below are relative to `Compliance`, not tied to the local or live drive.

| Document | Exact location | Owns |
|---|---|---|
| RMCP working revision 1.7 | `FSCA inspections/2026/22Sep2026FeedbackRemediation/02 Working BRA and RMCP revisions/Kanaan RMCP 2026 - working revision 1.7 KCAS client workflow.docx` | Section 3 authority; sections 5.2-5.4 prospect, screening/CDD, risk, acceptance and restrictions; sections 6-14 substantive requirements. |
| Code of Conduct 2026 | `Policies and Plans/Code of Conduct/2026/Code of Conduct 2026 - draft for approval.docx` | Section 2 retains the seven-stage procedure; section 2A specifies the employee's KCAS workflow, disclosures, advice, client authority, execution and servicing. |
| Internal Compliance Function Policy 2026 | `Policies and Plans/Internal Compliance Function Policy/2026/Internal Compliance Function Policy 2026 - draft for approval.docx` | Section 5.1 oversight, notifications and escalation; not routine client acceptance. |
| Compliance Policy and Monitoring Methodology 2026 | `Policies and Plans/Compliance Policy and Monitoring Methodology/2026/Compliance Policy and Monitoring Methodology 2026 - draft for approval.docx` | Appendix A7 operating-record sampling and implementation verification, within the existing monitoring cycle. |

These are working drafts, not approved replacement policies. Existing signed policies remain operative until properly replaced. The other ten policy drafts and BRA 1.5 ratings are unchanged by this workflow pass. The BRA must not credit a future system gate as an operating control.

## Agreed Boundaries

- Staff prepare; KCAS guides/checks; one authorised KI accepts; Compliance monitors and handles relevant escalations. No new blanket Compliance signature or two-KI requirement.
- KI authority must be tied to actual appointment/authorisation, not a generic Administrator or Compliance permission.
- Assessment completion, internal client acceptance, actual relationship commencement, advice approval, client acceptance of the proposal and implementation authority are separate facts.
- Screening follows sufficient identification and precedes acceptance. Additional parties discovered through CDD must be screened too. An unavailable source, unchecked party or unresolved possible match cannot become a no-match result.
- PEP/PIP exposure is not automatically a sanctions prohibition. Apply the RMCP's applicable category and enhanced requirements.
- Client compliance risk is not investment suitability. Preserve the separate methodologies and their versioned results.
- Introductory contact and internal preparation may occur while pending. Issuing approved client-specific advice and authorising implementation require the relevant gates.
- Client approval is obtained by sending the proposal: it is not a prerequisite to issuing that proposal. The existing independent advice review remains a separate control, not an extra client-acceptance decision.
- Staff remain responsible for actual verification. A linked file, checked box or AI consistency analysis alone does not prove document authenticity.
- Proportionate historical source explanations remain available as the RMCP permits. Do not add a forensic investment audit or positive-balance prerequisite to onboarding.
- Record required source-of-funds/purpose information for a new investment before its account exists; link that evidence to the actual account when created without inventing an account to satisfy a gate.
- Reuse valid evidence and preserve manual/Codex attribution. Do not reintroduce folder scans or overwrite selected evidence.
- KCAS can block its own controlled operations, not external emails or administrator platforms. Staff controls and subsequent monitoring cover those boundaries.
- No new automatic annual frequency for every client: use the approved methodology and applicable review triggers.

## Required Employee Workflow

| Step | Employee/decision | Required KCAS behaviour | Record |
|---|---|---|---|
| Enquiry | Capture prospect, requested service and responsible representative; provide initial disclosures. | Save without accepting; show next action; create/reuse notification without claiming email delivery. | Enquiry, service, disclosure version/date/delivery reference and task. |
| Screen | Check client and applicable persons, resolve identity ambiguity and findings. | Screening first; retain failure/unresolved states; extend coverage when new parties appear. | Identifiers, checker/assistance, timestamp, sources, results and resolution. |
| CDD | Verify applicable identity, authority, ownership, purpose/activity and funds information. | Client-type requirements; verified evidence reuse; supported alternatives distinguished from mandatory gaps. | Findings, provenance, party/account or proposed-contribution coverage and limitations. |
| Compliance risk | Complete applicable factors and enhanced measures. | Versioned methodology; current prerequisites; no dependence on invented investments. | Assessment, reasons, EDD and monitoring plan where required. |
| KI acceptance | Accept, decline or return for clarification. | Actual KI permission; current-state recheck in the decision operation; preserve decision basis and snapshot. | KI, time, scope, assessment/evidence versions and decision. |
| Advice | Prepare Risk Analyser/CAR from conversation/email and applicable current facts; independent review. | Existing draft/review controls; distinguish client-compliance and advice risk. | Case sources, risk methodology, suitability and reviewer. |
| Issue proposal | Send approved proposal for the client's decision. | Require valid internal approval and applicable acceptance; do not require the client's prior approval of the proposal. | Approved version, recipient and actual issue event. |
| Authority | Record response, valid mandate/instruction and applicable administrator requirements. | No silence-as-consent; scope/authentication/payment checks; changed material proposal returns to review. | Response, authority, forms and relevant disclosures. |
| Implementation | Submit, follow up and confirm execution. | Separate authorised, submitted, acknowledged, executed/rejected; do not infer execution from upload. | Administrator evidence and actual investment/transaction links. |
| Service | Review material changes, due tasks and ongoing service/reporting. | Role-appropriate task visibility; reopen relevant checks; retain earlier evidence and decisions. | Trigger/date, follow-up and new review outcome. |

Keep the interface compact: one next action, visible blockers, completed summaries with who/date/evidence, and links to existing forms. Avoid duplicate entry, hidden mandatory steps or requiring users to infer the next action from unrelated pages.

## Read-Only Baseline and Gaps

Code was inspected, not modified, for this design. Do not treat this list as exhaustive verification of all endpoints.

| Area | Observed baseline | Implementation work |
|---|---|---|
| `ClientOperationsService.SaveClientAsync` | Saves the client and creates an initial compliance task. | Distinct prospect/acceptance state; connect task and guided path. |
| `Client` | Lifecycle and evidence/risk records exist; no distinct onboarding acceptance fields identified. | Separate acceptance from lifecycle and client mandate commencement; audited history. |
| `ClientComplianceReviewService` | Seven review sections; investments precede evidence/screening; overall completion is not acceptance. | Onboarding-specific ordering/gates without breaking existing-client reviews. |
| `ClientRiskAssessmentService.FinaliseAsync` | Checks evidence, reconciliation, lifecycle, factors and methodology; enhanced cases await KI approval. | Avoid circular dependence for no-investment prospects; integrate/reuse enhanced approval without duplicate decisions. |
| `ClientRiskAssessmentService.ApproveAsync` | Initial inspection showed pending status and approval recording; full role/current-state enforcement still needs tracing. | Confirm role protection and revalidate current prerequisites in the service operation. |
| `ClientAdviceService` | Draft, independent review, approval for issue, issue and signed-document stages already exist. | Trace all issue/export/implementation operations; add applicable acceptance gates without blocking proposal issue awaiting client response. |
| Transfers and existing clients | Existing review history and transferred evidence must remain intact. | Preserve history, validate source/live identity and provenance; do not synthesize an acceptance that never happened. |

## Implementation Sequence

- [x] Agree responsibility: KI ordinary acceptance; no routine Compliance sign-off.
- [x] Inspect relevant working policies and selected current service code.
- [x] Draft aligned RMCP and specialist-policy additions with exact links.
- [x] Finish document rendering, preservation and cross-reference verification.
- [ ] User review of the aligned draft workflow before application implementation.
- [ ] Trace all relevant role policies, mutation/export paths and existing notification contracts.
- [ ] Design minimal acceptance state/history and any migration, including explicit existing-client transition treatment. No fabricated or backdated approvals.
- [ ] Implement service-level gates and current-state validation; preserve independent advice approval.
- [ ] Implement guided view and reuse existing evidence/risk/advice forms and tasks.
- [ ] Address transfer/import compatibility and preservation of decision provenance.
- [ ] Run focused regression tests, then broader shared-contract coverage where needed.
- [ ] Demonstrate the complete workflow, role restrictions and failed-path cases without sending real instructions or inventing approvals.
- [ ] Record deployment verification and brief affected staff; only then describe the tested controls as operational.

No database work has occurred in the documentation pass. Apply AGENTS.md inventory requirements before/after any later temporary schema work and before completion of material database work or any GitHub mutation.

## Acceptance Checks for Implementation

| Case | Expected result |
|---|---|
| Ordinary new natural person, no investments | Prospect can be saved; valid checks lead to one KI acceptance without Compliance approval or invented investment/reconciliation history. |
| Trust or legal person | Actual relevant persons and required ownership/authority evidence are covered; resolving the entity alone does not clear unchecked parties. |
| Screening unavailable or possible TFS match | No automatic clearance or acceptance. Resolving a false positive requires recorded evidence; a confirmed prohibition cannot be overridden by an admin. |
| PEP/PIP requiring enhanced treatment | Correct category/EDD and authorised approval, not automatic decline or blanket exemption. Reuse the single qualifying KI decision where it covers both approval purposes. |
| Missing mandatory CDD vs permitted alternative | Mandatory gap blocks; a policy-supported alternative records actual grounds and evidence without a universal exception button. |
| Stale page or concurrent update | Change an identifier, screening outcome or relevant evidence after opening approval: saving rechecks current state and cannot approve the stale snapshot. |
| Wrong role/direct call | Administrator-only and Compliance-only users cannot impersonate a KI; direct service/endpoint calls apply the same restrictions. |
| Client not yet accepted | Internal drafts and clearly labelled samples remain available; approved advice issue/implementation cannot bypass the acceptance gate. |
| Proposal awaiting client decision | Independent review and approved proposal issue can complete before client response. No client-confirmation issue blocker. |
| Missing authority or material proposal change | Implementation blocked until applicable authority/re-review exists; prior approval does not silently cover changed terms. |
| Submission without execution | Submitted/acknowledged is not marked executed; rejection or delay stays visible. |
| Annual/risk-based review and material event | Correct audience sees tasks and outcomes; relevant gates reopen without deleting the previous review or imposing universal annual re-onboarding. |
| Existing completed/historical review | Evidence and assessment remain; absence of a historic acceptance record is not filled with a synthetic sign-off. No blanket reset to prospect. |
| Family/partial transfer | Each legal client remains separately assessed/accepted; provenance survives and partial work cannot import as acceptance. |
| UI and evidence | Desktop/mobile readable; completed steps identify actor/date/evidence; no reinstated scans, duplicated forms or client-facing internal notes. |

## Source and Verification Notes

- RMCP revision 1.6, Code of Conduct section 2, Internal Compliance sections 3-8 and Monitoring Appendix A were read before amendments.
- FIC Act sections 21E, 42 and 42A checked against the retained FIC booklet and official FIC source. One-KI ordinary acceptance, screen order, snapshots and service-level gates are Kanaan implementation choices, not prescribed software or blanket statutory signatures.
- Existing legal footnotes and source lists must be preserved. New policy wording should not be mistaken for actual approval or operating evidence.
- Pre-edit draft copies are retained under `_kanaan/.work/inspection-transcripts/onboarding-source-backups`; build manifest, rendering and structural QA under the same work area.
- Final Word render: RMCP 1.7, 28 pages and 31 footnotes; Code of Conduct, 6 pages and 6 footnotes; Internal Compliance Function, 8 pages and 1 footnote; Compliance Monitoring, 5 pages and 5 footnotes. Contents refreshed; all rendered pages inspected. Original footnote text and pre-existing paragraphs are preserved except the logged version/authority/lifecycle clarifications. The prior RMCP 1.6 file is unchanged.
- No application code, database, live service or GitHub mutation was performed for this documentation pass.
