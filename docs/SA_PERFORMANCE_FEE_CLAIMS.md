# SA Client Performance-Driven Fee Claims

## Scope and evidence

This module calculates individual SA wrap-mandate fees, not offshore fund-level fees and not the aggregate prudential estimate used in the AUM projection. From the client's **Investments** tab, select **PDF claims** beneath the relevant SA account number and **Investment returns** link. An existing profile opens directly: choose **Upload statements / client review**. For an unregistered account, choose **Upload client statements** to register the starting account and open that same client-level workflow. **Prepare manually** and **Request Codex preparation** remain alternatives for initial setup and genuine evidence gaps. Opening the account link does not itself register, request, approve or calculate a fee. Historical accounts remain available for tracing prior capital and fees. The central register and anniversary reviews remain under **Investments > Annual PDF claims**. Registration does not assert that a fee is due.

Prepare from the actual signed mandate and amendments, administrator statements, prior fee deductions, client cash-flow history and dated valuations. The administrator agreement and fund factsheets are research aids, not substitutes for the client's terms. Do not assume 15%, CPI + 3%, VAT or a reset convention for every client. Stable or zero-fee mandates may produce an approved zero-fee review; unsupported terms cannot produce a submission.

**Confirmed scope rule:** the applicable signed client fee mandate determines the chargeable investments, period and terms. Earlier account history traces capital and prior fee protection, not authority to levy fees before the mandate applied. Never retrospectively apply today's terms to an older investment or charge again for a crystallised period. A draft beginning before its mandate-effective date cannot be approved or submitted. Changed terms require evidence and separated periods, not a guessed baseline.

Design source checks included the signed segregated-mandate fee schedule (active SA wraps in clause 19.1.4, passive Stable SA in 19.1.5), the AIMS administrator agreement's fee annexure and the current SA wrap factsheets. The signed schedule distinguishes annual SA client fees from monthly offshore fund-level fees; those are not interchangeable calculations. No private client files or personal details are embedded in the application, migrations or regression fixtures.

## Administrator Excel Statements

### Client-Level Review

The default action is **Prepare all accounts**. KCAS determines the matched supported SA-wrap accounts from the statement and prepares their drafts without requiring manual exclusions. Historical and zero-balance accounts remain included for review; neither zero value nor an old account proves a 0% mandate or waives an unclaimed fee. Already-reviewed periods and pending approved claims remain visible but are not prepared twice. **Select accounts manually** is an optional override, not the normal workflow.

The result distinguishes **Fee payable**, **Verified 0% mandate - no fee**, **No chargeable outperformance - no fee**, **Calculated draft - review required**, and **Calculation incomplete**. KP1 Stable SA is included instead of silently omitted. Its active-wrap sibling's rate is never copied to it: the exact signed zero-rate mandate and covered period must be recorded with `ZeroRateConfirmed`. A verified zero-rate review requires the mandate/scope and dated closing statement, not invented CPI/VAT assumptions or a cash-flow calculation that cannot change a 0% charge. It verifies only the zero-rate fee; original ledger gaps remain available for transfers and other reviews. It does not create, reset or advance a financial HWM checkpoint. A zero fee caused by underperformance preserves the accumulated benchmark/HWM.

**Download combined draft review** includes the separate wrap outcomes and outstanding issues. After KI approval, **Download approved client report** includes both positive and zero-fee reviews. The **approved submission** remains restricted to positive claims; no 0% review becomes a payment request. Cash, investor-choice and other unsupported holdings are shown with scope explanations, not incorrectly labelled 0% mandates. Different account/wrap results are never netted to hide a loss or duplicated transfer.

The normal starting action is **Upload statements / client review** on an existing account claim, or **Upload client statements** when opening an unregistered account from the client's Investments tab. Codex is not a prerequisite for uploading or calculating. The client review is `/investments/performance-fees/{profileId}/client-review`.

