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

## Still required before the full agreed module is complete
- Online refresh adapter/scheduler with configured business-use permission and credentials.
- Excel column mapping, broker statement adapter and multi-currency FX histories.
- Evidenced fund-level fee overlay (currently explicitly excluded).
- Encrypted transfer preview/apply and integration with deployment runtime preflight.
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
2. Online provider refresh with persisted job results and environment switches.
3. Encrypted fund transfers with immutable snapshots and preview conflict checks.
4. Mandate-specific fee overlays and FX conversion inputs; then broader spreadsheet/statement adapters.
5. Scenario comparisons, interactive frontier selection, trade amounts and remaining source population.

The single-instance background worker marks interrupted runs on startup; multi-instance workers need a lease before deployment in such a configuration. Initial XLSX exports are data reports, not a replacement editable optimiser workbook.

## Runtime
`deploy/windows/Install-KCAS-FundModelRuntime.ps1` creates an isolated runtime from pinned requirements. Configure `FundModelling__PythonPath` for the KCAS service account. Never require desktop Excel or Solver on live.

## Limitations
Results labelled net deduct the trading allowance only, not Kanaan management/performance fees or investor tax. Price mode excludes distributed dividends. Selected-universe tests retain selection/survivorship bias. Historical targets are not risk guarantees.
