# KCAS Client Advice Workflow

## Summary

KCAS will provide a dedicated **Advice** area for FAIS investment advice. This is separate from the AML/CFT client-risk assessment.

KCAS will be the authoritative structured record, generate controlled Risk Analyser and Client Advice Record (CAR) PDFs, retain signed copies, and support a Codex-assisted quality-review workflow. This supports the FAIS requirements to assess the client's needs, financial position, risk profile and experience, and to document the advice basis, products considered and recommendation rationale.

## Core workflow

1. **Start an advice case**
   - Open `Clients -> Client -> Advice`.
   - Select new investment, top-up, switch, retirement decision, replacement, annual review or other advice.
   - Select the advice subject or subjects for individual, joint, family, trust or entity advice.
   - Link the relevant investments and proposed transaction.

2. **Capture the advice facts**
   - Snapshot the client profile, income, expenses, retirement age, dependants, current investments, valuations and existing allocation.
   - Record objectives, amount, time horizon, liquidity requirements, withdrawals, tax considerations and advice scope.
   - Record the source, date and supporting document for each material fact.
   - Explicitly record limited advice or information declined by the client.

3. **Complete the Investment Risk Analyser**
   - Keep it distinct from the AML client-risk assessment.
   - Use a recorded, selectable and versioned investment-risk methodology.
   - Support the evidenced KCAS methodology with bands of 18-30, 31-48, 49-66, 67-84 and 85+.
   - Also support the previous-form questionnaire with its original points and corrected half-open bands of 11-<20, 20-<40, 40-<55, 55-<75 and 75-85.
   - Present risk capacity, tolerance, objectives, knowledge and liquidity separately.
   - Calculate the score deterministically and require a reason for any adviser override.
   - Check allocation, concentration, domestic/offshore percentages, retirement proximity, withdrawals and product knowledge against the result.

4. **Prepare the Client Advice Record**
   - Populate the CAR from the approved facts and Risk Analyser.
   - Require the advice scope, client needs, material relied upon, products considered, exact recommendation, rationale, costs, tax, liquidity, restrictions, material risks and replacement consequences.
   - Record client departures from the recommendation and the warnings given.
   - References such as "see website", "see mandate" or "has been explained" cannot be the sole rationale.

5. **Perform the Codex-assisted quality review**
   - Export a local review manifest containing structured facts, calculations, document paths and hashes, but not copies of client files.
   - Record findings with severity, affected field, evidence, discrepancy and recommended correction.
   - Resolve or expressly accept every finding. Codex cannot approve or issue advice.
   - Allow the preparer to download a watermarked draft preview before approval. The preview contains the proposed client-facing Risk Analyser and CAR, followed by a separate internal issue report explaining each blocker, why it matters, its evidence and required action, and a concise blocker appendix. The internal report is excluded from the approved client PDF.

6. **Approve and issue**
   - Use `Draft -> Ready for review -> Returned/Approved for issue -> Issued -> Signed/Complete`.
   - The preparer cannot approve their own case.
   - Approval freezes the facts, calculations, recommendation and generated-PDF hash.
   - Later changes create a revision and supersede the previous version.
   - Staff issue the generated PDF through the existing process and upload the signed PDF. KCAS records its path, hash, issue date, signer and uploader.

## Evidenced Kanaan advice methodology

The structured workflow is grounded in Kanaan's retained advice records, particularly the 2007 Client Advice Record, the signed 2009 Risk Analyser and the signed 2022 Client Advice Record. Those records evidence the following recurring method:

1. Record the contact, advice scope, client needs and any information limitations.
2. Establish the client's financial position, objectives, experience and product knowledge.
3. Assess investment horizon, retirement proximity, income and withdrawal needs, dependants, age, expected income, emergency liquidity, objective, attitude to risk and volatility tolerance.
4. Calculate the score deterministically and map it to the approved bands: 18-30 Very low, 31-48 Low, 49-66 Medium, 67-84 Medium to high and 85+ High.
5. Consider existing asset classes, the amount being advised on, its proportion of the total portfolio and whether the money is discretionary or prudential.
6. Compare the resulting profile with suitable investment categories and portfolio exposure, while considering family, financial and other circumstances rather than treating the score as the sole decision.
7. Record products and funds considered, factsheets or quotations, exact recommendations and the motivation for selection or replacement.
8. Explain costs, tax, liquidity, restrictions, capital risk, guarantees and replacement consequences.
9. Record special instructions, client departures and limitations arising from information not supplied.
10. Obtain adviser and client acknowledgement, retain the signed record, and revisit the analysis after material changes, significant top-ups or withdrawals.