Select one or more original administrator XLSX exports (up to six, 20 MB each / 60 MB total), including files spanning separate or overlapping periods. KCAS previews the combined account matches before saving. **Import statement files** preserves each original once for that client; it does not rewrite or manufacture a merged Excel workbook. Existing originals uploaded through the earlier single-account page are available automatically. Matching transaction keys are counted once across files; conflicting values and unit continuity gaps block unsupported calculations. Exact policy-number matches are limited to this client, not everyone sharing a Kanaan ID. Ambiguous matches and unrecognised policies are shown but cannot be selected. No fee-bearing SA wrap means research-only, not a newly chargeable account.

Choose **Prepare all accounts**, or use optional manual selection. KCAS reuses evidenced covered mandate terms, prior-fee research, published CPI and protected checkpoints, calculating separate wrap drafts. The batch is saved in one transaction. No other client's terms or original inception are copied as defaults. **Download combined draft review** contains each account/wrap result, calculation inputs still missing and review requirements. A numerical draft is explicitly labelled, not presented as an authorised claim.

Cross-account unit transfers require unique date, exact holding, opposite units and value matches. Recorded actual mandate coverage can establish continuity; otherwise explicit evidenced confirmation is required. KCAS carries source protection only with supported numerical inputs, compatible terms, valuation and ordering; approval requirements propagate. Ambiguous pairings and incomplete protection remain issues. A matched amount is not a new subscription or fee crystallisation. Connected calculations retain reciprocal input hashes; changed source inputs prevent approval until rebuilt. Unrelated accounts are never pooled merely because they share a workbook.

**Resolve account gaps with Codex** is optional and appears against recorded outstanding items. Its brief identifies those items and existing mandate/source references, rather than serialising the entire preparation and requesting another compliance assessment. Reuse actual evidence already obtained during compliance and earlier PDF preparation; no scans, repeated screening, or manual reproduction of Excel arithmetic is required. Fee-specific evidence absent from the compliance review may still be needed. CPI periods must cover the actual chosen cutoff under the verified convention; an imported transaction statement is not a source of assumed inflation rates.

A current authorised KI can approve selected ready calculations together with a recorded reason. All selected approvals pass or none are saved. The existing administrator submission supports their separate claims and wrap schedules; deduction and payment remain separately evidenced actions. A new upload cannot silently change an approved/pending claim. Client-level statements, calculations and transfer decisions use the existing profile/source/JSON/audit storage; no additional schema migration or client data seed is introduced by this orchestration.

From an account's PDF claim page, select **Administrator statements**. Upload the original AIMS/Glacier `.xlsx` export with detailed transactions, inspect the preview, and choose **Import statement history**. Upload historical periods successively. KCAS preserves the exact original bytes and SHA256, recording account and UTC time. Downloading the preserved original does not regenerate/reformat the workbook. No macros, formulas, external worksheet links or derived calculation tabs are executed. The parser supports shared/inline text, moved columns, Excel serial dates (including the 1904 epoch), English/Afrikaans headings, decimal commas and parenthesised negatives. A three-column benefit/cash summary is not a unit-transaction table. Unsupported or incomplete tables fail visibly rather than being guessed.

The selected KCAS account must match an exact policy number in the statement. Other policies are shown in preview but are not silently added to this profile. Original files can contain those related policies for research; cross-account fee continuity is not inferred from a shared client name or matching amount. Historical imports do not modify KCAS account/transaction/valuation/compliance data. A repeated file is idempotent; matching source rows in overlapping files are counted once. Conflicting values, repeated identical rows and unit-rollforward gaps remain visible. Unit balance checks are not proof that all mandate, price, tax or cashflow evidence is complete.

Select **Prepare draft from statements** for separate SA wrap reviews (`KE1`, `KA6`, `KA9`, `KP1`); the client-level page prepares matched accounts together. Stable's 0% mandate is verified separately. Cash and investor-choice holdings remain visible outside the supported fee scope. Opening snapshots are not contributions or arbitrary new HWMs. A historical opening after inception needs an evidenced benchmark/HWM checkpoint. A common actual cutoff is required; nonzero stale-price holdings are flagged. Reinvested income and ordinary expenses remain investment performance. Uniquely matched cash allocations protect the actual amount received by the wrap as ScopeFunding, not new client money or wrap-earned performance. Contributions/withdrawals use actual dates; pre-withdrawal values are reconstructed only when all holdings have consistent same-day prices and ordering is unambiguous, otherwise they remain missing.

