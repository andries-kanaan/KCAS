# goAML daily access and email reminder

## Daily operation

Open **Compliance > Overview > goAML check**, `/compliance/goaml`.

Select **Open goAML**. An authenticated, antiforgery-protected POST records the South African date, actual UTC time and signed-in Compliance.Manage user, then redirects the new tab to the configured HTTPS goAML portal. The KCAS page refreshes its status automatically. No photograph, screenshot, notes or separate completion action is required. One record per date is retained; repeated clicks do not replace a completed record or another checker's identity.

The status is **Daily access recorded**, not **No new messages**. A click records an access action, not proof that external login succeeded or that the message board was read. The user still reads the message board. Optional **Record message outcome or access problem** supports nil results, an outage description or a message requiring action. Action-required records still create a High-priority compliance work item. Completed outcomes cannot be overwritten. Previous checks, audit records and screenshots remain available. Access-only checks are supported in encrypted goAML transfers; an older deployment must be upgraded before importing that status.

## Email delivery

**Send goAML email**, beside **Open goAML**, lets a Compliance.Manage user request one manual email to the configured recipient. It works even when the daily schedule is disabled, uses this environment's configured link, and identifies its host in the subject. It does not record access or change daily delivery/deduplication records. KCAS logs the requesting user and acceptance time; SMTP acceptance still requires inbox confirmation. The button is disabled while a send is in progress. Failed/uncertain sends are not automatically retried.

The background worker runs every five minutes without requiring a login. Starting at the configured hour (default 08:00 South African time, UTC+2), it sends one reminder for the current date if access is outstanding. It runs every calendar day, including weekends. There is no backfill of emails for older missed days. If KCAS starts later that day, it sends the outstanding current reminder then. An already recorded access or outcome suppresses that day's reminder.

The email links to `/compliance/goaml` on the configured live KCAS address, not directly to the external portal. Opening an email or its KCAS link does not create an access record. The configured KCAS address must be reachable from the recipient's computer/network.

The unique per-day delivery ledger and conditional database claim prevent multiple workers or restarts from routinely sending duplicates. **Sent** means accepted by the SMTP server, not confirmed arrival in the inbox. A rejected single recipient is retried after 30 minutes, at most three attempts that day. A timeout, ambiguous transmission or interrupted process is shown as **Uncertain** and is not automatically resent, because the server might already have accepted the email. Check the sender's delivery log before an administrator resets that day's status to Pending. No UI action claims a successful email when sending is disabled or failed.

The **Email reminder** section shows configuration availability and the most recent recorded delivery or problem. The page does not expose SMTP credentials.

## Deployment configuration

The migration `AddGoAmlReminderDeliveries` adds the delivery ledger. It does not replace the database, backfill completed checks, remove screenshots or send email by itself. Use the normal reviewed deployment/migration process. The fresh-database schema also includes the new table.

Published defaults enable reminders in the Production environment, using Gmail STARTTLS and Andries's Kanaan sender/recipient. Sending remains blocked until a private SMTP password is supplied. Development additionally requires `LocalEnabled=true`; Testing, Staging and unknown environments cannot send daily reminders. The environment is the ASP.NET Core host environment, not the browser URL or the drive letter. `Restart-KCAS.ps1` explicitly starts Development. Production uses the live link; Development uses `LocalPublicBaseUrl`.

Keep local sending off except during deliberate tests to avoid duplicate emails from separate databases. Configure secrets and overrides in private configuration or environment variables, not tracked source files:

| Key | Setting |
|---|---|
| `GoAmlReminders:Enabled` | Master switch; published default `true`. Private settings can disable it. |
| `GoAmlReminders:LocalEnabled` | Additional Development-only switch; default `false` |
| `GoAmlReminders:LocalPublicBaseUrl` | Development email link base; default `https://kcas.test:8443` |
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

### Private password entry and switches

From PowerShell in the local repository, run:

```powershell
& .\deploy\windows\Set-KCAS-GoAmlEmail.ps1 -Target Local -Sending Enabled -SetPassword
```

Paste the Google app password only into the hidden prompt, never chat or a command argument. This updates the existing ignored `src/KCAS.Admin/appsettings.Development.json`, preserving other settings and file permissions. Restart local KCAS afterward. To disable local sending without re-entering the password:

```powershell
& .\deploy\windows\Set-KCAS-GoAmlEmail.ps1 -Target Local -Sending Disabled
```

On live, after publishing the code, run the equivalent command in the server's repository:

```powershell
& 'D:\Deploy\KCAS\repo\deploy\windows\Set-KCAS-GoAmlEmail.ps1' -Target Live -Sending Enabled -SetPassword
```

The live command updates the existing shared and active `appsettings.Production.json` files, preserving unrelated settings. Restart the existing KCAS task afterward. Future deployments copy shared configuration forward. Both files must already exist; this is not a deployment/install command. Existing environment-variable overrides still take precedence.

The password is stored in private JSON, like the existing database credentials, not encrypted. Restrict NTFS access to administrators and the KCAS process identity. The helper preserves those ACLs, but does not repair overly permissive ones. A Google app password must be provisioned separately on each machine; publishing deliberately excludes private settings and never transfers the password. No database migration is required for the environment switches. The setup command sends no email; the normal worker respects today's existing access and delivery records after restart.

## Policy alignment

This replaces the earlier KCAS screenshot-required operating description. When the working RMCP section 15 and supporting goAML procedure are next adopted, describe the automatic timestamped access log and optional outcome/escalation, not mandatory daily screenshots or an automatically verified nil result. Do not change signed submissions or historical evidence retrospectively.
