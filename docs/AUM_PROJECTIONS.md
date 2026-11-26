# AUM projections

Open **Investments > AUM projections** with any approved KCAS account. Guests and accounts awaiting approval have no access. An administrator imports the prepared JSON snapshot. Approved users can view, run, save and download scenarios. Saving a run records the user and does not change the shared snapshot or defaults. **Business forecast** opens by default with the workbook's annualised baseline, five projection-year columns, recurring monthly run rates and break-even figures. **Inputs** shows the snapshot assumptions and sets custom CARs. Leave a custom rate blank to use its default. **Reset to defaults** clears the overrides without altering earlier saved runs.

The other tabs show **Scenario comparison**, **Monthly forecast**, **Fund monthly**, **Client support**, **EO and IBKR**, **PDF schedule**, **SA PDF estimates**, and aggregate **Mortality** coverage and expected estate withdrawals. The workbook's raw source and actuarial calculation sheets are not copied. Save a run to freeze its assumptions and results. The matching Excel tabs are a formatted values export, not a second, recalculating model. The PDF remains a concise summary.

Snapshots prepared before the layout upgrade still run, but the original document baseline reference and aggregate mortality coverage are only shown if present in the snapshot. Import the updated snapshot and run again for full detail; earlier saved runs retain what was recorded at the time.

## Inputs and boundaries

Version 1 uses the reviewed workbook *Kanaan AUM and business projection - separate EO and IBKR CAR scenarios.xlsx* as a dated input snapshot. It does not use the workbook's projected outputs at runtime. It calculates 60 months from the snapshot date. Shares, contribution schedules, mortality coefficients, fee assumptions and operating expenses are snapshot inputs. They do not silently refresh from live values or price feeds. Nothing in the module changes an investment, valuation, client instruction or performance-fee claim.

- Equity Offshore and EO IBKR Brokers have separate return paths. The broker sleeve starts inside the existing EO wrapper, not as new AUM or a merger. EO's quarterly high-water mark and CPI test apply at wrapper level.
- Projected client support enters the investment balances as deposits and is also a company expense. **Client support** lists eligible scope balances, withdrawal rates and stopping decisions by month. Row numbers refer to the source workbook's **Client support** sheet; names are not copied.
- The SA performance fee is a planning estimate, not the client-anniversary claim calculation. It responds to changes in the relevant SA CARs. Mortality is an expected-value adjustment limited to evidenced eligibility in the snapshot, not an individual death forecast.
- The workbook used for the first snapshot is dated 8 October 2026. A fresher snapshot requires review of both operational balances and assumptions; an Excel refresh or a CAR edit is not a substitute for that review.

## Preparing a snapshot

Use the workbook layout supported by `tools/prepare-aum-snapshot.py`, with Excel formulas already calculated and saved. With Python 3.11+ and openpyxl installed:

```powershell
python tools/prepare-aum-snapshot.py "path-to-reviewed-workbook.xlsx" "private-output.json"
```

The helper reads the workbook without modifying it. The JSON keeps its file name and SHA-256 reference and omits names, birth dates and account numbers. Balances and business assumptions are still confidential: keep snapshots outside Git and share them only with authorised administrators. To use the same inputs on live, import the same private snapshot there after deployment. Runs already saved on either system remain fixed.