Explicit performance-fee/PDF/prestasie descriptions are flagged as `PerformanceFee`, not ordinary expenses. A previous deduction requires review of its actual scope, crystallisation date and after-fee protected checkpoint before calculating later fees. It does not automatically become a paid KCAS claim or reset a HWM. A generic wrap/adviser fee, or the absence of an explicit PDF label in a partial statement, is not proof that a prior PDF was or was not charged.

Unrecognised entries, cross-account transfers, prior PDF deductions and unmatched cross-wrap switches remain explicit evidence issues. **Review classification** records an authorised person's actual reason/account/time without overwriting the administrator row. Reclassification/import invalidates an existing draft until rebuilt. Source imports and classification changes cannot change an approved/pending claim. Same-day exactly paired active-wrap switches are proposed, not confirmed: in **Prepare / update calculation**, verify the source valuation and mandate continuity before marking the transfer supported. The calculator moves the proportionate source benchmark/HWM rather than adding fresh protected capital. Different fee terms, ambiguous same-day ordering or unsupported continuity block automatic transfer treatment. An external incoming `TransferIn` requires evidenced carried benchmark and HWM; it is not automatically an ordinary contribution.

The preparation page's wrap selector edits each wrap's actual mandate, values, CPI segments, VAT and prior-fee history independently. No 15%/CPI/VAT defaults or zero-CPI fallbacks are inferred from the spreadsheet. The saved calculation and PDF show per-wrap thresholds, excess, fees and VAT. Administrator CSV lines identify the wrap under the same unique claim reference. Each wrap's protected checkpoint is carried forward separately after the actual fee deduction or approved zero-fee review; losing wraps are not reset merely because another wrap generated a fee. The internal draft PDF includes source evidence and genuine issues; the approved administrator document excludes private source paths and internal commentary.

Deployment adds `PerformanceFeeStatements` and initialises existing profile review metadata through migration `AddPerformanceFeeStatements`. The full schema and incremental SQL accompany the EF migration. Private administrator workbooks and client-specific fixture data are never included in source control.

## Anniversary and Catch-Up Rules

- First due date is one year after the original first investment. Top-ups do not reset it.
- Never previously charged: research and explicitly confirm that history, then calculate from the first investment covered by the applicable fee mandate to the actual chosen closing valuation date. This is not necessarily the client's oldest investment or the latest receiving account's opening date.
- Previously charged: use the last verified fee checkpoint, not inception again. An external historical checkpoint requires actual evidence of its value, benchmark and HWM.
- Subsequent calculations use the preserved module checkpoint. Zero-fee years retain unrecovered underperformance and accumulated hurdle.
- After an overdue catch-up ending between anniversaries, the next review is the original anniversary, allowing a short stub period. A 29 February anniversary uses 28 February in non-leap years without permanently drifting away from 29 February.
- Existing January invoicing / 28 February timing in older administrator material is not the selected operational schedule. The user's instruction is the individual first-investment anniversary; mandate/administrator compatibility remains part of the KI review.

## AIMS / Glacier History Completeness

The relevant administrator-history boundary is the capital's first entry into AIMS/Glacier, not an investment with a preceding administrator. AIMS/Glacier branding changes and internal account/wrap transfers do not create a fresh fee inception. KCAS's imported original investment date remains unchanged; the fee profile separately records the evidenced administrator fee inception.

The upload preview and saved client review prominently group accounts whose earliest supplied summary has an existing opening balance. Before any preparation, the notice names each account, its opening balance, the supplied start date and the earlier-history cutoff (the preceding day), and requests AIMS/Glacier history from first investment. It explains that transfer-funded receiving accounts may depend on this history too, without asserting an unproven transfer match. The same notice appears on the single-account statement page. Combined earlier files that establish the beginning remove the notice immediately in preview; a verified checkpoint covering all fee-bearing wraps also avoids an unnecessary earlier-history request. Complete accounts remain available for preparation. This is a presentation change, not permission to reset missing benchmark/HWM protection or bypass existing calculation/approval gates.

