# Spec Requirements Document

> Spec: Admin Console - an operator surface for a deployment with real users
> Created: 2026-08-22
> Status: **Complete.** Shipped 2026-08-22 in `3492ec5` (`0.27.0`), with the browser pass and its one
> correction following the same day in `26e26d3` (`0.27.1`) - the plan write, the only write on this surface,
> was the single JSON write in the app that never declared `Content-Type`, and a minimal API refuses an
> inferred body parameter **415 before the handler runs**. Verified against `cambelt.app` with both
> permissions assigned: the link is absent without `admin:read`, the list renders with masked plates, and a
> plan override reaches the target account on its next request with no restart.

## Overview

Sign-up opened to the public on 2026-08-22 (`0.24.0`, DEC-022) and the deployment gained no way to see who
walked through the door. There is no operator surface of any kind: no list of accounts, no way to read what
the assistant is costing across the deployment, and no way to ask the running container what configuration it
actually resolved. `0.24.1` exists because of that last gap - `cambelt.app` took `0.24.0` with
`PLANS_COMP_EMAILS` unset, every account resolved to `Free`, the assistant went dark for everybody including
its owner, and the only place that said so was a container log nobody was tailing.

This spec adds `/admin`, gated on Auth0 API permissions assigned to the operator. It is **read-only apart from
one write** - a per-account plan override, which is the thing you reach for within a day of a real user
appearing. It deliberately cannot read any account's fuel log, service history, documents or chat transcripts,
and it never renders a full registration plate.

## User Stories

### Somebody signed up and I do not know who, or whether they stayed

As the operator of a public deployment, I want a list of every account with the date it was created and the
date it was last seen, so that I can tell a stranger who tried the product once from one who came back.

Today the only way to answer this is `psql`. `users` holds `created_at` and nothing else about a session, so
even in the database the second half of the question has no answer: nothing records that somebody signed in,
looked around and wrote nothing, which is most of a first session. The list also has to carry enough context
to be worth opening - how many cars, on which plan and why, and what each account has spent on the assistant -
because an account list with only addresses on it answers the easiest question and none of the others.

### I cannot tell what the assistant is costing me

As the operator paying for model inference, I want today's token spend across every account against the
deployment ceiling, and a thirty-day trend, so that I find out what opening sign-up costs before the invoice
does.

`chat_usage` has held per-account, per-day token counts since `0.14.0` and `ChatBudget` reads them on every
turn to enforce two ceilings. Nothing has ever displayed the deployment-wide figure. The per-account ceiling
is visible to the account holder on their own plan panel; the number that decides whether this deployment is
affordable is visible to nobody.

### The container is not configured the way I think it is

As the operator, I want to ask the running API what it resolved for sign-up mode, comp lists, chat, lookup and
the Auth0 Management credential, so that a posture I believe in is one I have checked rather than one I
assumed.

This is the failure this codebase has recorded twice. `0.13.1` reached the NAS with `Auth0__Management__*`
empty and refused an invited, verified address with "not yet invited"; `0.24.0` reached `cambelt.app` with no
comp list and silently put every account on the free tier. Both were one line of configuration, both looked
like application faults, and in both cases the diagnosis required shell access to a container. A boot log line
was added after the first and did not prevent the second, because a line printed once at start-up is only
useful to somebody already reading logs.

### Putting somebody on the paid tier should not need a restart

As the operator running a beta, I want to grant an account the Pro tier from the admin screen, so that letting
a tester use the assistant is a click rather than an edit to `deploy/.env`, a rebuild of a Container Manager
project and a container recreate.

`Plans:CompEmails` is the only route today, and DEC-022's consequences say so plainly: "Granting the paid tier
is a config key and a restart. No admin UI, and no per-account override." That was right for a deployment with
one account. With testers it is the difference between a two-second answer and a maintenance window.

## Spec Scope

1. **An admin authorisation gate** - two Auth0 API permissions, `admin:read` and `admin:plan:write`, checked
   against the access token's `permissions` claim by a predicate that lives in the domain and is unit-tested
   there, because there is no `CarTracker.WebApi.Tests` project to test a policy in.
2. **A cross-owner read service** - `AdminReadService`, the one file permitted to call `IgnoreQueryFilters()`
   for this surface, projecting per-account rows and deployment aggregates from tables the ownership filter
   does and does not reach.
3. **Four read endpoints** - the account list, one account in detail, deployment-wide usage, and a diagnostics
   endpoint reporting the effective configuration posture with every secret reduced to a boolean.
4. **A per-account plan override** - `users.plan_override`, written by `PUT`/`DELETE /api/admin/users/{id}/plan`
   and read ahead of the comp list by the plan resolver, with `PlanReason` gaining `AdminGranted` so the
   account holder's own screen says why.
5. **A last-seen observation** - `users.last_seen_at`, stamped on the request path but coalesced to fifteen
   minutes, so the list can distinguish an account that came back from one that never did.
6. **The `/admin` screen** - a route-only page reached from the identity menu, showing the deployment band,
   the account table with masked registrations, and the posture list.

## Out of Scope

- **Admin-initiated account deletion, and revoking somebody's assistant tokens.** Both are wanted eventually
  and neither is wanted today, and each will arrive with its own permission (`admin:account:delete`,
  `admin:token:revoke`). That is the entire reason this spec refuses a generic `admin:write`: a permission
  meaning "any admin mutation" silently confers every future admin mutation on whoever already holds it, so
  the destructive one lands already granted and nobody re-decides.
- **Reading any account's records.** Not their fuel log, service history, expenses, documents, anomaly detail
  or chat transcripts. The surface is counts and aggregates by construction, and the moment it grows a row
  view it becomes a tool for reading other people's data rather than for running a deployment. Refusing this
  now is free; refusing it after somebody has asked for it is not.
- **Impersonation, or "view as this account".** The same objection with a worse blast radius, and it would
  require unpinning `ICurrentUserAccessor` from the authenticated subject - the single mechanism every
  ownership guarantee in the application rests on.
- **Per-account allowance overrides beyond the plan.** Setting one account's document ceiling to 500 means
  allowances stop being a property of a plan, and `PlanAllowances` stops being the one answer three surfaces
  share. If the two tiers are wrong, the fix is the tiers.
- **A stored audit trail of admin actions.** The plan write is logged at Information with the acting subject
  and the before and after. With one administrator that is proportionate. A second administrator is the point
  at which a log line stops being an audit trail, and this spec says so rather than leaving it to be noticed.
- **Paging.** The list is unpaged and clamped at 500 accounts. Correct for tens, wrong for hundreds, stated
  here and in the response rather than allowed to read as the whole population.
- **Any change to what an account may spend.** The two tiers, their four allowances and the two chat ceilings
  are exactly as DEC-022 shipped them. This spec changes who can be put on a tier, not what a tier is.

## Expected Deliverable

1. Signed in as an account holding neither permission, `/admin` is not linked from the identity menu and every
   `/api/admin/*` route answers 403 - and the rest of the application is unchanged, with the account's own
   plan resolving exactly as it did before.
2. Signed in as an account holding `admin:read`, the identity menu offers Admin, the screen lists every
   account with masked registrations, per-account token spend and each account's plan and reason, and the
   deployment band shows today's tokens against the global ceiling. No full registration appears in any
   response body.
3. Holding `admin:plan:write` as well, putting a second test account on Pro makes that account's own plan
   panel read Pro with the granted-by-an-administrator reason on its next request, with no restart; clearing
   the override returns it to whatever the comp list says.
