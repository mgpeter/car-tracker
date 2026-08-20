# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-08-19-account-data-import/spec.md

Two endpoints, in `AccountImportEndpoints.cs` beside `AccountExportEndpoints.cs`, under the same
`/api/account` group and the same authorization: the Auth0 fallback policy, so an `AssistantToken` bearer is
refused at the door with **401** rather than admitted in order to be told 403. That is the precedent
`api-spec.md` set for `DELETE /api/account` and the reasoning holds unchanged - widening a scheme so it can be
refused politely is a bad trade.

**Every refusal lives in the service, not the endpoint.** There is no `CarTracker.WebApi.Tests` project, so an
import's validation rules are asserted as Data tests against a real database; the endpoint maps an outcome to
a status code and does nothing else. This is the rule task 4 of the pre-public-release spec established for
account deletion, for the same reason.

---

## Endpoints

### POST /api/account/import/preview

**Purpose:** Parse and validate an export file, report exactly what importing it would do, and write nothing.

**Request:** `multipart/form-data`, one `file` part. JSON only. **25 MB cap enforced while reading**, not from
`Content-Length`, following `DocumentEndpoints`.

**Response:** `200 OK`

```jsonc
{
  "importId": "opaque, server-held, owner-keyed, expires in 15 minutes",
  "source": {
    "exportedAt": "2026-08-14T19:02:11Z",
    "schemaVersion": "0.13.2",
    "email": "someone@example.test",     // provenance only; written nowhere
    "displayName": null,
    "newerThanThisApp": false            // true when the file was written by a later VERSION
  },
  "reference": {
    "garages":           { "inFile": 3, "willCreate": 1, "alreadyYours": 2 },
    "washLocations":     { "inFile": 2, "willCreate": 0, "alreadyYours": 2 },
    "expenseCategories": { "inFile": 13, "willCreate": 0, "alreadyYours": 13 }
  },
  "vehicles": [
    {
      "index": 0,
      "registration": "BT53 AKJ",
      "description": "2003 Land Rover Freelander 1",
      "collides": true,
      "proposedRegistration": "BT53 AKJ-2",
      "rows": {
        "mileageReadings": 14, "fuelEntries": 13, "expenses": 15, "serviceRecords": 1,
        "tyreReadings": 0, "washEntries": 0, "checkDefinitions": 18, "checkLogs": 4,
        "tasks": 2, "issues": 1, "issueWatchChecks": 2, "equipment": 19, "budgetGroups": 5
      },
      "skipped": { "documents": 14, "anomalies": 3 }
    }
  ],
  "warnings": [
    "3 of 3 vehicles already exist in your garage and will be imported as copies.",
    "14 document records name files this export does not contain, and will not be imported.",
    "3 data-integrity flags will not be imported. They are worked out again from the rows once they land."
  ]
}
```

**Errors:**

| Status | When | Body |
|---|---|---|
| 400 `import-unreadable` | Not JSON, truncated, or not an export of this app | RFC 9457, `detail` naming what failed to parse |
| 400 `import-invalid` | Structurally readable, semantically impossible: a required field absent, an expense naming a `fuelEntryId` the file does not contain, two rows flagged `isVehiclePurchase` on one vehicle, a watch link crossing vehicles | RFC 9457 with a per-item `errors` map keyed `vehicles[0].expenses[7].fuelEntryId`, which `lib/formErrors.ts` already folds into a footer banner when it cannot match a field |
| 413 | Over 25 MB | RFC 9457 |
| 401 | Assistant-token principal, or unauthenticated | - |

**Nothing is written on any path through this endpoint**, including the successful one.

---

### POST /api/account/import/{importId}/commit

**Purpose:** Write the previewed import.

**Request:**

```jsonc
{
  "vehicles": [
    { "index": 0, "include": true, "registration": "BT53 AKJ-2" }
  ]
}
```

**The request carries no payload**, only decisions about the one the server is already holding. This is
`PendingWriteStore`'s rule from the chat: an earlier revision of that spec matched a client-supplied id against
a block in the client-supplied transcript, which validated the request against itself. Re-sending the file here
would be the same mistake, and would let a commit write something the preview never described.

- `index` refers to the previewed vehicle list. An index the preview did not contain is a 400.
- `include` defaults to true when a vehicle is not mentioned, so an empty `vehicles` array imports everything
  as previewed. Omitting a vehicle is not the same as excluding it.
- `registration` overrides the proposal. Absent means the proposal stands.

**Response:** `200 OK`

```jsonc
{
  "vehicles": [
    { "registration": "BT53 AKJ-2", "importedFrom": "BT53 AKJ", "rows": 102, "anomaliesRaised": 1 }
  ],
  "reference": { "garagesCreated": 1, "washLocationsCreated": 0, "expenseCategoriesCreated": 0 },
  "skipped": { "documents": 14, "anomalies": 3, "assistantTokens": 2, "auditEntries": 47 },
  "totalRows": 102
}
```

**Errors:**

| Status | When | Body |
|---|---|---|
| 404 `import-not-found` | Unknown, expired, or another owner's `importId` | RFC 9457. **A foreign id answers exactly as an expired one does** - telling them apart would confirm the id is real |
| 409 `import-collision` | An override registration collides, including one that became taken between preview and commit | RFC 9457 naming the registration |
| 400 `import-invalid` | An override is not a valid registration, or an index is unknown | RFC 9457 with the `errors` map |
| 401 | Assistant-token principal, or unauthenticated | - |

**One transaction**, inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`. Either the whole import
lands or none of it does. The `importId` is consumed on success and remains valid after a failure, so a
correctable refusal - a collision on an override - does not cost a re-upload.

---

## What the contract gains

Additive only: two paths and their request and response shapes. No existing endpoint changes, no enum gains a
member (`EntrySource.Import` already exists), and `GET /api/account/export` is untouched - the format is
already what it needs to be, which is the finding that made this spec small.

`api-contract/v1.json` and the generated TypeScript regenerate as usual, and the CI staleness gate must pass
in the same commit.