The reader captures each exact policy's account-level opening/closing summary, dated period, signed movement lines and original row references in English or Afrikaans, alongside the detailed holdings. Beginning coverage requires the earliest account summary to open at zero, supported by zero detailed opening capital/units and an actual first funding transaction. The first funding date, not the statement-period start, establishes the funded period. A zero row for one underlying does not establish the whole account's beginning. A nonzero earliest account opening requires earlier AIMS/Glacier history (or an already verified module fee checkpoint); it never authorises an opening market-value HWM reset.

Summaries reconcile to detailed opening/closing holdings and to opening plus signed summary movements, including market movement. Printed monetary rounding is allowed only up to half a cent per source line, with a minimum two-cent boundary. This is not a tolerance for missing capital. Source periods, not filenames, identify gaps and overlap. Multiple files are merged chronologically, original rows retained and exact duplicate transactions counted once. A covered beginning and a gap-free transaction chain are distinct findings. Generic first-day transfers retain an explicit source/protection requirement; a zero opening does not automatically classify an internal transfer as fresh external funding.

Exact uniquely matched same-day same-wrap cash transfers can link different underlying allocations; unique unit transfers retain the existing holding/unit checks. Both require actual settled totals, source valuation, compatible fee terms and evidenced mandate continuity before carrying protection. An opening transfer into a newly created receiving account can carry the source benchmark/HWM at the receiving account's opening date, without recording a second contribution or restarting its inherited administrator anniversary. Ambiguous splits, missing source histories, changed terms and uncertain ordering remain explicit blockers.

Preserved originals are automatically reparsed when the parser version changes; no re-upload is required. Read-only views do not mutate the database. Authorised batch preparation saves updated parsed metadata and new draft snapshots without changing original file bytes, import attribution, approved claims, investment histories, valuations or compliance records. Approval validates the current source fingerprint, so an old draft cannot be approved after a statement/interpretation change. The page distinguishes beginning coverage, earlier statement required, missing period and summary reconciliation. It suppresses stale draft figures and downloads until preparation is refreshed.

The administrator-facing evidence appendix includes source filenames/SHA256, dated account summaries, signed reconciliation movements, per-wrap mandate references, sourced CPI periods, previous-fee review basis and carried protection. Private disk paths and free-text internal handoff/continuity discussions are not included. The appendix does not imply that original signed mandates have been attached automatically; submission staff must include the supporting documents requested by the administrator. No KI approval or administrator acceptance is inferred from a successful calculation.

## Supported calculation convention

### Automatic Preparation And Review Requirements

Statement preparation performs numerical calculation separately from approval readiness. `HasCalculation` means the exact inputs support the displayed arithmetic under the explicitly recorded KCAS convention; it does not mean the mandate/convention has been accepted, the fee is authorised or VAT has been confirmed. `CalculationIssues` identifies missing numerical inputs. Other `Issues` remain mandatory review requirements. Unknown VAT must be displayed as unconfirmed, never silently treated as an agreed 0% rate. A draft with review requirements has no usable next checkpoint and cannot be approved or submitted. Invalidating source history suppresses all stale calculated amounts until rebuilt.

The shared **PDF benchmark data** page is reached from **Investments > Annual PDF claims**, and from a client PDF review. Import sourced CSV observations with columns `Month,PublishedOn,AnnualPercent,Reference`. Month is its first calendar day, dates use ISO format and AnnualPercent is headline annual CPI in percentage points, without the mandate spread. The reviewed public reference file is `docs/reference-data/performance-fee-cpi-2024-2026.csv`; its rates and publication references come from Stats SA P0141, not the old Excel engine. No client-specific evidence is in this file. Import this file in each environment before calculating against that date range; deployment does not overwrite operational benchmark records. Later observations can be imported without overwriting earlier sources. Original approved calculation snapshots remain unchanged.

