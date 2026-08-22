# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-08-22-admin-console/spec.md

All routes live in `src/CarTracker.WebApi/Endpoints/AdminEndpoints.cs`, in one
`app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization("AdminRead")`, registered in the
`Program.cs:392-413` block with the other endpoint groups.

**Authorisation applies to the whole group.** A caller without `admin:read` gets 403 on every route below,
including the two writes. An assistant-token bearer gets **401** rather than 403, because the policy names the
`Auth0` scheme only and the JwtBearer handler challenges at the door - the behaviour `AccountEndpoints.cs:9-27`
documents for the account routes, kept identical here for the same reason: widening a scheme purely so a
credential can be told no is a bad trade.

**No registration is ever returned in full.** Every plate on every route below is passed through
`RegistrationMask.Mask` in the domain before it reaches a response record.

## Endpoints

### GET /api/admin/users

**Purpose:** The account list. The screen's main table, and the answer to "who signed up, and did they stay".

**Parameters:** `limit` (int, optional). Clamped to 1..500, default 200, following the clamp on
`AssistantEndpoints.cs:119-134`. Ordered newest first by `CreatedAt`.

**Response:** `200` with `AdminUserListResponse`:

```
{
  "totalAccounts": 14,
  "returned": 14,
  "users": [
    {
      "id": 1,
      "email": "someone@example.com",
      "emailVerified": true,
      "displayName": "Someone",
      "createdAt": "2026-08-20T09:14:22Z",
      "lastSeenAt": "2026-08-22T17:02:00Z",
      "plan": "Pro",
      "planReason": "AdminGranted",
      "planOverride": "Pro",
      "vehicleCount": 2,
      "maskedRegistrations": ["BT** **J", "LR** ***X"],
      "chatTokensToday": 18422,
      "chatTokens30d": 402118,
      "chatTurns30d": 61,
      "documentCount": 7,
      "vehicleLookupsToday": 0,
      "assistantTokenCount": 1,
      "openAnomalyCount": 2
    }
  ]
}
```

`totalAccounts` is the unclamped count and `returned` is the number in the array, so a truncated list says so
rather than reading as the whole population. `planOverride` is null when the account has none, which is what
distinguishes "on Pro because an administrator said so" from "on Pro because the comp list matches" without
the client having to infer it from `planReason`.

**Errors:** `401` no credential or an assistant-token bearer; `403` authenticated without `admin:read`.

### GET /api/admin/users/{id}

**Purpose:** One account in detail, opened from a row in the list.

**Parameters:** `id` (int, route) - the local `users.id`, not the Auth0 subject. The subject is not in the
response at all: it identifies the person at the identity provider and nothing on this screen needs it.

**Response:** `200` with `AdminUserDetail` - every field of the list row, plus:

```
{
  "vehicles": [
    { "maskedRegistration": "BT** **J", "make": "Land Rover", "model": "Freelander",
      "year": 2003, "status": "Active", "isDefault": true, "createdAt": "..." }
  ],
  "chatUsageByDay": [ { "day": "2026-08-22", "inputTokens": 0, "outputTokens": 0,
                        "cacheReadTokens": 0, "cacheWriteTokens": 0, "turns": 0 } ],
  "assistantTokens": [ { "name": "Laptop", "scope": "ReadOnly", "createdAt": "...",
                         "lastUsedAt": "...", "readCount": 91, "writeCount": 0,
                         "revokedAt": null } ],
  "documentCount": 7,
  "documentBytes": 4812233,
  "openAnomaliesByKind": { "MileageNotMonotonic": 1, "FutureDatedEntry": 1 }
}
```

Make, model and year are included and plates are not, which is a deliberate line: the pair identifies a car
for a support conversation without identifying it to the DVLA. `chatUsageByDay` covers the last 30 days and
omits days with no rows rather than zero-filling, because the client is drawing a series and an absent day is
not a zero-token day in any sense the operator cares about.

**No token secret, ever.** `AssistantToken.TokenHash` is not projected. The export endpoint set that precedent
and it holds here.

**Errors:** `404` unknown id; `401`/`403` as above.

### GET /api/admin/usage

**Purpose:** Deployment-wide cost. **The number nothing in the application currently shows anywhere.**

**Parameters:** `days` (int, optional), clamped 1..90, default 30.

**Response:** `200` with `AdminUsageResponse`:

```
{
  "today": { "day": "2026-08-22", "inputTokens": 812004, "outputTokens": 20117,
             "cacheReadTokens": 0, "cacheWriteTokens": 0, "turns": 94,
             "accountsActive": 3 },
  "dailyTokenCeiling": 2000000,
  "perOwnerTokenCeiling": 500000,
  "byDay": [ { "day": "2026-08-21", "totalTokens": 611230, "turns": 71, "accountsActive": 2 } ],
  "topAccounts": [ { "userId": 1, "email": "someone@example.com", "tokens": 402118 } ],
  "vehicleLookupsToday": 4,
  "totals": { "accounts": 14, "vehicles": 19, "documents": 63,
              "documentBytes": 88213004, "assistantTokens": 3 }
}
```

`dailyTokenCeiling` and `perOwnerTokenCeiling` are the resolved figures actually in force, from
`ChatSettings`, so the numerator and its denominator come from one place and cannot disagree. A ceiling of `0`
means the assistant is off deployment-wide; null is not used, because `MetaEndpoints.cs:59-66` already
established that resolving the indirection server-side is the endpoint's job and not the client's.

`topAccounts` is capped at ten and carries the address, which is the point of it.

**Errors:** `401`/`403` as above.

### GET /api/admin/diagnostics

**Purpose:** What the running container actually resolved. The boot posture line, on demand, for the two
incidents recorded in the spec's Overview.

