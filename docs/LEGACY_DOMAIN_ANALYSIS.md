# KCAS Legacy Domain Analysis

Last updated: 2026-05-31

## Source Reviewed

- Legacy Yii1 app: `C:\wamp64\www\yii\demos\kanaanclients`
- Controllers: `protected\controllers`
- Models: `protected\models`
- Views: `protected\views`
- Legacy database: MySQL `kanaanclients`

This document captures the observed legacy workflow shape so the Blazor rewrite can keep the useful business behavior without copying the old schema directly.

## Main Legacy Modules

### Clients

Observed sources:

- `ClientController.php`
- `Client.php`
- `views\client`

The client workflow is the center of the legacy app. One wide client record supports personal details, spouse/family information, contact details, addresses, employment, income, retirement fields, goals, will/bank details, tax details, representative fields, KYC dates, and miscellaneous operational fields.

Important behavior:

- Client screens are split into full edit plus sectional views for contact, personal, and family information.
- `kanaan_id` is not a legacy leftover. It is a current internal administration identifier used to group family units, and multiple clients can intentionally share it.
- Legacy client row IDs should remain import traceability only. They must not become KCAS business identifiers.

Current KCAS coverage:

- Normalized client profile, contacts, addresses, relationships, financial profile, and legacy snapshots exist.
- Client create/edit, relationship editing, notes workflow, Kanaan ID generation, and Kanaan ID family sharing are implemented.

### Client Notes

Observed sources:

- `ClientnoteController.php`
- `Clientnote.php`
- `views\clientnote`

Client notes are a high-volume operational history tied directly to clients. They support standard create, update, delete, index, admin, and view actions in Yii.

Current KCAS coverage:

- Imported notes display on the client detail page.
- Notes can be added, edited, finalized, and soft-deleted in KCAS.

### KYC And Policy Records

Observed sources:

- `KycController.php`
- `Kyc.php`
- `KycrecommendController.php`
- `Kycrecommend.php`
- `views\kyc`
- `views\kycRecommend`

KYC records hold classified financial planning and product/policy data. They link to main and sub classes, carry administrator/product/policy fields, values, debt, premiums, income, and flags such as include, surrender, RA, PF, RP, and quote.

Important behavior:

- KYC includes transfer/copy actions for moving records or classified rows between clients/family contexts.
- Subclass selection is dependent on the selected main class.
- Life and disability cover should be derived from KYC classification data, not only from `tbl_client`.

Current KCAS coverage:

- KYC/policy records are imported as seed data and summarized on client detail pages.
- Full KYC create/edit/transfer workflow is not rebuilt yet.

### Investments, Accounts, And History

Observed sources:

- `InvestmentaccountController.php`
- `Investmentaccount.php`
- `InvestmenthistoryController.php`
- `Investmenthistory.php`
- `FundController.php`
- `Fund.php`
- `views\investmentaccount`
- `views\investmenthistory`
- `views\fund`

This is the next major operational area after clients, notes, and KYC. The legacy app separates investment accounts, investment history rows, and fund summaries.

Important behavior:

- Investment accounts belong to clients and link to fund/LISP/product reference data.
- Creating an investment account can redirect directly into investment history maintenance.
- Investment history tracks dates, descriptions, exchange rates, investment and withdrawal amounts, balances, frequencies, increases, final status, and deleted status.
- Fund workflows include fund summaries and report-style views.

Current KCAS coverage:

- Normalized investment account and investment transaction entities exist.
- `tbl_investmentaccount` and `tbl_investmenthistory` are imported as seed data.
- Client detail pages show read-only investment account summaries and recent transaction history.
- Investment editing, fund summaries, and report calculations are not rebuilt yet.

### Reference Data

Observed sources:

- `CompanyController.php`
- `CompanyproductController.php`
- `FundnameController.php`
- `LispnameController.php`
- `MainclassController.php`
- `SubclassController.php`
- `MiscinfoController.php`

Reference data supports the classification and product workflows. Main class/subclass data is especially important for KYC behavior.

Current KCAS coverage:

- Some reference values are preserved through imported KYC summaries and raw snapshots, but reference management screens are not rebuilt.

### Reports And Planning Forms

Observed sources:

- Client report/input actions such as `Raa`, `Capreq`, `Retreq`, `Finplandata`, `Kycb`, `Lifecover`, and `Caa`
- Excel/report actions such as `Raareport`, `Contribexcel`, `Immcapreqexcel`, `Retcapreqexcel`, `Finplanexcel`, `Idshfreport`, and `IdsEntityreport`
- `views\reports`

Reports appear to depend on client profile data, KYC classifications, investments, fund values, and planning parameters.

Current KCAS coverage:

- Reports are intentionally deferred until the underlying normalized domain data is present.

## Import Policy

Current imports from `kanaanclients` are test and seeding data only. They are used to validate mapping, screen behavior, and realistic workflow shape while the rewrite is being built.

They are not the final production dataset. Once KCAS is ready for deployment and switch-over, the current seed data can be cleared and the latest full legacy data can be imported as the final migration.

## Recommended Next Slice

Review the imported investment display, then choose the next operational investment or KYC slice.

Candidate investment follow-up scope:

- Add investment account editing.
- Add investment transaction editing.
- Add fuller fund/current-value summaries from `tbl_fund`.
- Rebuild selected fund summary/report views only after the imported account/history display is accepted.

Reasoning:

- Client, notes, relationships, and KYC summaries already provide the client-centered foundation.
- Investment accounts and history are now available for review in KCAS.
- The next step should be driven by what is most useful operationally after reviewing that data: editing workflows, fund summaries, or deeper KYC management.

Deferred until after this slice:

- Fund summary reports.
- KYC transfer/copy workflow.
- Planning and Excel-style reports.
