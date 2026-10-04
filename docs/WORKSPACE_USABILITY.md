# Workspace usability

## Implemented

- Home focuses on existing review notifications, Codex requests and follow-up, without development/foundation text. Client metrics and shortcuts follow existing permissions.
- Main navigation groups Clients, Investments, Advice, Compliance and Administration. Each item uses its destination's existing authorisation policy; empty groups are omitted.
- Only the specific destination is highlighted. Menu headings expand without navigating; the current workspace opens automatically. Account/sign-out are separate.
- Transfers are grouped under Administration. Moving an entry does not grant permission: employee transfer and security permissions remain distinct from Administrator-only transfers/imports.
- Programme and regulatory evidence retains the FSCA demonstration order, with direct links to methodology, BRA, RMCP, governance, references, evidence, client compliance, monitoring, audit and inspection response.
- Monitoring and remediation points to the actual work register. Task configuration remains separately accessible and clearly titled. goAML check remains linked from Compliance overview, not a main-menu entry.
- Shared client navigation covers overview, investments, compliance, advice and notes. Related pages include acceptance, screening/evidence, risk reports, ownership for entities, investment verification and individual/family summaries, subject to existing permissions.
- Main client, review, evidence, acceptance, risk, ownership, advice and return screens use this navigation. Next-step actions remain prominent. Secondary advice actions use a dismissible dropdown; approved/draft PDF remains the primary action.
- Administrator bulk client-visibility controls are collapsed initially; existing preview/confirmation behavior is retained.

## Boundaries

This pass changes navigation and presentation, not acceptance gates, assessment decisions, advice approval/issue, stored evidence or client records. Existing URLs remain valid; no database migration is needed.

## Subsequent Pass

Improve individual list filters and smaller edit forms, including consistent labels, empty/loading states, dropdowns and error handling. Do not collapse genuinely different workflows simply because they have similar labels.

## Client Lists and Overview

The second usability pass adds consistent search and reset controls to Clients,
Client reviews and Evidence readiness. Lifecycle and feature-specific filters
remain distinct: review completion is not the same as evidence readiness.
Filters, sort order and page are stored in the URL, not in client records.
Lists use 50-row pages; Clients explicitly reports when its 500-match cap is
reached. Searching is debounced and paging cached results does not requery the
database. Current value descending remains the default on Clients and reviews.

Opening a client from a list carries a validated local return path. Shared client
workspace links retain this context and offer Back to results. The return target
is restricted to the three client-list routes and respects their access policy.
Individual workflow buttons outside the shared navigation may still use their
own existing return destination.

The client overview now shows investment value, recorded assessment state,
latest advice status and the next compliance-review action. These are summaries
of existing records, not new compliance decisions. A finalised assessment does
not claim that acceptance, evidence currency or every other workflow is complete.
Advice and investment summaries respect their existing view permissions.
The most recent advice includes cases in which the client is a participant;
its link points to the case owner's client route.

Evidence readiness now shows Kanaan ID and puts folder changes behind an
expandable control. No scan is run and linked evidence is not changed by filtering.
Loading failures offer a retry rather than an apparent empty list.

Verification: focused return-path and authenticated rendering tests (including
operational-review pages), existing client-search tests, and application build. Browser visual
verification remains unavailable in this session because the browser connection
fails before opening; responsive layout is not claimed visually verified.