**Parameters:** none.

**Response:** `200` with `AdminDiagnostics`:

```
{
  "version": "0.26.0",
  "environment": "Production",
  "serverTimeUtc": "2026-08-22T17:40:00Z",
  "timeZone": "Europe/London",
  "signup": { "mode": "Open", "allowedEmailCount": 0, "allowedDomainCount": 0,
              "allowlistIsInert": false },
  "plans": { "compEmailCount": 1, "compDomainCount": 0,
             "free": { "chatEnabled": false, "dailyChatTokens": 0, "maxDocuments": 100,
                       "dailyVehicleLookups": 3 },
             "pro":  { "chatEnabled": true, "dailyChatTokens": null, "maxDocuments": 2000,
                       "dailyVehicleLookups": 50 } },
  "chat": { "configured": true, "model": "claude-sonnet-5",
            "dailyTokensPerOwner": 500000, "dailyTokensGlobal": 2000000 },
  "lookup": { "vesConfigured": false, "motConfigured": false },
  "identity": { "managementConfigured": true, "pendingIdentityDeletions": 0 },
  "ownership": { "claimUnownedVehiclesForConfigured": false },
  "documents": { "rootPath": "/documents", "exists": true, "writable": true },
  "database": { "lastAppliedMigration": "20260822..._AddAdminObservability",
                "pendingMigrationCount": 0 }
}
```

#### Why this carries no secret, and what "no secret" means here

Every credential is reduced to a boolean: `chat.configured`, `lookup.vesConfigured`,
`identity.managementConfigured`. No key, no client secret, no connection string, and no token appears on this
response in any form. The comp and allowlist entries are reported as **counts, not addresses** - the operator
can already see which accounts resolved to which plan on the user list, with the reason attached, which
answers "is my address comped" better than printing the list would.

`allowlistIsInert` is the same condition the boot warning added in `0.24.1` reports: sign-up is `Open` and
`Signup:AllowedEmails` is populated, so a list is being read for nothing. It is the shape every pre-`0.24.0`
deployment arrives in.

`database.lastAppliedMigration` is the fastest available answer to "is this container running the code I think
it is", which is the question behind both recorded incidents.

**Errors:** `401`/`403` as above.

### PUT /api/admin/users/{id}/plan

**Purpose:** Put an account on a tier without a configuration edit and a container recreate.

**Authorisation:** additionally `.RequireAuthorization("AdminPlanWrite")`. Group and route policies combine
with AND, so this needs `admin:read` **and** `admin:plan:write`.

**Parameters:** `id` (int, route). Body:

```
{ "plan": "Pro" }
```

**Response:** `200` with the updated `AdminUserRow`, so the client refreshes one row rather than re-fetching
the list.

#### Why an override of Free is accepted

`plan` accepts `Free` as well as `Pro`, and that is not an oversight. The override is a nullable plan rather
than a boolean precisely so an account can be pinned *below* a comp list it would otherwise match - a domain
comp entry that has caught somebody it should not, most obviously. Clearing the override and adding an
exception to a config list are not the same operation and only one of them can be done without a restart.

**Errors:** `400` an unparseable plan, as an RFC 9457 validation problem with the field named, so the sheet
can render it inline through the existing `reportApiError`; `404` unknown id; `401`/`403` as above.

### DELETE /api/admin/users/{id}/plan

**Purpose:** Clear the override so the account falls back to the comp list.

**Authorisation:** as `PUT`.

**Response:** `200` with the updated `AdminUserRow`. Not `204`: the caller wants to see what the account
resolved to *after* the override was removed, and a second request to find out would be a race with nothing.

**Errors:** `404` unknown id, **including when the account exists but holds no override** - the resource being
deleted is the override, and reporting success for a deletion that deleted nothing is how a UI comes to show a
state the server does not hold. `401`/`403` as above.

## What the contract gains

Additive throughout. No existing path changes, no existing field is removed, renamed or retyped.

**New paths (6):** `/api/admin/users`, `/api/admin/users/{id}`, `/api/admin/usage`, `/api/admin/diagnostics`,
and `PUT`/`DELETE /api/admin/users/{id}/plan`.

**New schemas:** `AdminUserRow`, `AdminUserListResponse`, `AdminUserDetail`, `AdminVehicleRow`,
`AdminChatUsageDay`, `AdminAssistantTokenRow`, `AdminUsageResponse`, `AdminUsageTotals`,
`AdminTopAccount`, `AdminDiagnostics` and its nested blocks, `SetPlanOverrideRequest`, `AdminCapabilities`.

**Changed schemas (2), both additive:**

- `AuthenticatedResponse` gains `admin` of type `AdminCapabilities`, **required and non-nullable**. The
  remarks on that record at `MetaEndpoints.cs:114-120` record why: a defaulted record parameter emits as
  nullable, the generated client gets `AdminCapabilities | null`, and every consumer handles a null the
  endpoint cannot return. That broke CI a commit before `0.24.1`.
- `PlanReason` gains the member `AdminGranted`. Additive to the enum; existing members keep their names and
  order. `PlanPanel.tsx:22` types its copy as `Record<PlanReason, string>` off the generated union, so the
  front end will not compile until a sentence is written for it, which is that guard doing its job.

## Configuration added

**None.** The gate is Auth0 tenant state, so `deploy/.env.example`, `deploy/docker-compose.yml` and
`src/CarTracker.WebApi/appsettings.json` are untouched. What is required instead is four steps in the Auth0
dashboard, documented in the README and in the technical spec: enable RBAC **and** "Add Permissions in the
Access Token" on `cartracker.api`, define `admin:read` and `admin:plan:write`, assign them through a role, and
sign out and back in so a token is issued that carries them.
