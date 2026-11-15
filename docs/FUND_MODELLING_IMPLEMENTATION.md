# Fund modelling delivery checkpoint

Research-only. No client investments, compliance assessments, fee claims or broker instructions are changed.

## Implemented in working tree
- Fund register, shared securities, saved per-fund scenarios and effective-dated holdings.
- Immutable run inputs/results, background queue, visible progress/cancellation and input-hash reuse.
- SciPy constrained optimisation, frontier and annual/monthly/quarterly/semiannual walk-forward tests.
- Price/total-return modes, missing-history checks, explicit currency mismatch rejection.
- PDF, Excel, CSV and JSON exports from the saved result.
- CSV monthly price preview/import with revision history.
- XLSX first-sheet import for Month, Price, TotalReturnIndex, Source columns.
- EF migration and dedicated Python runtime installer.
- Administrator-only encrypted fund-modelling transfer packages, preview/apply and runtime check.

## Still required before the full agreed module is complete
- Optional scheduled refresh (manual public-price refresh is implemented; no background market downloads).
- Excel column mapping, broker statement adapter and multi-currency FX histories.
- Evidenced fund-level fee overlay (currently explicitly excluded).
- Automatic integration of calculation-runtime checks into deployment preflight (a manual runtime check is available on transfers).
- Side-by-side scenario comparison, frontier point selection and current/proposed trade amounts.
- Populate other Kanaan funds from actual source records; do not invent missing histories.

## Verification
- Five Python tests passed for constraints/drift/compounding, infeasibility, gaps, no-look-ahead and return basis/currency checks.
- EOIBKRBrokers 13% and 16.4% cached-input comparison passed: maximum monthly-return difference below 0.00003 percentage points.
- Application build and six C# price-validation tests passed.
- Migration 20261114180158_AddFundModelling installed locally; incremental and initial schema scripts generated.
- Eight funds registered locally. EOIBKRBrokers holds the 22 researched securities and price histories. Runs 1 (13%) and 2 (16.4%) completed through the KCAS background worker. Both PDF and Excel generation succeeded.
- KCAS restarted at https://kcas.test:8443, using the tested FundModel build configuration on the normal backend port 5143. Local Python path is in ignored development settings.
- Remaining browser and complete-module acceptance checks are outstanding; do not label the full plan complete.

## Next implementation order
The usability follow-up adds audit-retained deletion for stopped runs, saved-scenario deletion without changing old runs, security monthly-return tables (live and frozen run inputs), and graded frontier axes with current/proposed markers. Fifteen affected C# tests passed; actual saved-result layouts checked at 1440px and 390px. KCAS restarted with the FundModelUx build.

1. Browser/UI and report visual verification, permission/integration tests and native XLSX validation.
2. Evaluate additional public/authorised data sources and optional scheduled refresh separately.
3. Extend deployment preflight with the calculation runtime check if automatic checking is desired.
4. Mandate-specific fee overlays and FX conversion inputs; then broader spreadsheet/statement adapters.
5. Scenario comparisons, interactive frontier selection, trade amounts and remaining source population.

The single-instance background worker marks interrupted runs on startup; multi-instance workers need a lease before deployment in such a configuration. Initial XLSX exports are data reports, not a replacement editable optimiser workbook.

## Runtime
`deploy/windows/Install-KCAS-FundModelRuntime.ps1` creates an isolated runtime from pinned requirements. Configure `FundModelling__PythonPath` for the KCAS service account. Never require desktop Excel or Solver on live.

## Encrypted fund modelling transfers
- Administrator menu: Investments > Fund modelling transfers. Also available from Transfer fund modelling on the fund-modelling page, with the current fund preselected.
- Select one or more funds, optionally include completed runs, enter a reason and passphrase, then create/download the `.kcas-fund-models` package. It includes each selected fund's holdings, cash, saved scenarios and shared security prices/source references. Runs carry their original frozen inputs, results, performer and dates; queued, running, failed and deleted runs are excluded.
- Deploy this code to live first. On the same menu there, choose the file, enter its passphrase, preview, record the reason, confirm and apply. Applying never reruns optimisation, fetches prices, scans folders or alters client investments/compliance. No additional migration is required beyond the existing fund-modelling migration.
- Funds and runs match stable keys, not database IDs. Live assigns its own numeric IDs; the apply audit records the original-to-live mapping. Identical imports do not duplicate records. Live-deleted runs are not resurrected. A same-name fund with a different key blocks import rather than guessing its identity.
- Later source updates can update a previously imported fund/security only while its live content still matches the last imported baseline. Independent live edits conflict. Changed shared security histories also block if another live fund outside the package uses them. Include all affected funds or reconcile deliberately; no overwrite option is provided.
- Apply repeats the persisted administrator check, package validation, conflict and stale-preview checks inside a serializable transaction. Export/apply provenance and baselines use existing ModelEvents; source audit history itself is not merged or impersonated.
- Packages use purpose-specific authenticated AES-GCM encryption and PBKDF2-SHA256, matching existing transfer protection. Maximum plaintext 50 MB, 25 funds, 1,500 securities and 500 completed runs. Package files stay outside wwwroot; the authorised download requires a recorded export.
- Saved results/reports do not need Python. New live calculations do: install the isolated runtime with the existing installer, configure `FundModelling__PythonPath`, restart KCAS and use Check runtime on this server. The check verifies Python/NumPy/SciPy and the deployed worker without installing or downloading anything. Runtime executables, credentials, machine-specific settings and live IDs are not transferred.
- Verification (16 November 2026): 65 affected C# tests passed, covering authenticated encryption, remapped live IDs, preserved PDF/XLSX results, repeat imports, supported source updates, live edits, stale previews, shared-history conflicts, retained run deletion and persisted administrator revocation. Published-build browser export/download/preview/repeat-apply/runtime checks passed at 1440px and 390px, with no horizontal overflow. EOIBKRBrokers' real 22 securities and five completed runs passed read-only package validation. Local Python/NumPy/SciPy check passed. Local KCAS now runs the FundTransfer build on port 5143; live deployment/import has not been performed.

