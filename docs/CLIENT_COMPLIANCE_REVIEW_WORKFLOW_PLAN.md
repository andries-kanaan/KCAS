# Client Compliance Review Workflow Plan

Status: In delivery  
Plan owner: Kanaan / KCAS  
Last updated: 2026-08-01

## Outcome

Make an ordinary low-risk client review on the website materially equivalent to the client-by-client Codex review: KCAS gathers and reconciles what it already knows, presents one concise proposed result, and asks the user only about genuine unresolved or material issues.

The normal case must begin and be orchestrated on one client review page with one final approval action. Evidence preview and exceptional decisions should open in drawers, modals or focused detail pages without losing the review pathway.

## Operating principles

1. Start from facts already in KCAS: current investments and ownership, KYC notes and discussions, client age and retirement indicators, payout history, linked Kanaan IDs, and the assigned evidence folder.
2. Treat the selected folder as an instruction to save immediately, identify clients sharing that folder or Kanaan ID, and scan once for the whole household or linked group.
3. Do not make historical archive ambiguity a blocker. Rank and show only documents that could satisfy a current requirement; retain the rest as searchable historical evidence.
4. A document may satisfy more than one requirement and more than one confirmed client. The UI must support explicit multi-classification and multi-owner links rather than creating confusing duplicate files.
5. Apply deterministic conclusions automatically when reliable records agree, except for investment reconciliation changes. Investment status, surrender dates, account links and valuation associations are always proposed for explicit verification before they are changed.
6. Keep every automated proposal, user confirmation, source, override and final decision in the compliance audit trail.
7. Do not depend on an AI service. Implement the core flow with KCAS data queries, document metadata/text extraction, rules and confidence thresholds. AI-assisted interpretation can be an optional later enhancement, never the only route to completion.

## One-page client review

Primary route: `/clients/{id}/compliance-review`.

The page has a persistent progress summary and seven ordered sections. Folder selection is always first; risk assessment and final completion form the last combined section. Completed sections remain visible as concise summaries rather than separate blocking pages.

### 1. Client folder and scan

The review starts here. If no folder is assigned, the user selects it with the server folder browser. Selection saves immediately and starts the first scan. If the existing folder already has a completed scan, KCAS reuses it; rescanning occurs only after a folder change or an explicit refresh.

KCAS identifies every client sharing the folder or Kanaan ID before interpreting ownership or evidence, so a household folder is scanned once and linked-client context is visible from the start.

### 2. Client and relationship context

KCAS automatically loads:

- lifecycle status and current-investment status;
- all records sharing the Kanaan ID or evidence folder;
- joint policyholders, beneficial owners, spouses and related entities;
- active and surrendered investment accounts, latest valuations and relevant transactions;
- ages, retirement references, delivery-channel notes and current profile values.

The system proposes Current, Historical, Closed, Deceased or Duplicate. Current investment ownership should normally make Current the default. A shared-folder or shared-policy association is shown once for confirmation if it would update another client.

### 3. Facts and conflicts

KCAS proposes only changes supported by identifiable sources, such as address, retirement status, tax residence, tax number, marital status and relationship identity details. It automatically accepts exact corroboration across authoritative records; material cross-client changes or unresolved contradictions require confirmation.

Questions must explain the conflict, show the competing values and identify each source. Optional or absent legacy data must not block the assessment when the risk conclusion can be supported without it.

### 4. Investment reconciliation / verify

Both the client-by-client Codex route and the website route must stop at the same explicit investment-verification checkpoint before evidence readiness and risk scoring are completed. Automation prepares the reconciliation; it does not silently alter investments.

KCAS compares every active and historical investment against:

- current valuations and their valuation dates;
- investment and withdrawal transactions, including the latest balance;
- surrender, repurchase, transfer, switch and proof-of-payment documents in the assigned client folder;
- predecessor and successor account numbers, administrators and effective dates;
- current and historical investments held jointly or under a linked household client.

