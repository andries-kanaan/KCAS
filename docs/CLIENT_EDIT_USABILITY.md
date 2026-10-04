# Client edit form

The new-client and edit-client pages share the same controls and validation.

- Titles, languages, gender, marital status, qualifications, contact/address types and relationships use dropdowns. Existing unlisted values remain selectable without automatic replacement. Open-ended choices also allow a custom entry; gender offers Male/Female and retains an existing unlisted value.
- Sections have navigation links, responsive field layouts and a sticky save bar. Family employment/income details expand when needed. Row removal updates the form's unsaved state; saving disables repeat submissions.
- SA ID validation checks 13 ASCII digits, an encoded calendar date, the citizenship digit and checksum. It is not a Home Affairs lookup or proof of identity. Reference: [SARS IT3 business requirements, Appendix J, PDF page 276](https://www.sars.gov.za/wp-content/uploads/SARS_External-BRS_2022_IT3s_v4.0.0T-9.pdf#page=276). The population-register digit is not restricted to 8/9, consistent with that source's qualification.
- Existing unchanged invalid IDs are retained and flagged for verification. Newly entered or changed invalid IDs are rejected by both the form and the save service. Identity fields remain optional when recording a prospect; compliance acceptance still requires the applicable evidence.
- Passport number, issuing country and expiry are stored separately in the personal profile. A passport number requires an issuing country. Expiry produces a warning, not automatic erasure or a claim of current verified identity.
- Passport numbers are searchable and included in screening scope and client-acceptance scope when present. Existing scope fingerprints remain unchanged for clients without passport numbers.
- Validation covers required surname/entity name, database string limits, email addresses, non-negative amounts, percentage ranges and future relationship birth dates. Errors appear next to applicable fields and in a form summary. All save-service validation runs before applying edited values.

## Deployment

Apply `20261004154805_AddClientPassportDetails` through the normal deployment process. It adds three nullable columns to `ClientPersonalProfiles`; no existing client data is rewritten. The incremental SQL and fresh-database SQL accompany the migration.
