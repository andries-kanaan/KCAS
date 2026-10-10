# Investment funding history and CAR

KCAS displays current-investment CAR and capital-history CAR using its existing
dated-cash-flow XIRR engine. Capital history follows invested money, and is not a
claim that ownership remained unchanged when a personal account became joint.

## Automatic recognition

- Reuse an evidenced, current `Transferred` reconciliation link. A connection may
  cross records within the same recorded family; family membership alone is not
  a connection.
- Match terminal source payments against receiving inception capital. Separately
  received instalments can be matched as a consecutive receiving-capital sequence.
- Retain actual dates. Both sides of matched internal transfers are excluded from
  capital-history XIRR; current-investment XIRR retains the receiving contributions.
- Retain genuine withdrawals and later top-ups. Do not select arbitrary older
  withdrawals or infer missing FX amounts, fees, retained capital or allocations.
- Multiple original sources and ambiguous/partial allocations remain return-history
  issues. They do not reopen compliance assessments.

## Document-only connections

From an investment's returns page, open **Review funding history**. Existing links
are reused without another approval. Where correspondence or statements establish
a missing link, use the Codex handoff or record an evidenced connection manually.
If several receiving sequences match (for example, equal instalments), record the
actual final receiving date from the evidence to disambiguate the connection.

`InvestmentFundingService.RecordAsync` verifies current saving-account permission,
visibility of both clients, same-family scope, full cash-flow matching and cycles.
Codex results are recorded by an authorised Compliance review account with
`performer: "Codex"`; a manual save records the actual saving person. Evidence,
reason, performer, saving account/time and both historical fingerprints are retained.
No transaction, surrender date or compliance reconciliation is changed by this action.

The separate `InvestmentFundingConnections` table is an additive migration.
Historical fingerprints exclude local IDs, commentary, private paths and current
monthly valuations. Later contributions beyond the matched receiving dates do not
invalidate a connection; changes to the relied-on historical cash flows do.

## Presentation and limitations

The returns page shows both periods, named account owners, the chain, dated paid-out
and receiving movements, amount, evidence and reviewer. Its start is the earliest
established capital, not an invented original inception. An older partial source
must be allocated before extending the calculation further back.

Recorded ZAR cash flows can support a mixed local/offshore capital history. A native
currency return still requires amounts and currency evidence for every included
segment; KCAS does not invent historical conversions.

## Live transfers

Client/family review packages (format 6) include matched funding chains and stored
findings. Source/live matching uses client and account source identifiers, scoped
account metadata, and portable historical fingerprints, not local database IDs.
Existing formats 2-5 remain readable. Older applications must be deployed to the
new version before receiving format 6.

Preview warns when an account cannot be uniquely resolved or historical cash flows
differ. These warnings affect return-history availability, not compliance import.
Unresolvable provenance remains in the archived package; no unrelated investment
is automatically created. Resolved stale connections retain the source fingerprints
so a later calculation cannot silently treat different live history as reviewed.

## Verification

Focused tests cover dated/staged payments, established cross-family-record links,
family membership without a connection, stale historical evidence, partial/multiple
sources, ownership labels, currency gaps, hidden-client access, unchanged compliance
reviews and portable source/live identities. Browser checks cover saving a manual
connection, the handoff copy control and desktop/mobile return displays.

Local verification on 7 October 2026: 85 focused return, funding, reconciliation
and review-transfer tests passed. The additive migration was applied locally and
the EF model check found no pending changes. Desktop and mobile browser checks
passed for saving a connection, copying the handoff and displaying both return
periods without page overflow. Synthetic browser records were removed. The normal
local application was restarted successfully; live deployment remains separate.
