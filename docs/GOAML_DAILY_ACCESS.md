# goAML daily access and email reminder

## Daily operation

Open **Compliance > Overview > goAML check**, `/compliance/goaml`.

Select **Open goAML**. An authenticated, antiforgery-protected POST records the South African date, actual UTC time and signed-in Compliance.Manage user, then redirects the new tab to the configured HTTPS goAML portal. The KCAS page refreshes its status automatically. No photograph, screenshot, notes or separate completion action is required. One record per date is retained; repeated clicks do not replace a completed record or another checker's identity.

The status is **Daily access recorded**, not **No new messages**. A click records an access action, not proof that external login succeeded or that the message board was read. The user still reads the message board. Optional **Record message outcome or access problem** supports nil results, an outage description or a message requiring action. Action-required records still create a High-priority compliance work item. Completed outcomes cannot be overwritten. Previous checks, audit records and screenshots remain available. Access-only checks are supported in encrypted goAML transfers; an older deployment must be upgraded before importing that status.

## Email delivery

The background worker runs every five minutes without requiring a login. Starting at the configured hour (default 08:00 South African time, UTC+2), it sends one reminder for the current date if access is outstanding. It runs every calendar day, including weekends. There is no backfill of emails for older missed days. If KCAS starts later that day, it sends the outstanding current reminder then. An already recorded access or outcome suppresses that day's reminder.

The email links to `/compliance/goaml` on the configured live KCAS address, not directly to the external portal. Opening an email or its KCAS link does not create an access record. The configured KCAS address must be reachable from the recipient's computer/network.

The unique per-day delivery ledger and conditional database claim prevent multiple workers or restarts from routinely sending duplicates. **Sent** means accepted by the SMTP server, not confirmed arrival in the inbox. A rejected single recipient is retried after 30 minutes, at most three attempts that day. A timeout, ambiguous transmission or interrupted process is shown as **Uncertain** and is not automatically resent, because the server might already have accepted the email. Check the sender's delivery log before an administrator resets that day's status to Pending. No UI action claims a successful email when sending is disabled or failed.

The **Email reminder** section shows configuration availability and the most recent recorded delivery or problem. The page does not expose SMTP credentials.

## Deployment configuration

The migration `AddGoAmlReminderDeliveries` adds the delivery ledger. It does not replace the database, backfill completed checks, remove screenshots or send email by itself. Use the normal reviewed deployment/migration process. The fresh-database schema also includes the new table.

Only the live server should have sending enabled. Repository defaults leave sending disabled so developer/test copies do not email the real user. Configure these keys in the live server's private configuration or environment, not tracked source files:

| Key | Setting |
|---|---|
| `GoAmlReminders:Enabled` | `true` after the approved sender is configured |
| `GoAmlReminders:Recipient` | Primary goAML user's email; current default is Andries's Kanaan address |
| `GoAmlReminders:PublicBaseUrl` | Live HTTPS KCAS base address, currently `https://kcas:8443` |
| `GoAmlReminders:HourLocal` | `8` by default |
| `Email:Smtp:Host` | Approved SMTP server or relay |
| `Email:Smtp:Port` | Usually `587` for STARTTLS; use the mail provider's value |
| `Email:Smtp:EnableSsl` | `true` for STARTTLS; implicit TLS on port 465 is not supported by this sender |
| `Email:Smtp:From` | Approved sender email address |
| `Email:Smtp:UserName` | SMTP login if required; blank for an approved anonymous relay |
| `Email:Smtp:Password` | Secret supplied through protected server configuration/environment only |

Environment keys use double underscores, for example `Email__Smtp__Password`. The sender uses platform TLS and certificate verification; there is no certificate-validation bypass. Do not disable TLS for authenticated SMTP. OAuth-only mail providers need an approved SMTP relay or a separately implemented provider integration; do not assume an ordinary account password will work.

After configuring and restarting live KCAS, check the email section and the recipient's inbox. A real delivery test remains required; automated tests use an isolated fake sender and do not send real email. This reminder sender does not alter the existing Identity password-reset/registration mail behavior.

## Policy alignment

This replaces the earlier KCAS screenshot-required operating description. When the working RMCP section 15 and supporting goAML procedure are next adopted, describe the automatic timestamped access log and optional outcome/escalation, not mandatory daily screenshots or an automatically verified nil result. Do not change signed submissions or historical evidence retrospectively.
