# KCAS Codex Working Protocol

These repository-specific checks are performed by Codex. The user should not need to remember or run them manually.

## Read-only database inventory

- Run `deploy/windows/Report-KCAS-DatabaseInventory.ps1` before finishing any task that creates, restores, stages, rehearses, recovers, migrates, or otherwise materially affects a MySQL database.
- Run the report before any requested commit, push, pull request creation/update, or release preparation.
- If the report identifies `Review` or unexpected `Monitor` items, summarize them to the user before handoff or GitHub mutation.
- The inventory check is read-only. Never delete a database merely because the report flags it. Database deletion always requires explicit user authorization and exact target verification.
- When a database-affecting task creates a temporary schema, run the inventory both before and after the task so new buildup is visible.

## Git and GitHub identity

- Before any requested commit, push, pull request creation/update, release, or other GitHub mutation, inspect `git remote -v`, `git config user.name`, `git config user.email`, `gh auth status`, and `gh api user --jq .login`.
- The active GitHub account must match the owner of the `origin` repository unless the user explicitly directs otherwise.
- If the active account does not match, stop before committing or mutating GitHub and report the mismatch. Do not silently use or switch to another account.
- Re-run the identity check immediately before the first GitHub mutation if substantial work or authentication changes occurred after the initial check.

## Sandbox and command-failure handling

- A non-zero command exit is not, by itself, evidence of a sandbox failure. Inspect the command output and correct syntax, quoting, parameters, credentials, paths, validation rules, or script defects inside the sandbox first.
- Never request or accept an unsandboxed retry merely because the client offers “command failed; retry without sandbox?” as a generic fallback.
- Request sandbox escalation only when the error specifically demonstrates a filesystem permission, process, GUI, or network restriction and the command is essential to the user's task.
- Before requesting escalation, exhaust safe sandbox-compatible alternatives and continue any independent work that does not require approval.
- Routine KCAS checks—including `Report-KCAS-DatabaseInventory.ps1`, builds, tests, Git inspection, and local MySQL read-only queries—must use their tested sandbox-compatible forms and should not require user interaction.
- If genuine escalation is unavoidable, explain the exact restricted operation and request it once with the narrowest practical scope.
