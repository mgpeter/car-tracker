# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-08-06-in-app-chat-assistant/spec.md

Three endpoints in a new `ChatEndpoints` group, mapped under `/api/chat` and behind the standard Auth0
fallback policy - no new scheme, no new policy. They follow the existing groups' shape: registration → id via
`VehicleLookup`, RFC 9457 problem details on failure (which `lib/formErrors.ts` already maps onto fields).

Additive to the committed OpenAPI contract, plus one additive field on the existing `GET /api/meta`.

## Endpoints

### POST /api/chat

**Purpose:** Send a user message (with optional files) and run the conversation until the assistant either
finishes its turn or requests a write.

**Request:**

```jsonc
{
  "vehicle": "BT53AKJ",              // optional; the current route's reg. Pre-bound into tool calls.
  "messages": [                       // the full transcript so far, client-held
    { "role": "user", "content": [ { "type": "text", "text": "..." } ] },
    { "role": "assistant", "content": [ /* echoed back verbatim, reasoning blocks included */ ] }
  ],
  "files": [                          // optional, max 5 TOTAL, attached to the new user message
    { "mediaType": "image/jpeg",     "data": "<base64, no newlines>" },
    { "mediaType": "application/pdf", "data": "<base64, no newlines>" }
  ]
}
```

- `messages` is round-tripped unmodified. Assistant content **must** be echoed back exactly as received -
  reasoning/thinking blocks edited or dropped are rejected upstream, and on `claude-opus-5` they arrive with
  their text **omitted** (an empty string, not a missing block), which is not permission to drop them.
- `files` is one list, not an `images` list plus a `documents` list: the cap is on what the owner attached, and
  splitting it would make "max 5" mean two different things depending on the mix. The server maps each entry to
  an image or document content block by its `mediaType`.
- Accepted `mediaType`: `image/jpeg`, `image/png`, `image/webp`, `application/pdf` - the list in the technical
  spec is authoritative and HEIC is converted in the browser before it gets here.
