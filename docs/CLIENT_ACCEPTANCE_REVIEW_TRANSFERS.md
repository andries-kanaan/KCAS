# Client Acceptance Review Transfers

Implemented scope: existing client, family and partial review packages also carry
acceptance preparation, Codex handoff findings and actual KI decision history.
These records remain confidential package data, not source-control seed data.

## Contents

- Requested service, representative, purpose/funds, actual disclosure version,
  delivery reference/date, enhanced measures and recorded relationship authority.
- Source Codex handoffs, findings, actual original times and source recipient names.
- Actual KI decisions, original actor/assignment references, reasons, source hashes
  and complete frozen source snapshots. Imported decisions retain their source key
  across later exports and do not acquire target account rights.
- Portable supporting-evidence keys remapped to live evidence IDs. Original source
  snapshots stay intact; their original database IDs are source references.
- A source revision fingerprint and the post-import live acceptance fingerprint,
  retained in the existing client review transfer receipt.

## Workflow

Use **Compliance -> Review transfers**, `/compliance/review-transfers`, as before.
Preparation and history are included automatically when the client has those records.
The preview lists preparation, handoffs and decisions for a client or family member.

Apply maps the records within the existing per-client import transaction. It keeps
original source provenance, appends missing source decisions once and rechecks live
prerequisites after evidence/assessment mapping. Supported validated findings can be
reused without repeating a Codex check. A partial or unresolved review remains pending;
any live follow-up task uses the actual current approved Compliance recipients.
Historical source notifications are not recreated as sent live notifications.

On **Clients -> selected client -> Client acceptance**, the transferred record shows
source findings, source dates/authors, mapped supporting evidence and frozen decisions.
An imported acceptance remains a source decision in history. The authorised live KI
can confirm the current mapped scope using the ordinary decision operation. The import
does not grant permissions, authenticate a new client instruction or invent approval.

## Protection and Updates

- An initial package cannot replace existing native live acceptance work.
- A later package must follow the same recorded source lineage and cannot predate
  the previously applied export. Changed live preparation, tasks or decisions block
  replacement and are identified during preview.
- Apply locks the client and rechecks the live acceptance fingerprint before writes.
- Duplicate packages remain rejected by the existing import receipt. A subsequent
  source revision preserves earlier handoffs and source decision snapshots.
- Source snapshot integrity and portable evidence references are validated at preview.
- Family and ZIP imports use the same per-client rules. One client's source decision
  never accepts another family member.

## Deployment

Review package format 5 carries acceptance records. Formats 2, 3 and 4 remain readable;
their absent acceptance section does not erase live acceptance work.
Deploy the updated code on source and live before creating/importing format 5 packages.

`20261003133438_AddClientAcceptanceTransferProvenance` adds two nullable provenance
columns to the existing profile and handoff tables. The existing transfer receipt
stores source history and fingerprints; no client records or decisions are seeded.
Incremental SQL and the fresh-install schema are maintained with the migration.

Verification outcomes are recorded in `KCAS_CLIENT_WORKFLOW_IMPLEMENTATION.md`.