The verification view shows every account, not only exceptions. Each row contains the proposed status, current value or historical closing evidence, start and surrender dates, predecessor/successor links, relevant folder evidence, discrepancies and the proposed correction. The available verified outcomes are **Current**, **Historical - surrendered**, **Transferred**, **Duplicate/continuation**, and **Needs follow-up**.

The system flags at least these contradictions:

- a historical or zero-value account with no effective surrender/transfer date;
- an account marked surrendered while a current valuation still exists;
- a purportedly current account with no current valuation or recent supporting statement;
- duplicate account numbers or administrator-continuation records that may represent one investment;
- a valuation with no matching account or more than one plausible account;
- an investment/withdrawal amount that does not reasonably reconcile to its predecessor, successor or supporting document;
- a joint investment that is absent from the linked client's compliance context.

The user may approve individual rows or all unambiguous rows, mark a row verified with no change, or send it to follow-up. Any approved correction records the old value, new value, source document, user and reason in the compliance audit trail. Material unresolved rows block final assessment completion; informational rounding or timing differences may be accepted with a recorded explanation.

### 5. Evidence readiness

Folder selection saves immediately and starts the household-aware scan. KCAS then:

- ranks current evidence candidates by requirement, recency, signature and source quality;
- proposes ownership and requirement classifications;
- allows one file to support several requirements and linked clients;
- provides a direct Reclassify action when scanner classification is wrong;
- verifies high-confidence evidence in a review batch;
- leaves the full archive searchable but out of the normal decision path.

The page shows only incomplete requirements, contradictions and low-confidence candidates. A user can expand the completed evidence pack if desired.

### 6. Screening

PEP/PIP, sanctions/TFS and adverse-information checks run as a single section for the client and relevant joint or controlling parties. The system records list/source version, access date, searched names/identifiers and outcome. Clear exact-name/identifier checks are proposed as No match or None found. Possible matches always require human resolution; sanctions concerns block finalisation.

### 7. Risk assessment and completion

The six-factor assessment is prefilled from the preceding sections:

- client/ownership from category and relationship transparency;
- geography from residence and investment exposure;
- product from current products and services;
- delivery from notes, signed declarations and interaction records;
- activity from the verified investment reconciliation, transactions, payouts and stated purpose;
- source from verified source-of-funds and source-of-wealth evidence.

Each proposed answer includes its short rationale and linked evidence. The user edits only a disputed factor. KCAS calculates the score, rating, EDD requirement and next review date automatically.

One final summary shows lifecycle classification, profile corrections, linked clients, investment-reconciliation status, evidence completion, screening outcomes, factor scores, rating and next-review date. The normal action is **Approve and complete review**. That action atomically records the confirmations, finalises a Standard/Low assessment and produces the audit snapshot.

High risk, a risk override, PEP/adverse escalation, sanctions concern, unresolved identity conflict, evidence exception or KI approval remains a separate explicit decision.

## Manual-intervention boundary

Only these conditions should interrupt the normal one-page flow:

- ambiguous client identity or ownership that KCAS cannot resolve reliably;
- conflicting authoritative current records;
- a proposed change to another client not already confirmed by a shared signed source;
- possible or confirmed PEP, sanctions/TFS or material adverse-information match;
- unexplained transaction or source-of-funds/wealth concern;
- an unresolved material investment-reconciliation contradiction;
- a mandatory high-risk trigger, EDD, exception or rating override;
- final approval by the authorised person.

Missing optional legacy fields, numerous historical files, ordinary offshore exposure and facts directly inferable from current investments or signed records must not create artificial blockers.

## Compliance navigation reduction

Reduce the current ten submenu entries to four. The Compliance heading itself opens the overview/work queue, so there is no separate Overview submenu item.

