# Investment returns

## Scope and terminology

Clients > client > Investments > Investment returns (on current and historical accounts).

CAR here is the **annualised investor return**, calculated by dated money-weighted
XIRR. It is not cumulative growth, the fund's published time-weighted performance,
or a forecast. With no intervening cash flows it agrees with compound annual growth.
It incorporates the size and actual date of each contribution and withdrawal.
Periods under one year are explicitly labelled annualised. Net gain is closing
value plus payments received less contributions; it is not a percentage return.

- Current investment: recorded inception of this account to its dated current
  valuation (or dated final payout for a surrendered account).
- Long term: original inception through the existing reviewed transfer chain to
  this account. Matched transfer-out and reinvestment amounts are displayed but
  excluded as internal movements, including time in transit between their dates.
  With no earlier recorded transfer, the scopes coincide. This is **not** proof
  that no earlier unrecorded investment existed.

The main chain follows funding of each destination account at its inception. A
reviewed source whose final payment occurs later is separate top-up funding, not
a competing original predecessor. Its contribution is included once on the actual
recorded date in the destination; its earlier capital, withdrawals and performance
are outside this chain. This is an investment-chain return, not a combined return
on every investment the client ever held. The view explicitly notes this boundary.

## Inputs and safeguards

Only final, non-deleted, dated one-off movement amounts are used. Intermediate
balances are not contributions and are not counted as payouts. Contributions are
negative and money paid to the investor is positive. Current valuation lines are
summed once, only within the selected client/account and with one common date.
Transactions after that date are not included. A zero closing valuation is a real
value, distinct from a missing value. A surrender date without a payout amount is
not evidence of a total loss.

A full payout need not occur on the administrative surrender date. Earlier staged
payments are accepted where the latest evidenced surrender/transfer review matches
the account snapshot and recorded closure date. Without that review, earlier partial
withdrawals are not assumed to exhaust the account. Actual payment dates and amounts
remain unchanged; no artificial payment is inserted on the surrender date. The
historical return endpoint is the last recorded payout, not administrative closure.
Amount-bearing movements after surrender require settlement-history confirmation
rather than being silently omitted. This does not certify that every movement has
been captured or resolve conflicting narrative totals.

Monthly/annual/other recurring instructions are **not** expanded into assumed
payments. These histories require actual payments from statements. Rates based on
recorded movements remain conditional on completeness of those movements: a
compliance investment-history review alone does not certify cash-flow completeness.
This feature does not run evidence scans or rewrite investment history.

Long-term linking uses only the latest `Transferred` reconciliation with matching
account snapshot, surrender date and an evidence reference. A linked-family ID,
same folder or similar account number is not a transfer. Duplicate/continuation
and wrong-client duplicate outcomes are not additional invested capital. Cycles,
missing predecessors, multiple predecessors, cross-client ownership changes,
unmatched transfer amounts (including fees/retained cash), undated movements,
negative/reversal amounts and preliminary transactions are reported rather than
silently incorporated. Update the existing reconciliation only when evidenced.

Staged original-continuation transfers are matched using a terminal sequence of
source payments, grouped by payment date, whose total exactly equals the destination's
inception contributions. Split payments and an aggregated reinvestment are supported.
Actual dates are retained; administrative closure need not equal payout or reinvestment
date. The matcher does not search arbitrary subsets of old withdrawals, split a day's
payments arbitrarily, or infer fees to force a match. Earlier external withdrawals
remain included. Contributions between proposed payout stages require allocation.
Multiple sources funding inception remain ambiguous; later top-up source links do
not compete with the original chain. Each source link must still have current evidence,
matching snapshot/date and the same client ownership, with recorded later contributions
for top-up links. No source record or contribution is manufactured to satisfy a link.

ZAR returns use recorded historical ZAR cash flows and ZAR valuations, never the
latest exchange rate applied backwards. A native-currency view additionally
requires explicit matching fund-reference currency throughout the chain. Unknown
or mixed currencies are not added together. Fees reflected in movements/valuations
affect the result; unrecorded fees are not invented and gross/net-fee performance
is not claimed. Fund switches inside the same account have no separate dated
fund-level movement ledger: the view is account-level, not a manufactured
since-current-fund return. Splits/mergers/partial transfers require an explicit
allocation before they can be combined.

## Numerical calculation

ExcelFinancialFunctions 3.2.0 provides the established XIRR solver, on an
actual-days/365 basis. Same-day external movements are netted. Multiple starting
guesses and a present-value residual check reject non-finite, unreliable or
detected multiple-root results. Searching multiple guesses is not a mathematical
proof that all roots have been found; unusual financing/reinvestment patterns
still warrant review. Total loss with a recorded zero terminal value is -100%.

Primary references:
- [CFA Institute: Rates and Returns](https://www.cfainstitute.org/insights/professional-learning/refresher-readings/2026/rates-and-returns)
- [GIPS handbook: money-weighted returns and cash flows](https://www.gipsstandards.org/standards/gips-standards-for-firms/gips-standards-handbook-for-firms/)
- [ExcelFinancialFunctions API](https://fsprojects.github.io/ExcelFinancialFunctions/reference/excel-financialfunctions-financial.html)
- [Solver compatibility and limitations](https://fsprojects.github.io/ExcelFinancialFunctions/compatibility.html)

## Deployment

This is a read-only derived view with no additional schema or stored return values.
Deploy the code to live. The calculation then uses live's own history, valuations,
fund references and reconciliation links. Existing transfer packages already carry
investment reconciliation links; missing statement cash flows are not magically
provided by those packages. A monthly valuation refresh changes the endpoint, not
the historical movements. No separate return-data transfer or backdated approval
is created.

## Verification (4 October 2026)

- Application build succeeded; 42 focused calculator, service, summary, status and
  reconciliation tests passed. Included dated top-ups/withdrawals, losses, full
  surrender, three-administrator continuity, unknown earlier origins, transfers
  between client records, ambiguous roots, currency and hidden-client access.
- Subsequent staged-payout validation passed 51 tests, including earlier payments,
  missing/stale/superseded closure reviews and post-surrender movements. Read-only
  verification confirmed both return periods for the reported historical account.
- Original-chain versus later-top-up matching passed 64 focused tests, including
  a database-backed service test with staged inception funding and a separate later
  source. Read-only service verification confirmed the full original-inception
  period through the latest current valuation; client data remained unchanged.
- Read-only calculations were exercised against the local current-account data.
  Missing cash-flow evidence was reported; no client/account data was changed.
- Existing investment regression fixtures were made non-colliding and rolled back
  or cleaned up using only their own created records. The pre-existing test schema
  was preserved, not dropped/recreated.
- Browser visual verification was unavailable because the browser connection failed
  before navigation (`sandboxPolicy` metadata missing). Build/service checks do not
  replace a visual desktop/mobile review of the new page.