The KCAS benchmark convention selects the latest actual CPI release published on or before each accrual date. Publication dates split ACT/365 periods, so there is no look-ahead to an unpublished observation, and the mandate spread is added exactly once. This is a disclosed calculation convention, not a claim that Stats SA or legislation mandates this fee method. Existing explicit manually evidenced segments remain in use unless **Use published SA headline CPI** is selected. Missing monthly releases, conflicting observations and stale coverage stop numerical calculation; there is no zero-CPI/projection fallback. KI approval checks that a draft's automatically generated CPI snapshot still matches the source register.

**Reuse evidenced mandate terms** records exact same-client account IDs and actual signed coverage evidence. KCAS reuses only matching wrap terms covered for the proposed period, never merely a shared family ID. Amounts, account inception, historical fee conclusions and HWM checkpoints are not copied. Conflicting account terms remain visible. Reused terms retain source provenance and cannot be approved against changed source terms or revoked coverage until rebuilt. The applicable convention and any conflicting administrator quote remain review matters; copying a rate is not agreement to retrospectively charge older capital.

Uniquely reconciled cash sweeps are recorded as **ScopeFunding**: an allocation from the cash holding outside the fee-bearing wrap. Its actual market value increases the receiving wrap's protected capital, without being labelled new client money or charging cash-scope income as wrap performance. Exact, uniquely paired same-wrap rebalances settling within seven days are neutral. Unmatched amounts, reversals, cross-wrap changes and missing source history remain explicit; no tolerance is used to conceal a material difference. Actual prior-fee research can be derived from a reconciled zero-unit inception ledger without unknown/performance-fee entries, retaining the precise statement basis. A historical opening balance is not an inception or a new HWM.

Exact paired transfers between this client's accounts can carry protection automatically where recorded mandate coverage includes both accounts and the underlying/date/units/value match uniquely. A numerical source draft may supply protection before its first fee anniversary, because carrying existing capital is not levying a fee. Its unresolved mandate/VAT/previous-fee review requirements propagate to the receiver, and reciprocal preparation hashes prevent stale approval. A 0% fee review does not establish a financial HWM for a positive-fee receiving scope.

The client page shows per-account/per-wrap draft results and separates calculation inputs from review-before-approval requirements. **Prepare all accounts** reports how many drafts were saved, ready, calculated subject to review, or numerically incomplete. Ready account reviews may be approved without forcing unrelated incomplete histories into a claim. Draft PDFs show calculated excluding-VAT figures even where approval is outstanding; approved reports/submissions remain subject to all original KI, history and mandate gates.

Repeated preparation checks a saved batch fingerprint before running statement preparation or numerical calculations. Unchanged statements, account facts, selected accounts, mandate/preparation evidence, transaction classifications, checkpoints, CPI register and transfer confirmations reuse the existing drafts, including incomplete drafts. The page explicitly reports that existing results are being shown; preparation timestamps, versions and audit events are not rewritten. Changed inputs rebuild the selected batch together so connected transfers remain consistent. A changed date matters only where a previously future inception/cutoff becomes valid, not merely because another day has elapsed. Approved claims retain their existing processing protections. Calculation-rule changes must bump `StatementBatchFingerprint`'s algorithm version; older unmarked drafts are rebuilt once to establish the fingerprint. This is JSON metadata and requires no schema migration.

`CapitalHwmAct365V1` is explicit and must be verified against the applicable signed mandate and administrator convention before approval:

1. Opening actual capital, benchmark capital and HWM capital come from inception or the last evidenced checkpoint.
2. The CPI-plus-spread benchmark compounds using actual days divided by 365. Dated evidenced CPI segments cover the entire period without gaps or overlaps. No projection CPI assumption is used.
3. Actual contributions increase benchmark and HWM capital. Withdrawals reduce both proportionally, using the verified capital value immediately before each withdrawal. Same-day entries retain their supplied chronological order. Recurring instructions do not prove individual payments. An allocation to a **different wrap fund** starts that receiving fund's protection at its actual amount/date under the applicable-fund mandate. A transfer of the **same wrap between accounts** carries its existing benchmark/HWM: changing account numbers does not erase underperformance. An internal allocation is not labelled new external client money.
4. Protected threshold is the higher of the adjusted benchmark and HWM. Only positive excess of closing value before this PDF is fee-bearing.
5. Apply the agreed exclusive-of-VAT fee rate to that excess; round the fee to cents, calculate/round VAT, then add them. Rounding is midpoint away from zero.
6. A positive fee establishes after-fee capital only after the actual approved deduction is evidenced. An approved zero-fee review preserves benchmark/HWM rather than resetting them downwards.