- Files are never persisted (see the spec's Out of Scope) and never logged.
- **No classification field, in either direction.** The client does not say what a file is and the response
  carries no type verdict; the model's reading arrives as ordinary `text` events before any `pending_write`. A
  structured verdict would be a second thing to keep in step with the sentence the owner actually reads.
- **The transcript is not evidence.** It is client-supplied and the server treats it as untrusted input: it is
  replayed to the model, and nothing in it authorises a write. See `pending_write` below.

**Response:** `200` with `Content-Type: text/event-stream`. Events:

| event | data | meaning |
|---|---|---|
| `text` | `{ "delta": "…" }` | assistant text, incremental |
| `tool` | `{ "name": "get_due_items", "status": "running" \| "done" }` | a **read** tool ran automatically - surfaced so the UI can say what it is doing, not for the user to act on |
| `pending_write` | see below | the loop suspended on a write tool; render the draft card |
| `done` | `{ "messages": [ … ] }` | turn complete; the authoritative transcript to store client-side |
| `error` | `{ "detail": "…" }` | see Errors |

`pending_write` payload:

```jsonc
{
  "pendingWriteId": "pw_01H…",              // opaque, server-held, 10-minute expiry, keyed to this owner
  "tool": "add_service",                    // for display only - /confirm does not accept it
  "title": "Add service record",            // from the tool's [Description]
  "arguments": { "serviceDate": "2026-07-08", "type": "MOT", "mileage": 80705, "garage": "K&P Motors" },
  "schema": { /* the tool's JSON Schema, so the card can label and type every field */ }
}
```

**`pendingWriteId` is the whole authorisation.** The server holds the proposed tool name, arguments, vehicle and
owner id in its own cache under that id; `/confirm` names only the id and the owner's final arguments. An
earlier revision of this spec matched a client-supplied `toolUseId` against a `tool_use` block in the
client-supplied transcript and called that a guard - it validated the request against itself, so a crafted POST
could invent an assistant turn proposing `delete_service` and confirm it. Server-held state is what makes the
check mean something.

**Errors:** `400` on a malformed transcript, an unsupported media type, more than 5 files, or a PDF over the
page cap (say which, and how many pages it had - "too long" without a number is not actionable); `401` when the
Auth0 token is absent or expired; `413` when the body exceeds the configured cap (a phone photo set is the
realistic cause - the message must say so, not surface Kestrel's default); **`429` when the owner's daily token
budget or the deployment's global ceiling is spent, with the reset time in the detail**; `503` when no
`Chat:ApiKey` is configured (the `Lookup:` precedent - permanent until someone provisions a key, so distinct
from a 502 that invites a retry); `502` when the upstream provider fails, with the request id in the detail. A
`stop_reason` of `refusal` is surfaced as an assistant message explaining the request was declined, not as an
error - it is an HTTP 200 upstream and pretending otherwise would lose the explanation.

---

### POST /api/chat/confirm

**Purpose:** Execute a suspended write with the owner's final (possibly edited) arguments, then resume the
conversation.

**Request:**

```jsonc
{
  "messages": [ /* transcript including the assistant turn that requested the write */ ],
  "pendingWriteId": "pw_01H…",
  "arguments": { /* what the owner actually confirmed - may differ from the proposal */ }
}
```

**Behaviour:**

- The server resolves `pendingWriteId` from its own cache. **The tool name is read from the cache, never from
  the request** - there is no `tool` field to send. An id belonging to another owner is a `404`, the same way a
  cross-owner vehicle presents.
- `arguments` are validated against the tool's schema and the tool is invoked in-process, stamping
  `EntrySource.Chat`. Domain validation failures return the same RFC 9457 `errors` map the web writes return, so
  the draft card marks the bad field inline exactly as an add sheet does.
- The real result is sent back to the model as the approval response, and the loop resumes - so the response is
  the same SSE stream as `POST /api/chat`, and may itself suspend again on a second write (a receipt and an
  odometer photo in one message legitimately produce two).
- Every suspension must be answered, by confirm or by decline. An unanswered approval request breaks the
  transcript for every later turn.

**Response:** `200`, `text/event-stream`, same event set as above.

**Errors:** `400` on schema-invalid arguments; `404` on an unknown `pendingWriteId`, or one belonging to another
owner; **`409` when the draft has expired** ("that draft has expired - ask again"; silently re-proposing would
write something the owner last saw ten minutes ago), and from tools that model a state conflict (e.g.
`complete_task` on an already-completed task); `429`; `502`.

---

### POST /api/chat/decline

**Purpose:** Tell the model the owner refused the write, so the turn completes instead of hanging.

**Request:** `{ "messages": [ … ], "pendingWriteId": "pw_01H…", "reason": "optional free text" }`

**Behaviour:** sends a rejected approval response for that pending write (plus `reason` when given), drops the
cache entry, and resumes the loop. **Not** simply dropping the block: an unanswered approval request is rejected
upstream and would break the transcript for every later turn.

**Response:** `200`, `text/event-stream`, same event set.

**Errors:** `400`; `404` on an unknown or foreign `pendingWriteId`; `401`; `502`.

---

### GET /api/meta - one additive field

`MetaResponse` gains **`chatConfigured`** (bool, default false): whether this deployment holds a chat
credential. False means `/api/chat` would answer 503 whatever you asked, so the shell renders no chat icon at
all - strictly `=== true` on the client, so an in-flight `meta` hides the icon rather than offering one that
cannot work. This is the `vehicleLookupConfigured` precedent, for the same reason: a capability, not a
credential, on an endpoint that is anonymous by design.

---

## Notes

- **No endpoint lists the tools.** The catalogue is server-side and the client learns each tool's shape from the
  `schema` on `pending_write`. A `GET /api/chat/tools` would be a second place for the catalogue to be described
  and to drift from `/mcp`'s.
- **Nothing here writes without `/confirm`.** `POST /api/chat` can run reads and can spend tokens; it cannot
  change a row. That is the whole safety property, and it is enforced by the write tools being registered as
  approval-required - the same set the audit filter uses, read from one shared place (technical spec), so the
  two cannot drift.
- **Spending is bounded before the first model call**, not after: the budget guard runs ahead of the request and
  is updated from the reported usage afterwards. A 429 is therefore a refusal to start, not a refund.
- The gateway needs no new route: `/api` already proxies to the WebApi wholesale. **SSE must not be buffered by
  YARP** - verify response buffering is off for this path, or the stream arrives as one lump at the end and the
  streaming UI silently becomes a spinner.
