# KCAS Client Assessment Protocol

This protocol explains how Codex may assist with KCAS client-by-client compliance assessments. Codex must not fabricate or rubber-stamp assessments. It may complete an assessment only where the client folder, KCAS records, investment history, and screening evidence support the decisions.

## Objective

For a requested client or Kanaan ID/family, Codex should complete an evidence-based KCAS review by:

- finding the correct client folder;
- selecting current KYC/FICA evidence;
- checking required screening categories;
- reconciling all investment accounts;
- assigning lifecycle status;
- answering risk-assessment factors from evidence;
- finalising the assessment only if all required checks are supportable.

## Evidence Requirements

For each client, confirm or record evidence for:

- Identity
- Address
- Tax/residency profile
- Source of funds
- Source of wealth
- Beneficial ownership/control
- PEP/PIP screening
- Sanctions/TFS screening
- Adverse information review
- Product/service exposure
- Delivery channel
- Geographic exposure

Use actual files from the client folder where possible. Screening evidence may be recorded as a review note if no file exists, but it must state what was checked and the result.

## Investment Reconciliation

Before finalising, every investment account must be reconciled.

For each account:

- If it has current valuation data, mark it current.
- If it has no current value, confirm surrender/transfer from surrender date, transaction trail, statement, or other evidence.
- If it was transferred, record the transfer/continuation reasoning.
- If it is a wrong-client duplicate, reconcile it explicitly as such.
- If evidence is insufficient, do not finalise; record follow-up.

The assessment must not be finalised while investment reconciliation remains incomplete.

## Lifecycle Rules

Use lifecycle status based on evidence:

- `Current`: at least one current investment/account relationship remains.
- `Historical`: no current investment remains, but the client record is valid historical relationship data.
- `Closed`: use where the client relationship is clearly closed and should be treated as closed rather than merely historical.
- `Duplicate`: only where reconciled as duplicate/wrong-client duplicate.
- `Unreviewed`: leave unchanged if the assessment cannot be completed.

Do not mark a client current merely because they exist in KCAS. Use investment and relationship evidence.

## Risk Assessment Guidance

Use the KCAS risk methodology, not personal judgement.

Typical low-risk natural person profile:

- `CLIENT_OWNERSHIP`: Natural person or transparent simple structure
- `GEOGRAPHY`: Domestic and low-risk exposure
- `PRODUCT`: Simple or ordinary Kanaan investment service
- `DELIVERY`: Established/advised relationship with effective verification
- `ACTIVITY`: Expected and transparent activity
- `SOURCE`: Verified and consistent, or reasonably corroborated where evidence supports that

Use Standard or higher where there is ordinary cross-border exposure, trust/legal-person complexity, unclear source, unusual activity, or other documented reason.

Do not override ratings without a documented reason.

## FIC / Screening

A FIC-style review must be done for:

- PEP/PIP exposure
- sanctions/TFS concern
- adverse information

If web/open-source checks are used, search targeted names and relevant identifiers. Record whether any relevant hit was found. Do not cite irrelevant search results as evidence.

If there is a credible concern, do not finalise as low risk without escalation.

## Folder Mapping

KCAS may contain old live paths like:

`z:\Kanaan Trust\Clients\Clients\...`

Local review environments may use:

`C:\Download\_kanaan\ClientsKanaan\...`

When a client folder path is missing or unavailable, search the configured evidence scan root for the best matching folder. If a correct local folder is found, update KCAS to that mapped folder. Do not use a folder belonging to a different Kanaan ID/client merely because it is family-related.

## Family Reviews

When reviewing a Kanaan ID/family:

- review every client record in the family;
- determine each member's lifecycle separately;
- reconcile each member's investments separately;
- finalise only the members with sufficient evidence;
- include historical/closed completed members in family transfer where needed.

A family may contain current, historical, and closed members.

## Transfer Readiness

Before exporting/importing completed reviews:

- every transferred client must have a finalised/approved assessment;
- all blocking evidence requirements must be satisfied or excepted;
- every investment account must have a current matching reconciliation review;
- entity clients such as trusts/legal persons must have ownership/control profiles and related parties completed;
- preview should show no blocking conflicts.

## When Not To Finalise

Do not finalise if:

- identity/address/source evidence is missing;
- screening has not been performed;
- investments remain unresolved;
- trust/legal-person ownership/control is incomplete;
- live/local account matching is ambiguous;
- the client folder cannot be confidently matched;
- the conclusion would depend on assumption rather than evidence.

In these cases, record follow-up instead of completing the assessment.

## Standard Codex Instruction

When asked to complete a KCAS assessment, Codex should:

1. Inspect the KCAS client/family records.
2. Locate and verify the client folder.
3. Review actual files, not only KCAS metadata.
4. Reconcile investment accounts first.
5. Select evidence items for all required categories.
6. Perform and document screening.
7. Set lifecycle status.
8. Complete risk responses with evidence-linked reasons.
9. Finalise only if the above is complete.
10. Summarise what was done and any caveats.