If the mandate instead uses an index ratio, a different compounding/day-count convention, different withdrawal treatment, changed terms within the period, or a different HWM reset, retain the preparation as a blocked draft. Do not claim that the generic convention is mandated by law or infer agreement from an unsigned template. Review and implement any genuinely required alternative before using it.

## Preparation and decisions

### Concise Approval And Download View

**Client PDF review** opens with one account summary and the totals for the exact
ready drafts awaiting KI approval. Incomplete or stale calculations are marked
separately and excluded from those totals; missing amounts are never shown as zero.
A KI records one reason and approves the displayed ready set through the existing
version-checked batch approval. Statement preparation, mandate settings, source rows
and reconciliation remain under **Detailed calculations and preparation**, with
individual **Calculation** links beside each account.

**Downloads** separates **Summary PDF** from **Detailed review package** for drafts,
and **Summary PDF** from **Detailed submission package** for approved positive claims.
Each approved pair uses the same claim selection. Where only approved zero-fee
reviews exist, an approved summary and detailed review package are available instead.
The short report contains account periods, status, fees/VAT/totals, the relevant
fee-bearing wrap schedule and, for final reports, claim references and KI authority.
It omits the evidence appendix and detailed calculation steps, not approval gates.
Drafts retain their warning; final exports require the same supported, KI-approved
claims and current permissions as the detailed exports. No download marks a claim
submitted, deducted or paid.

### Shared Calculation Convention and VAT

From **Investments > Annual PDF claims > Convention and VAT** (also linked on **Client PDF review**), the normal view shows the selected method, evidence, VAT treatment, version and recording/adoption identities and times. **Update settings** opens a separate deliberate editing page. An authorised Compliance reviewer records a proposed convention and evidenced VAT treatment; only a current governance-assigned KI with investment-management permission can adopt the convention. Recording a proposal, selecting it for a client, adopting it and approving a claim are distinct audited actions. Codex must never impersonate KI adoption or claim approval.

A pending shared convention appears once in the bell for authorised KIs with investment-management permission. It disappears after adoption; it is not duplicated for every client.

The supported shared convention is restricted to already evidenced SA wrap mandates with 15% excluding VAT above CPI + 3%, the supported capital/HWM method, and signed coverage of the period. It does not populate absent mandates, alter client fee percentages, adopt an alternative mandate, or overwrite a contradictory existing client-specific VAT rate. The annual CPI rate effective from each actual publication date is compounded by ACT/365. This is Kanaan's proposed/selected convention, not a universally required industry formula. ASISA's CIS and retirement-fund performance-fee standards provide fairness, consistency, disclosure and protection principles; applicability to a particular wrap must not be overstated.

**Apply shared convention and VAT** on a client review selects the configuration for matching evidenced scopes with an actual applicability reference. **Prepare all accounts** refreshes their calculations and connected transfers. While KI adoption is outstanding, the numerical draft can include evidenced VAT but remains blocked for submission. Other fee terms and 0% Stable SA reviews retain their separate treatment. A client-specific method can still be evidenced on **Fee setup / evidence** without using shared settings.

The firm-wide VAT reference must identify actual Kanaan evidence and the SARS fee-based-services/rate basis. It is not inferred merely from being an FSP or from the mandate saying "excluding VAT". For a standard-rated domestic claim, VAT is charged on the fee, not the full outperformance. Each wrap's exclusive fee and VAT are rounded independently before the account totals are added; this can differ by one cent from rounding tax on the aggregate fee.

