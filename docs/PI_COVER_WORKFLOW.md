# PI cover register and renewal workflow

## Access

Compliance > Organisation and governance > PI cover, or `/compliance/insurance`.
The Compliance navigation also has a PI cover link. A policy year opens its terms,
supporting documents, findings and recorded history. Updates use a separate page.

- Compliance View: read the register and securely open linked evidence.
- Compliance Manage: capture terms, link/refresh evidence, request Codex, record a
  manual review, submission or insurer confirmation.
- Compliance Approve **and** an active matching Key Individual governance role:
  record acceptance. Administrative access alone is not a KI appointment.

## Policy years and evidence

Each year has a separate record and transfer identity. Capture the quoted or issued
dates and amounts accurately; do not silently correct an insurer's document.
Use Findings for errors and unresolved terms. The shared-limit setting distinguishes
a combined crime/civil limit from separate limits. A monthly instalment is nullable:
do not substitute annual premium divided by twelve for a confirmed arrangement.

Document references are stored as `Compliance/PI Cover/<year>/...`, with a SHA-256
fingerprint and optional document date. Original files are not uploaded, copied,
changed or scanned. The authenticated file endpoint checks permission, the permitted
PI directory and the fingerprint before serving the file. Junction/symlink traversal
is rejected. Link/refresh document at the same path records a new fingerprint and
invalidates the old review; a changed file is not silently accepted.

`Insurance:ComplianceRoot` can explicitly select the root **ending in Compliance**.
Without this setting KCAS uses the existing local Compliance folder, otherwise the
established live `E:\Userdata\Kanaan Trust\Compliance` root. Browser workstations do
not need access to that drive: KCAS serves the authenticated document from the server.

## Renewal

1. **Preparation:** create a new policy year; link proposal, no-claims declaration,
   financial statements and correspondence as applicable.
2. **Submitted:** record the actual submission date and supporting correspondence.
   Creating or signing a proposal is not evidence that it was sent.
3. **Quotation received:** record the actual quote and relevant receipt/reference.
   A quotation can also be captured directly without inventing earlier submission.
4. **Reviewed:** request a Codex renewal review and copy its handoff into the authorised
   session. Codex reads the actual files, compares prior cover, premiums, VAT, limits,
   excesses, conditions and payment terms, and saves a substantive review through
   `InsuranceService.RecordReviewAsync`. Its performer is **Codex**, with the actual
   saving account and time retained separately. A human may instead record a manual
   review. Changes to terms or linked evidence invalidate the previous review.
5. **Accepted:** the authorised KI records the decision, actual date and acceptance
   correspondence. Current review, resolved findings, valid period and unchanged
   evidence are required. A quotation is not acceptance evidence.
6. **Cover confirmed:** link the final policy schedule or written insurer confirmation
   and record the actual date. Acceptance does not itself establish binding. An issued
   historical schedule may be captured directly as confirmed for a past policy period;
   this does not fabricate a retrospective KI approval or proof of premium payment.

Accepted/confirmed terms and their supporting evidence are frozen. A later policy
year is a new record; corrections before acceptance remain audited in the current
year. Events retain occurrence date, recording timestamp, performer, saving account,
summary and any linked evidence identity.

## Reminders and decisions

Defaults are 60 days for preparation, 30 for follow-up and 7 for urgency before the
recorded expiry; these thresholds are configurable per policy. A background job
refreshes every six hours, and loading the Home notifications also refreshes tasks.
One controlled work item per policy prevents duplicate reminders. Compliance review
recipients receive in-app work; an eligible KI sees a separate acceptance action once
the current review is clear. These are **in-app notifications, not emails**.

A Codex request also creates work even when renewal is not yet due. Recording that
review clears the preparation request, but a due renewal remains open. Reading a
reminder or accepting a quote does not close renewal work. The old period is resolved
when the immediately following policy period has evidenced insurer confirmation.
Missing historical schedules therefore remain visible rather than being assumed.
Controlled PI tasks cannot be closed through the generic worklist closure controls.

## Local-to-live transfer

Use the existing Compliance programme transfer page (`/compliance/programme-transfers`).
Version-two programme bundles include PI records, document references/fingerprints,
workflow state and original event provenance. Apply requires the corresponding live
files to exist under the configured Compliance root and match the exported fingerprints.
Preview reports missing or different PI files before application. No scan is performed.
Newer or accepted/confirmed live snapshots cannot be overwritten by different source
terms. Identical snapshots are retained. Import adds an audit entry identifying the
importer without rewriting the original review or decision performers.

Both environments must deploy this feature before importing version-two bundles.
Version-one programme bundles remain readable and leave existing PI records untouched.
This is a programme bundle, not a new insurance-only transfer format.

## Deployment

The additive `AddInsuranceRegister` EF migration creates policies, document references,
events and the controlled reminder link. The matching incremental SQL and complete
schema are maintained alongside the other deployment scripts. Private policy contents
are application data, **not** seeded into migrations or committed fixtures. Deploying
code creates the schema; use a reviewed programme bundle to move captured records.
