# Client BRA-Linked ML / TF / PF Report

## Purpose and Status

Show a client's relevant ML, TF and PF scenarios before and after implemented
controls, with the actual calculation, reasoning, evidence and limitations.
This is a separate **proposed client linkage**, not a replacement for the current
client methodology, an amendment to a signed BRA/RMCP, or a KI acceptance.
The formal client rating and mandatory enhanced-measure rules remain unchanged.

The controlling calculation reference for this implementation is **Kanaan
Business Risk Assessment 2026 - working revision 1.5 practical risk scenarios**,
section 3 and the relevant section 4 scenarios. The latest working RMCP is
revision 1.9, particularly client risk/acceptance and monitoring. These working
documents must not be described as newly approved policies.

FIC Guidance Note 7B, paragraphs 13-15, distinguishes likelihood/impact and
inherent risk before controls from residual risk after implemented controls.
Paragraphs 48-52 discuss matrices and mitigation. Source retained in the
inspection reference material; official publication:
https://www.fic.gov.za/wp-content/uploads/2026/06/2026.6-GN-260611-G7_FIC-Guidance-Note-07B.pdf
The numerical 3-by-3 matrix is Kanaan's chosen tool, not a statutory formula.

## Calculation Rule

1. Define one actual client/service misuse scenario for each of ML, TF and PF.
2. Choose inherent likelihood and impact separately, each a whole number 1-3.
   Explain the client's exposure without double-counting controls.
3. Record the controls actually implemented, their evidence and why they affect
   this same scenario. Planned controls earn no current reduction.
4. Choose residual likelihood and impact separately and explain both.
5. Compute both products: likelihood x impact. Bands: Low 1-2, Moderate 3-4,
   High 6-9. The possible products are 1, 2, 3, 4, 6 and 9; 5 is not a result.
6. Retain impact unless a separate consequence-limiting basis justifies a change.
   Identity checks or a no-match screen do not automatically reduce consequences.
7. Do not force a reduction. A likelihood already at 1 cannot fall below 1.
   Effective controls can legitimately retain a Moderate or High residual result.
8. Do not average ML, TF and PF into a replacement overall rating. Existing High
   triggers, EDD and sanctions restrictions continue to apply independently.

Codex may prepare evidenced professional judgements under recorded user
authorisation, using the actual documents and screening records. KCAS performs
arithmetic and validates references; it does not infer input scores simply from
the number of completed checks. The manual alternative records its actual user.

## KCAS Workflow

- Clients > selected client > Compliance risk > **ML / TF / PF report**.
- Client acceptance also links to the same report and records it in the KI
  snapshot when an actual decision is subsequently made.
- The report shows comparison bars, both products/bands, four input reasons per
  scenario, mitigating evidence/performer/time, scope, limitations and history.
- Deliberate update page: `/clients/{id}/risk/bra/update`. Empty reports have no
  default risk scores; an update creates a new immutable report row.
- API: `ClientBraRiskReportService.RecordAsync`, using the actual authorised
  principal, audit reason, exact BRA reference and actual performer. Only current
  risk-preparation permission may record; viewing/restricted-client rules apply.
- Current verified client-owned evidence is required. Missing/changed files,
  expired evidence and unresolved escalation cannot support a reduction. A
  stale page or another recorded version cannot silently overwrite history.
- A client-check fingerprint identifies context changes. A new report changes
  the KI decision snapshot and requires revalidation of a previously validated
  handoff; it does not fabricate acceptance or restart the client's lifecycle.
- Printing uses the read-only report, including its proposed status and limits.

## Transfers and Deployment

- Additive migration `AddClientBraRiskReports` creates one table. No existing
  clients, funds, transactions, risk scores or methodology rows are rewritten.
- Client/family/partial review exports carry the latest report in package v4.
  The importer still accepts v2/v3 packages without this optional report.
- Control references use existing evidence keys, not source database IDs.
  Import remaps IDs and preserves performer, original date and source reference.
- An imported proposal is explicitly a source review needing live-context
  reconfirmation, not a claim that a new live check or KI decision occurred.
- A receiver running older code must be upgraded before importing v4 packages.
- Signed policies remain untouched. Adopt the client linkage formally through
  the normal BRA/RMCP/methodology approval process before calling it the approved
  replacement client method.

## Verification

Check matrix boundaries, missing inputs/reasons, unsupported controls, consequence
reductions without a basis, current-role/hidden-client restrictions, changed
evidence, concurrent/stale versions, preserved formal ratings and separate-environment
transfers. Use synthetic records and retain the test database. Record the actual
verification outcomes in the workflow implementation log after completion.