Policy data use `ComplianceReferenceValues` category `PerformanceFeePolicy`, with optimistic version checking and audit snapshots; no additional schema migration is needed. Private evidence paths and client-specific findings are operational data, never seeded into code or migrations. Drafts retain the exact shared-policy snapshot. A proposal change or KI adoption invalidates dependent drafts and the batch fingerprint; approval rejects stale/unadopted snapshots. Already approved claims remain frozen and are not recalculated by configuration changes. The settings/evidence must also be established in the live environment; a code deployment alone does not invent operational approval there.

Conflicting quotes or other fee disclosures are separate structured review points. Their existence does not suppress supported arithmetic, but prevents approval even after the generic convention is adopted. A KI must record the actual reconciliation evidence and conclusion using **Record KI reconciliation** on the client review. Generic preparation or a Codex result cannot silently remove or resolve those points. Resolving a disclosure does not itself approve, submit or deduct the fee.

1. Register the account. Existing KCAS dates/names are starting clues only.
2. Upload the client statement(s) and prepare the selected account drafts. Where genuine evidence gaps remain, **Request Codex preparation** / **Resolve account gaps with Codex** creates a scoped handoff and bell action for authorised Compliance reviewers. Reuse the recorded compliance/fee evidence; only unsupported facts need further source research. No evidence scan is run or overwritten.
3. Codex uses `PerformanceFeeService.PrepareAsync(..., codex: true)` under an authorised Compliance account. Performer, recording account, actual recording time, evidence references and substantive findings are retained. The request version must match; a stale result is rejected.
4. Manual preparation uses the separate preparation page and is attributed as Manual. Incomplete inputs may be saved, but blockers prevent approval/export.
5. Review the draft PDF, calculation steps, scope and evidence. A current governance-assigned KI with investment-management permission deliberately approves. Codex cannot impersonate that decision.
6. Approved positive claims are immutable. Export a single claim or select multiple claims for the same administrator. The ZIP contains an administrator request PDF, CSV schedule, individual calculation PDFs and a hash manifest. Private source paths and internal preparation commentary are not exported. Downloading does not mark a claim sent.
7. Record the actual submission reference/date, administrator acceptance, exact fee deduction and subsequent payment receipts. Partial receipts remain outstanding. Fee deduction and payment are different events.
8. Only verified deduction, or an approved zero-fee review, advances the checkpoint. Original anniversary and historical input/result snapshots remain intact. A later monthly valuation import cannot rewrite a calculation.

An unsubmitted draft/approved claim can be voided with a reason (KI required for an approved claim). Submitted claims cannot be silently voided or altered: record an actual administrator return/decline with evidence, then prepare and approve a new claim without overwriting the returned record. A deduction already recorded cannot be retrospectively changed through this workflow; an actual discrepancy requires reconciliation, not an invented checkpoint. Repeated downloads use the same unique claim reference; approval blocks overlapping periods within the profile. Duplicate payment references for the same claim are rejected. The reviewer must also check cross-account capital scope, since a family relationship or administrator transfer does not itself establish fee continuity.

## Reminders, security and deployment

- Registered profiles appear in the bell within 30 days of the next review, and remain overdue until resolved. An additional bell action and initial-review table automatically surface unregistered, unsurrendered Equities SA / Global SA / Compulsory SA accounts whose recorded first anniversary is due within 30 days or overdue. These are mandate-research candidates, not assertions that a fee is payable. Stable SA is not automatically chased for a performance fee. Search can locate other mandates, historical accounts or accounts with unconfirmed dates. The first-investment date and scope must still be verified from evidence.
- Codex requests appear for ComplianceAdministrator/ComplianceApprover users with compliance-management permission. KI-ready calculations and administrator/payment actions appear for the corresponding authorised investment users.
- Hidden clients remain administrator-only in the register, detail, searches, bell and exports. Actual database permissions/approval status are rechecked by services, not trusted from a stale browser claim.
- The module adds separate profile/claim/event tables by EF migration. It does not rewrite existing investment transactions, valuations, mandate documents or compliance assessments. Historical inputs/results are stored independently of monthly valuation refreshes.
- The full schema and incremental migration script are shipped with the migration; deployment applies the normal KCAS migrations. Client-specific preparation is operational data, not seeded personal data or a code deployment.
# Staged Transfers And Split Allocations

