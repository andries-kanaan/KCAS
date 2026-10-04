# KCAS Codex Working Protocol

These repository-specific checks are performed by Codex. The user should not need to remember or run them manually.

## Efficient commit and PR handoff

- Treat a routine commit/push/PR request as packaging already completed work, not as a fresh implementation or full code-review task. Inspect the actual diff for unexpected changes, sensitive client data, secrets, conflict markers and migration/deployment completeness; investigate anything unexpected before including it.
- Reuse existing, inspected repository helpers and tested command forms when suitable. Check their target repository, branch, paths and behavior before use; do not recreate helpers for every request or blindly reuse hard-coded values from an earlier PR.
- Complete any necessary verification first, then perform one fresh database inventory and the required identity checks close to the mutation sequence. Commit, push, create/update the PR and verify its repository/base/head without repeating unchanged checks between each operation.
- If the previous PR was merged, fetch the current base and prepare the next branch while preserving all working changes. Remove only local branches confirmed merged; never reset, revert or discard user work as cleanup.
- Report the commit, branch, PR link, local verification performed or reused, and unverified CI status promptly. Do not run additional tests, start another review pass or poll CI merely to prolong this handoff.

## Read-only database inventory

- Run `deploy/windows/Report-KCAS-DatabaseInventory.ps1` before finishing any task that creates, restores, stages, rehearses, recovers, migrates, or otherwise materially affects a MySQL database.
- Run the report before any requested commit, push, pull request creation/update, or release preparation.
- One fresh report can cover the related commit/push/PR sequence for the same request when no database-affecting work or known database-state change intervenes. Repeat it after such a change; do not reuse an inventory from an earlier request.
- If the report identifies `Review` or unexpected `Monitor` items, summarize them to the user before handoff or GitHub mutation.
- The inventory check is read-only. Never delete a database merely because the report flags it. Database deletion always requires explicit user authorization and exact target verification.
- When a database-affecting task creates a temporary schema, run the inventory both before and after the task so new buildup is visible.

## Git and GitHub identity

- Before any requested commit, push, pull request creation/update, release, or other GitHub mutation, inspect `git remote -v`, `git config user.name`, `git config user.email`, `gh auth status`, and `gh api user --jq .login`.
- Stop before pushing if the active GitHub user lacks access or is clearly the wrong account.
- Check the configured accounts with `gh auth status`.
- Before the first push in this repository, explicitly select the repository-owning account with `gh auth switch -u andries-kanaan`.
- Confirm the selected account and repository with `gh auth status`, `gh api user --jq .login`, and `git remote -v`.
- Prefer the explicit keyring verification sequence below before concluding that GitHub authentication is broken:
  1. `gh auth status --hostname github.com`
  2. `gh auth switch --hostname github.com --user andries-kanaan`
  3. `gh auth status --hostname github.com`
  4. `gh api user --jq .login`
  5. `git remote -v`
- If an earlier combined command reports HTTP 401 while `andries-kanaan` is shown as the active keyring account, do not immediately report an authentication blocker. Re-run the explicit sequence as separate checks and inspect whether `GH_TOKEN` or `GITHUB_TOKEN` is present without printing either token. Proceed when the status check is healthy and `gh api user --jq .login` returns `andries-kanaan`.
- Push the branch only after `andries-kanaan` is confirmed as active.
- Create or update the pull request using the same confirmed account.
- Verify the pull request's repository, base branch, and head branch after creation or update.
- If the account cannot be switched or authenticated, report the authentication blocker instead of repeatedly pushing with the wrong credentials.
- Re-run the identity check immediately before the first GitHub mutation if substantial work or authentication changes occurred after the initial check.
- Reuse that confirmed identity within the uninterrupted mutation sequence only while the account, credentials and remote remain unchanged. Recheck after an authentication error, account/credential/remote change, interruption or substantial intervening work; do not repeat a healthy check solely because the next operation is push or PR creation.

## Sandbox and command-failure handling

- A non-zero command exit is not, by itself, evidence of a sandbox failure. Inspect the command output and correct syntax, quoting, parameters, credentials, paths, validation rules, or script defects inside the sandbox first.
- Never request or accept an unsandboxed retry merely because the client offers “command failed; retry without sandbox?” as a generic fallback.
- Request sandbox escalation only when the error specifically demonstrates a filesystem permission, process, GUI, or network restriction and the command is essential to the user's task.
- Before requesting escalation, exhaust safe sandbox-compatible alternatives and continue any independent work that does not require approval.
- Routine KCAS checks—including `Report-KCAS-DatabaseInventory.ps1`, builds, tests, Git inspection, and local MySQL read-only queries—must use their tested sandbox-compatible forms and should not require user interaction.
- If genuine escalation is unavoidable, explain the exact restricted operation and request it once with the narrowest practical scope.

## Cross-platform tests and PR checks

- During local iteration, run the affected test class or feature area first; do not run the entire suite after every small edit. Broaden coverage when a change touches shared contracts, transfers, database migrations, or multiple modules, and leave the full-suite run to GitHub PR checks without waiting for it before handoff.
- Reuse successful local verification from the current work only when its tested source/diff and coverage are known, and the relevant application code, tests, dependencies, build settings, migrations and environment/database prerequisites have not changed since the run. Do not infer a pass from memory or run `--no-build` against stale binaries.
- When relevant inputs change, rebuild as needed and rerun the affected tests, broadening coverage for shared changes. Newly included changes not covered by the earlier run require appropriate verification. Git staging/committing alone does not invalidate test results; documentation-only changes need document/diff checks unless they affect executable or generated behavior.
- Do not delete passing regression tests merely because their feature is old. Consolidate only tests that duplicate the same assertion or fixture setup after checking what distinct failure each test catches.

- GitHub PR checks run on Ubuntu while KCAS development and deployment are primarily Windows-based. Treat every filesystem, drive-mapping, evidence-path, and transfer test as cross-platform.
- Keep Windows-path parsing separate from native filesystem operations. Do not assume `Path.GetFullPath`, `Path.Combine`, directory creation, or absolute-path detection will treat `C:`, `E:`, `Z:`, backslashes, and temporary paths identically on Windows and Linux.
- In tests, assert against KCAS path-mapping and file-resolution contracts rather than hard-coded host-specific normalized paths. When a test needs a mapped folder to exist, create the exact mapped target returned by KCAS.
- Use unique test records and assert on the records created by the test. Avoid exact global counts when shared seeded or collection-scoped database state can legitimately add records.
- When testing transfers, explicitly model source and live as separate environments. Remove or replace source-only assessments, evidence, configuration, and reconciliation state before previewing the live import.
- After any CI-only failure, inspect the entire affected test and related assertions for the same platform or isolation assumption; do not patch only the first failing line.
- After creating or updating a pull request, report the commit, branch and PR link without waiting for or repeatedly polling GitHub checks, unless the user explicitly requests CI verification. State that CI has not been verified; do not claim the checks passed or the PR is ready to merge.
- The user monitors GitHub checks and reports failures. When a failure is reported, read the failed-step logs, fix the cause on the same PR, run appropriate local verification and push the correction. Leave replacement CI monitoring to the user unless explicitly requested.