1. **Client reviews** — client list, lifecycle, evidence, screenings, risk assessments and client review transfers. Transfers become an action on the list/client review page, not a primary menu destination.
2. **Daily compliance** — goAML checks, goAML package transfer and recurring operational checks. Transfer is an action on the goAML page.
3. **Programme and controls** — business risk assessment, RMCP and monitoring/work register, organised as tabs or cards on one landing page.
4. **Inspection readiness** — inspections, reports, exports and evidence packs.

Existing URLs remain available and redirect or deep-link to the relevant tab, preserving bookmarks and permissions. Configuration belongs under Administration or contextual page settings rather than the main Compliance menu.

## Delivery phases

### Implemented foundation — 2026-08-01

- Added `/clients/{id}/compliance-review` as the primary client-review route. It starts with folder selection/scanning, presents all seven ordered sections, reuses existing completion rules and identifies one next action.
- Replaced the selected-client verification/evidence/risk button cluster with one prominent **Start / continue compliance review** action. The client review list now opens this unified route, while the older operational verification screen is explicitly an exception tool.
- Added a deterministic lifecycle proposal from reconciled current or fully historical investments; confirmation records the existing controlled lifecycle audit event without requiring the user to type a reason already established by KCAS.
- Added `/clients/{id}/investments/reconciliation`, showing every current and historical account, valuations, transactions, discrepancies, evidence candidates and linked household/folder clients.
- Added explicit outcomes, effective dates, predecessor/successor links, evidence references, reasons, persisted-state snapshots and audit events. No proposed investment correction is applied until the reviewer verifies it.
- Added stale-review detection so a material account, transaction or valuation change automatically reopens that row.
- Made completed investment reconciliation a prerequisite for finalising a client risk assessment, while clients with no investment data pass without a synthetic task.
- Included current reconciliation decisions and their approved surrender dates/links in client review export/import packages, rematched by stable legacy ID or account number/administrator on the receiving server.
- Recorded the approved pilot reconciliations in the local structured review register. Deeper inline editing and the final atomic approve/complete command remain planned below.
- Reduced the Compliance submenu to Client reviews, Daily compliance, Programme and controls, and Inspection readiness. Client/goAML transfers are contextual actions on their working pages, Programme and controls consolidates BRA/RMCP/monitoring links, and all prior deep links remain available.

### Phase A — orchestration and page shell

- Extend the implemented unified route from orchestration into deeper inline editing.
- Retain the existing lifecycle, evidence and risk services behind the orchestration service.
- Complete redirects/deep links and reduce the menu to four destinations.

### Phase B — household-aware evidence automation

- Scan shared folders once and resolve shared Kanaan IDs/policies.
- Add first-class multi-requirement classification and evidence reclassification.
- Rank requirement candidates and suppress non-blocking archive ambiguity.
- Integrate the implemented reconciliation/verification page into the unified client-review shell.

### Phase C — deterministic proposals

- Add rules for lifecycle, current products, retirement, address, delivery, activity and source conclusions.
- Add conflict detection, confidence levels and source-linked explanations.
- Prefill screenings and all six risk factors.

### Phase D — one-action completion

- Add the final review summary and atomic approve/complete command.
- Preserve KI/EDD/escalation gates.
- Add export/import support for the complete client-review package from the same page.

## Acceptance criteria

- An ordinary natural-person client with adequate records can move from Unreviewed to a finalised assessment on one page with no manual data re-entry.
- After selecting a folder, the user is not asked to initiate a separate save or scan.
- Current investment, joint ownership, notes and signed evidence are considered before any question is shown.
- Every active and historical investment appears on a reconciliation/verify page; no investment correction is applied without explicit approval.
- Historical zero-value accounts have an evidence-backed surrender/transfer date or remain visibly unresolved.
- Duplicate/continuation accounts and joint investments are represented without double-counting value.
- A shared household folder is scanned once, and cross-client changes are presented as one clear confirmation.
- Historical archive ambiguity does not block readiness.
- Every factor is prefilled with rationale and evidence; only genuine exceptions require editing.
- The normal case ends with one approval action and a complete audit trail.
- The Compliance submenu contains no more than four entries.