## Limitations
Results now includes Annual performance immediately above Monthly returns. Each calendar-year row compounds saved monthly gross, after-trading-allowance and equal-mix returns, with exact partial-year coverage. Partial periods are not annualised. The table is absent for optimisation-only runs with no simulated months. Existing run snapshots and calculations are unchanged; this does not add a new Excel sheet.

Security returns now offers 3-year, 5-year and Max saved-history presets. They set both the displayed price-table range and CAR endpoints, anchored to the latest saved month. Exact 36/60-month opening observations are required; unavailable fixed periods are disabled rather than silently shortened. Max uses existing records only and never downloads more data. Custom month-end selections remain available. A selected-security-only month-end refresh reuses the existing authorised preview/revision/save workflow; no scope selector or all-fund request is exposed there. It is available to investment managers on current histories only, never on frozen run histories. Saving refreshed prices reloads the displayed series and recalculates the selected preset.

Security history: Prices > View prices / CAR opens the chosen underlying's Security returns tab. The table shows saved monthly prices, monthly returns and sources. CAR from/to month-end controls calculate compounded annual growth using elapsed calendar months / 12, separately from cumulative growth and separately for price versus dividend-adjusted total return. Missing endpoint data never falls back to another basis. The year filter affects the table, not the selected CAR period. Editing/imports remain separate. Also available for frozen run histories. Verified with 26 focused tests and rendered desktop/mobile checks; local runtime is now FundHistory on port 5143.

Results labelled net deduct the trading allowance only, not Kanaan management/performance fees or investor tax. Price mode excludes distributed dividends. Selected-universe tests retain selection/survivorship bias. Historical targets are not risk guarantees.

## Manual public prices and deletion controls
- Investments > Fund modelling > fund > Prices > Refresh market prices. Choose all fund holdings or one registered security.
- Uses the saved Yahoo Finance symbol (including the exchange suffix/share class). No paid account or API key, no browser/background polling and no scheduled refresh. Only the ticker and date range leave KCAS, not client or portfolio details.
- Daily public history is reduced to the last quoted trading date in each completed exchange-local calendar month. Current-month prices, stale final observations and missing final quotes are not manufactured into month-end values.
- Currency must match exactly; GBp is not silently treated as GBP. Price is the provider's split-adjusted close; the separate total-return series uses its dividend-adjusted close. Reference: https://help.yahoo.com/kb/SLN28256.html.
- Download preview shows new, changed and unchanged months. Historical revisions require explicit acceptance; saved histories and completed-run snapshots remain unchanged until saving. Incomplete replacement of a revised history is blocked to avoid mixing adjustment scales.
- Saves are permission checked, concurrency checked and recorded in ModelEvents, including previous prices, changed rows and retrieval/source details. No schema migration is required.
- Successful requests are cached in memory for 15 minutes; failures for 2 minutes. Requests are serialised and spaced by at least 2 seconds, with a 30-second timeout and 16 MB response limit. Restart clears this cache.
- Public endpoints are best-effort, not a contracted feed or assurance of redistribution rights. Unavailable/unsupported symbols retain the CSV/XLSX option. Do not substitute a similarly named share class. No purchase or subscription is created.
- Holding, scenario and stopped-run deletion use compact bundled trash icons with named confirmation dialogs. Cancel receives initial focus; Escape cancels. A holding is removed only from the draft until Save fund; shared prices survive. Deleted runs retain the audit record.
- Verification (15 November 2026): 24 focused C# tests passed. Actual authorised service previews fetched TSLA and XS2D.L successfully through October 2026, without saving price revisions. All 22 local EOIBKRBrokers symbols were mapped from their existing verified source metadata (only blank mappings filled). New components were rendered and checked with Playwright at 1440px and 390px, including icon dimensions, initial Cancel focus and Escape cancellation. The full authenticated save/delete workflow was not exercised against user records. Local KCAS restarted using FundRefresh on port 5143.