The client review includes a statement-row capital trace for wrap redemptions through
the administrator's tax/cash staging scope and subsequent receiving allocations.
Uniquely balanced routes can join up to four source accounts into one receiving
account, or split one source into up to four receiving accounts. Dates may differ;
ambiguous or unbalanced candidates remain unresolved rather than being rounded into
agreement. Source/target rows and intermediate staging rows remain identifiable.
An isolated staging lot may include explicitly identified ordinary expenses or
reinvested interest where every row balances to the actual exit. The net settled
capital changes but its source benchmark/HWM is not reset or reduced merely for an
expense. Tax deductions, prior performance fees and unknown entries are not silently
treated as ordinary expenses, and unexplained differences cannot be entered as plugs.

A Compliance preparer records the actual scope of a staged route. Same-wrap continuity
carries source benchmark/HWM proportionally, including elapsed benchmark growth in
cash staging. A genuinely different-wrap allocation uses receiving capital instead;
it does not require the preceding fund's HWM simply because it is the same client's
money. Only an explicitly evidenced bespoke mandate could require cross-fund
continuity; that is not the default applicable-fund treatment. Actual carried source
and target drafts have linked-input dependencies. Missing same-wrap source protection,
uncertain ordering or missing CPI coverage prevents carrying. Approved claims remain frozen.

The **FundAllocation** statement review classification records an evidenced new
fund allocation with its actual source reference and reviewer. It is not a shortcut
for an unmatched same-wrap transfer: source/mandate research must establish the
distinction. Unclassified transfers remain visible blockers, not guessed contributions.
A uniquely balanced wrap-to-tax switch is a withdrawal from the source fund at the
actual exit date. Its later tax/redemption destination does not block that source
calculation. Partial withdrawals still require a same-scope pre-withdrawal valuation.

The trace distinguishes supplied transfer/settlement rows from an actual positive
opening-balance history gap. A receiving account starting at zero does not establish
the transferred capital's earlier HWM. Only earlier AIMS/Glacier history or a verified
fee checkpoint is requested, not investment history before entry to that administrator.
Same-wrap cash rebalances retain protection on the returned capital. Only the actual
amount left outside the wrap reduces benchmark/HWM proportionally to the evidenced
pre-exit value. Both cash legs and the unique return must reconcile; differences are
not invented fees, interest or balancing adjustments.
If that verified rebalance's full-wrap pre-exit prices are unavailable, retain all
existing protection rather than estimate a proportional reduction. The actual
closing value still excludes the residue. This explicitly disclosed conservative
overprotection can only reduce the claim, never increase it; it is not a fake price
or a general relaxation of the valuation requirement for actual client withdrawals.

## Earlier Holdings And A Later Wrap Beginning

A positive account opening is not automatically a missing fee-wrap beginning. KCAS
can recognise a later wrap's first allocation where every underlying has an explicit
zero opening, the initial allocations reconcile in units, and any incoming switch
balances uniquely to fully exited holdings outside the supported wrap scopes. It
does not assume that an unmatched transfer is fresh capital. A documented allocation
from a different fund is distinct from same-wrap continuity and uses its own actual
receiving capital under the applicable-fund mandate.
The original account history, allocation dates, amounts and source rows remain visible.

This establishes statement coverage for that wrap, not retrospective mandate authority
or settlement of the preceding fund's fees. A switch from an earlier outside-scope
holding adds an unresolved KI review item covering the mandate and separate fund
fee-period basis. Numeric draft arithmetic remains separate from permission to claim.
Only the trusted statement projection can create that review item; ordinary draft or
Codex preparation cannot remove it or forge its resolution. Source changes invalidate
an earlier scope reconciliation. Identical statement rebuilds retain a real KI decision.

The CPI reference files include January 2022 onward, using Stats SA P0141 headline
annual rates and actual release dates. They are source data for the authorised CPI
import, not hard-coded mandate assumptions or automatic deployment approval. The
client statement preparation fingerprint is versioned when interpretation changes,
so an older cached draft cannot conceal a newly recognised beginning or review gate.
