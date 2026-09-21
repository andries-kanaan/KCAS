# Unresolved Investment Register

## Purpose

This register tracks investment accounts that cannot yet be reconciled as either
current or historical from the available evidence. It is an operational control:
an absent valuation must not be treated as proof that an account was surrendered.

Each entry remains in KCAS as `NeedsFollowUp` until provider evidence supports
the recorded outcome. Once resolved, retain the linked evidence and reconciliation
audit record, then remove the item from this register or mark it resolved with the
resolution date.

## Open Items

| Kanaan ID | Client | Account | Provider / product | Last supported status | Evidence reviewed | Required resolution | KCAS treatment | Investigation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 024 | Nicolaas Dirk Oosthuizen | Metropolitan `90725`; unmatched AIMS `RA00020632` and `SRA5050497` | Metropolitan voluntary investment; AIMS/ABSA retirement annuity and savings component | AIMS valuations total R11,366.44 at 9 September 2026; the older Metropolitan account has withdrawals through 2013 but no documented endpoint | Imported accounts, transactions, current valuation rows, local evidence root and connected live-network folder paths | Recover the client/provider file; create or restore the two AIMS account records; establish whether Metropolitan `90725` transferred, surrendered, or remained separate | Keep assessment open; do not infer a Metropolitan-to-AIMS link or surrender date | [Detail](../outdated-kyc/KID-024-NICOLAAS-DIRK-OOSTHUIZEN.md) |
| 079 | Andre Deon Neethling | `LA20007498` | AIMS / Absa Glacier Living Annuity | Current through 2026-04-13; November 2025 statement value R1,070,878.02 | June 2025 statement, November 2025 income review, October 2025 processing confirmation, April 2026 contact-detail confirmation, related folders and legacy account record | Obtain a current Absa/Glacier valuation. Only obtain closure evidence if the provider confirms that the account has ended. | `NeedsFollowUp`; do not capture a surrender date or finalise the client assessment. | [Detail](LA20007498-NEETHLING.md) |
| 194 | Andrzej Kijko | `3550365` | Sanlam / Glacier Retirement Annuity, KMB (Kanaan Met Balanced) | Active through 2015-11-20; later status unknown | 2012 Central Retirement Annuity maturity and transfer records, 2012 opening transfer of about R189,858, November 2015 signed Glacier fee instruction, client and related folders, legacy notes, transactions, valuations, KYC records and legacy account record | Obtain a current Sanlam/Glacier statement or a formal surrender/transfer confirmation identifying the effective date and destination or payout. | `NeedsFollowUp`; do not capture a surrender date or finalise the client assessment. | [Detail](3550365-KIJKO.md) |

## Resolution Standard

For every item, record one of the following before changing its status:

1. **Current account:** a provider statement or valuation identifying the account,
   valuation date and value. Link the evidence, correct or load the valuation, and
   reconcile the account as current.
2. **Surrendered account:** a provider confirmation, statement or transaction
   record identifying the account and effective surrender date. Capture the date,
   underlying withdrawal transaction where known, source document and reason;
   then reconcile as `Historical - surrendered`.
3. **Transferred account:** evidence of the effective transfer date and successor
   account or destination. Link both accounts where applicable and reconcile as
   `Transferred`.

Do not infer a zero balance, surrender date, transfer destination or historical
classification from an old statement, an administration fee form, a missing
monthly valuation or the absence of later documents.
