# User Action Bell

The signed-in header shows a bell with the number of outstanding actions for the
current account. Its dropdown contains short subject/action links to the actual
case or approval section. There is no intermediate actions page or read/unread
state. Opening the bell or following a link does not complete work.

The feed reads existing workflow state and current approved-account permissions:

- Client Codex handoffs: recorded Compliance recipients who can perform preparation.
  The request remains outstanding until results are validated, even if its generic
  task was closed separately.
- Codex advice preparation: eligible Compliance users with access to the case and
  all participants. Supported result recording closes the preparation task; adviser
  confirmation and independent approval remain separate actions.
- Advice review: accounts with Advice.Review, excluding the original preparer and
  Codex-results recorder. Returned advice goes back to its preparer.
- Client acceptance: current governance-register KIs with client-management and
  risk-view permission, when current supported results are ready for a new decision.
  This is not duplicated as a separate enhanced-risk approval.
- Standalone pending client-risk approval, methodology approval, BRA/RMCP approval,
  and task-closure approval: their existing permissions and recorded approval rules
  apply. A completed personal approval disappears even while a second approval is
  outstanding for another user.
- Assigned compliance work, including existing annual/trigger-review tasks, sanctions
  coverage and complaints, and recorded employee-review recipients. Closed/withdrawn
  work is omitted. Employee acknowledgements are not completed reviews.

Restricted clients and advice participants are hidden from non-administrators.
Repeated entries for the same action are combined, and overdue work is listed first.
The feed refreshes on opening, navigation, authentication changes and every minute.
Loading failure is displayed as unavailable, not as a false zero.

Existing Home sections, registers, task generation and business decision controls
are unchanged. This is in-app only: no email, automatic Codex launch, migration or
new notification table is introduced.
