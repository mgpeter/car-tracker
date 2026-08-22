# Spec Summary (Lite)

An operator surface at `/admin`, gated on two Auth0 API permissions, for a deployment that has just opened
sign-up to strangers and can see none of them. Four read endpoints - accounts, one account, deployment usage,
configuration posture - plus exactly one write, a per-account plan override. Two nullable columns on `users`,
one migration, no new configuration key.

**The gate is an Auth0 `permissions` claim, and DEC-022 said no to exactly that, so this spec has to say why
it is different.** That decision rejected `permissions: ["chat:use"]` because a JWT-carried entitlement is a
copy of a fact owned elsewhere, free to go stale in both directions, on the one surface where being wrong
costs money. An operator permission is not that fact: it is tenant state that is genuinely *about* the tenant,
it changes when a human decides it does, and a revoked administrator keeping access until their token rotates
costs nothing and is recoverable. The other half of DEC-022's objection - that nothing in this repository can
assert, test or restore an Auth0 role - is conceded rather than answered, and the mitigations are that the
predicate is a domain type with real tests and that the surface it opens is read-only and plate-masked.

**`admin:read` and `admin:plan:write`, and the refusal of a generic `admin:write` is the load-bearing part.**
Account deletion and token revocation are coming. A permission meaning "any admin mutation" would confer them
on whoever already holds it, so the destructive capability would land already granted and nobody would
re-decide. One permission per capability means the next dangerous thing needs a new, deliberate assignment.

**The plan override is a stored input, not a stored derived value, and that distinction is the whole
argument.** DEC-022's consequences say granting the paid tier is a config key and a restart, with no admin UI
and no per-account override. This reverses that clause and nothing else: `users.plan_override` is the same
kind of thing `Plans:CompEmails` already is, just editable without a container recreate. The resolved plan is
still computed on every request from the override, the comp list and a verified address, so DEC-002 is
untouched. `PlanReason` gains `AdminGranted`, because an account told it is on Pro deserves to know why, and
`PlanPanel`'s `Record<PlanReason, string>` will refuse to compile until somebody writes that sentence.

**Two callers now need the plan ladder, so it stops being a private method.** `AccountEntitlements` reads
`ICurrentUserAccessor` and answers for one account; the admin list answers for all of them. A second copy of
that ordering is a second chance to get it wrong, so the decision is extracted into a pure
`PlanResolver.Resolve`. One consequence is recorded rather than hidden: entitlement can no longer short-circuit
on an empty comp list before touching the database, because an override might exist.

**`IgnoreQueryFilters()` appears in exactly one file.** An admin request is provisioned like any other, so the
vehicle ownership filter is live and pinned to the administrator's own owner id. `AdminReadService` is the one
place allowed to widen it, so the widening is one reviewable file rather than a habit spreading through
handlers - and deliberately not `BypassOwnership`, which is a request-wide hammer that would silently widen
code with no idea it was running under an administrator. A Data test with two seeded owners is the only thing
that proves the widening is on every query.

**Registrations are masked in the domain, and the spec says plainly that this is data minimisation rather
than a security control.** The operator has database access anyway. What masking buys is a screen that can be
opened, screenshotted and shared without spreading other people's plates, and friction against idle
curiosity; an address plus `BT** **J` is still enough to answer a support email.

**The diagnostics endpoint is the boot posture line, on demand.** Two incidents argue for it: `0.13.1` reached
the NAS with an empty Auth0 Management credential and refused an invited address, and `0.24.0` reached
`cambelt.app` with no comp list and put every account on the free tier. Both were one line of configuration,
both read as application faults, and both needed shell access to diagnose. It carries booleans and counts and
never a secret.