## KCAS changes

- Add an advice register, client advice history, guided advice case and frozen print/PDF routes.
- Add `Advice.View`, `Advice.Prepare`, `Advice.Review`, `Advice.Issue` and `Advice.Audit` permissions.
- Add structured advice-case, participant, risk-response, product, recommendation, finding, approval and document records.
- Reuse existing client profiles, investments, valuations, evidence paths, hashing, audit events, PDF generation and drive mapping.
- Recognise `.docm` and `.xlsm` during evidence scanning.
- Transfer structured advice records, approvals, findings and document hashes safely between KCAS environments.
- Index existing Risk Analysers and CARs as historical records without treating extracted values as verified current data.

## Additional workflows

After the Risk Analyser and CAR are stable, add these modules on the same advice-case foundation:

1. **Annual portfolio review** - freeze valuations and allocation, assess performance and suitability drift, record actions, and generate an approved review report.
2. **Financial needs analysis / Finplan** - structure income, expenses, assets, liabilities, dependants, insurance, capital requirements, retirement needs and savings shortfalls.
3. **T2/T6 retirement scenarios** - replace uncontrolled spreadsheets with a validated, versioned calculator whose tax, growth, inflation, drawdown and fee assumptions are dated and sourced.
4. **Source of funds and wealth** - link client-level and investment-level declarations to contributions, accounts and supporting evidence.
5. **Fees, EAC and replacement advice** - compare existing and proposed products, charges, penalties, tax, access, guarantees and benefits lost.
6. **Mandate and disclosure pack** - track applicable mandates, disclosure letters, factsheets and declarations by version, issue date and acceptance.

## Quality and tests

- Test every score boundary and percentage-total validation.
- Detect contradictions between risk profile, objectives, liquidity and recommendation.
- Block approval when required facts, products, rationale, costs or replacement details are absent.
- Enforce different preparer and reviewer identities.
- Ensure edits invalidate approval and create a new revision.
- Cover individual, joint, family, trust and entity cases.
- Verify generated PDFs, hashes, signed-copy linkage and historical indexing.
- Test Codex finding imports without allowing automatic approval.
- Test encrypted local/live transfers and Windows path mapping on Windows and Ubuntu CI.

## Laptop-to-live advice transfer

Use **Client advice > Advice transfers**, or **Transfer advice record** on an advice case, to create an encrypted `.kcas-advice` package for a client. The package preserves all advice cases and revisions, participants, Risk Analyser responses, products, fact sources, linked investments, findings, approvals, document metadata, statuses, frozen snapshots, and case audit events.

Live preview must uniquely match every client and investment before apply is enabled. Paths inside the normal client folder are remapped to the live client folder. Referenced working documents outside that folder are encrypted into the package and extracted under `KCAS Advice Imports/<package id>` in the live client folder. Applying does not approve or advance a case; its source status and review blockers are retained.

## Delivery assumptions

- Phase one delivers the Risk Analyser, CAR, quality review, PDF generation and signed-copy tracking.
- Existing documents are indexed historically, not converted automatically into verified structured records.
- Client signatures remain outside KCAS initially and are uploaded as signed PDFs.
- Every client-facing Risk Analyser and CAR requires independent second-person approval.
- Finplan and portfolio-analysis modules follow after the core workflow is operational.

## Implementation status at 2026-09-18

Phase one is implemented for structured Risk Analyser and CAR cases, family participants, dated material-fact sources, links to existing investments, deterministic scoring, product comparisons, Codex review manifests and finding imports, independent approval, frozen snapshots and generated-PDF hashes, controlled revisions, signed-PDF tracking, audit events, historical Risk Analyser/CAR indexing, and encrypted laptop-to-live advice transfer.

KI-controlled investment-risk methodology configuration and the additional portfolio, Finplan, T2/T6, source-of-funds, replacement-comparison and mandate modules remain follow-on work. Advice records use their dedicated `.kcas-advice` transfer package rather than client-review transfer packages.
