# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-08-11-pre-public-release-gates/spec.md

Three new endpoints in one new group, `AccountEndpoints`, registered in `Program.cs` beside the others and
mapped at `/api/account`. The group is **not** vehicle-scoped: it is about the signed-in person, not a car.

**None of the three gets an MCP tool.** An assistant holding a read-write token must not be able to delete an
account or dump it; the blast radius of a leaked token stays where DEC-014 put it. `AccountEndpoints` is
therefore the first endpoint group deliberately reachable only by the Auth0 scheme — an assistant-token
principal is authenticated but must be refused, so the handlers require an Auth0 `sub`, not merely a resolved
owner.

## GET /api/account/summary

**Purpose:** The counts the deletion confirmation states before it will arm. Deleting is irreversible, and a
screen that says "this will delete everything" without saying how much everything is asks for consent it has
not informed.

**Parameters:** None.

**Response:** `200 OK`, `application/json`

```json
{
  "email": "someone@example.com",
  "createdAt": "2026-07-24T00:25:36Z",
  "vehicleCount": 1,
  "logEntryCount": 214,
  "documentCount": 6,
  "documentBytes": 4718592,
  "assistantTokenCount": 2
}
```

`logEntryCount` is the sum across the log tables — one number, because the screen is establishing weight, not
producing an inventory. The inventory is the export.

**Errors:** `401` when unauthenticated.

## GET /api/account/export

**Purpose:** UK GDPR Art. 15 and Art. 20 in one response. Everything the account owns, as raw rows.

**Parameters:** None.

**Response:** `200 OK`, `application/json`, `Content-Disposition: attachment` with a filename carrying the
export date.

```json
{
  "exportedAt": "2026-08-11T09:00:00Z",
  "schemaVersion": "0.12.0",
  "notes": [
    "Every figure this app displays is derived from these rows at read time and is not stored, so no computed value appears here.",
    "Document files are not included. Their metadata is; download the files individually from the documents screen."
  ],
  "account": { "email": "…", "displayName": "…", "createdAt": "…" },
  "reference": { "garages": [], "washLocations": [], "expenseCategories": [] },
  "vehicles": [
    {
      "registration": "BT53 AKJ",
      "profile": { },
      "mileageReadings": [], "fuelEntries": [], "expenses": [], "serviceRecords": [],
      "tyreReadings": [], "washEntries": [], "checkDefinitions": [], "checkLogs": [],
      "tasks": [], "issues": [], "equipment": [], "budgetGroups": [],
      "documents": [], "anomalies": []
    }
  ],
  "assistantTokens": [{ "name": "…", "scope": "read", "createdAt": "…", "lastUsedAt": "…" }]
}
```

**Raw rows only.** Nothing from `IDerivedMetricsService` appears — no MPG, no cost-per-mile, no check status,
no totals. Those are recomputable from what is here by definition, and an export containing stored derived
figures would be the exact defect the five workbook figures document. The first entry in `notes` says so in
the payload, because the person reading the file is entitled to know why it does not contain the numbers they
see on screen.

**Token secrets are never included** — name, scope and timestamps only. The secret was shown once at
creation and the database holds a hash.

**Errors:** `401` when unauthenticated.

## DELETE /api/account

**Purpose:** UK GDPR Art. 17. Destroys the account and everything it owns.

**Request body:** `application/json`

```json
{ "confirmEmail": "someone@example.com" }
```

The body is required and must match the signed-in user's email exactly (ordinal, case-insensitive). This is a
second gate behind the UI's typed confirmation — an endpoint that deletes an account on an empty `DELETE` is
one mis-wired button away from a catastrophe, and the client is not the only possible caller.

**Response:** `204 No Content`.

**Errors:**

| Status | When | Why distinct |
|---|---|---|
| `400` | `confirmEmail` missing or not matching | Per-field RFC 9457 `errors` map, so the sheet marks the field rather than showing a banner |
| `401` | Unauthenticated | |
| `403` | The principal is an assistant token, not an Auth0 session | An assistant must not be able to do this at all |
| `503` | `Auth0:Management:` is not configured | **Deletes nothing.** See below |

### 503 rather than a partial deletion

If the Auth0 Management credentials are absent, the endpoint refuses before touching anything and says why.
This follows the `Lookup:` precedent exactly (`503 NotConfigured`, distinct from a `502` that would invite a
retry that cannot succeed): a deployment that has not provisioned the credential would otherwise delete all
the local data and leave the identity behind, which is the worst of both outcomes and is silent.

`GET /api/meta` gains a flag so the client can hide the deletion control on a deployment where it cannot work,
rather than offering a button that 503s.

### What happens after the 204

The response returns once the local data is gone and the identity deletion has either succeeded or been
recorded in `pending_identity_deletions` for retry. The client then calls Auth0 `logout()`. The 204 is not a
promise that the identity is already gone — it is a promise that the data is, and that the identity's removal
is now guaranteed to be attempted until it succeeds.

## Changed behaviour on existing endpoints

No route or shape changes. Two behavioural corrections fall out of per-owner reference lists:

- `GET /api/reference/garages`, `/wash-locations`, `/expense-categories` return only the signed-in user's rows,
  and their `referenceCount` counts only that user's records. Today the count aggregates across every account,
  which is a quiet cross-tenant leak on a screen nobody would think to check.
- `PATCH`/`DELETE` on those routes affect only the caller's rows. A name that exists for another user is, and
  reports as, `404` — the same not-found-rather-than-forbidden posture the vehicle query filter already takes.

## Configuration added

| Key | Purpose | Absent means |
|---|---|---|
| `Auth0:Management:ClientId` / `ClientSecret` / `Audience` | M2M application for identity deletion | `DELETE /api/account` answers 503 |
| `Signup:AllowedEmails` | Allowlisted addresses | **Closed** — nobody new is provisioned |
| `Signup:AllowedDomains` | Allowlisted email domains | As above |
| `Ownership:ClaimUnownedVehiclesFor` | The one external id permitted to adopt pre-multi-user vehicles | No adoption ever (DEC-016 retired) |

An empty allowlist meaning *closed* rather than *open* is the fail-safe direction and must be stated in
`.env.example` and the README Quickstart, because the opposite reading is the natural one and would silently
open registration on a deployment that forgot to set it.
