# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-06-in-app-chat-assistant/spec.md

## Architecture

```
Browser                        Gateway        WebApi                          Anthropic API
────────────────────────────   ───────────    ─────────────────────────────   ──────────────
ChatPanel / AssistantPage  ──▶ /api/chat  ──▶ ChatConversationService     ──▶ claude-opus-5
  transcript in component                       ├─ ChatToolCatalogue            (vision + tools,
  state (never on the server)                   │   (schemas from                streamed)
                                                │    [McpServerTool])
  draft card ◀────────────────────────────────  ├─ ChatToolInvoker
  (write suspended)                             │   └─ the same DI-resolved
                                                │      *Tools classes /mcp uses
  confirm ────────────────────▶ /api/chat/confirm ─┘        │
                                                            ▼
                                            LogWriteService / ServiceRecordFactory /
                                            FuelEntryFactory / VehicleUpdateService …
                                            (one write path, whichever surface)
```

The chat is a **consumer** of the MCP tool surface, not a second implementation of it. `tech-stack.md` has
said so since DEC-014 ("*Microsoft Agent Framework* is not the MCP host — it is a candidate for the future
in-app chat that would *consume* these tools"); this spec resolves that candidacy in favour of the **official
Anthropic C# SDK** rather than the Agent Framework, for the same reason DEC-014 chose the official MCP SDK:
the thing that exists and is maintained beats the thing that was named first.

## Technical Requirements

### Project and dependency

- **New project `CarTracker.Chat`**, referenced by `CarTracker.WebApi`, referencing
  `CarTracker.ModelContextProtocol`, `CarTracker.Domain` and `CarTracker.Shared`. It holds the conversation
  loop, the tool catalogue, the system prompt and the request/response DTOs. It does **not** hold any domain
  logic — every write goes through an existing service.
- Registration mirrors `McpServerRegistration`: an `AddCarTrackerChat(this IServiceCollection)` extension so
  `Program.cs` stays two lines, called **after** `AddCarTrackerDomain()` and `AddCarTrackerMcp()` (the tool
  types must already be in the container).

### Model configuration

- **Model `claude-opus-5`.** Vision is required (the whole photo path), and the fidelity matters: it reads
  2576 px on the long edge with coordinates mapping 1:1 to pixels, which is what makes small printed digits
  on a receipt legible. Do **not** downgrade the model for cost without measuring on real BT53 paperwork —
  a misread litre figure is the failure this spec exists to avoid.
- **Adaptive thinking is on by default on `claude-opus-5`** — omit the `thinking` parameter. Do **not** pass
  `thinking: {type: "disabled"}`: with thinking off the model occasionally writes a tool call into its
  *visible text* instead of emitting a `tool_use` block, which here means a draft card that silently never
  appears.
- `output_config.effort = "high"` to start; sweep `medium` and `low` on real transcripts before committing —
  Opus 5 performs unusually well at the lower levels and effort is the primary cost lever.
- **Streaming is required.** `max_tokens` sized with headroom (thinking counts against it), and a
  non-streamed call at that size risks the SDK's HTTP timeout. Text deltas relay to the client over SSE.
- **Prompt caching** on the system prompt + tool definitions. The tool list is rendered at position 0, so it
  must be **serialised deterministically** (stable ordering by tool name) or nothing caches. The system prompt
  must be **frozen** — no interpolated date, no vehicle registration, no user id. Per-turn context (today's
  date, the current vehicle) goes in the message body, after the cached prefix.
- API key from configuration `Anthropic:ApiKey`, via user-secrets in development. **`ASPNETCORE_ENVIRONMENT`
  must be `Development` or user-secrets do not load** — this has already produced three fake bugs in this
  repo; check it first when the key "isn't being read".

### The tool catalogue

- Tool definitions are **generated from the existing attributes**, not hand-written: reflect over the
  `[McpServerToolType]` classes in `CarTracker.ModelContextProtocol.Tools`, read `[McpServerTool(Name = …)]`
  for the name, `[Description]` on the method for the description, and the parameter list + `[Description]`
  per parameter for the JSON Schema. A tool added to the MCP surface therefore appears in the chat with no
  edit here. If reflection over the SDK's own metadata is available (`McpServerTool.Create(...)` exposes a
  populated `Tool` with `InputSchema`), prefer that to hand-rolled reflection — the schema the chat advertises
  and the schema `/mcp` advertises must be the same object, not two derivations that can drift.
- **Read vs write is the existing split, read from one place.** `McpAuditFilter.WriteToolNames` is already the
  authoritative set. Lift it to a shared `McpToolClassification` so the audit filter and the chat gate read the
  same list — two copies of "which tools are writes" is exactly the drift that would make the confirm step
  quietly skippable. A tool absent from the catalogue but present in the write set (or vice versa) should fail
  a test, not a request.
- The `vehicle` parameter is **pre-bound**, not left to the model: the chat request carries the current
  registration and the invoker injects it when the model omits it. The model may still name a different
  vehicle explicitly; ownership makes that safe (below).

### The suspend-on-write loop

A manual loop, not the SDK's `BetaToolRunner`. The runner's per-turn hooks gate a tool *synchronously*, and
this gate spans an HTTP round trip and a human — the loop must halt, return, and be resumable from a later
request.

1. `POST /api/chat` with the transcript and the new user message (plus any files).
2. Loop: call the Messages API. While `stop_reason == "tool_use"`:
   - every requested **read** tool executes immediately; its `tool_result` is appended and the loop continues;
   - the first requested **write** tool **halts the loop**. Return `200` with the accumulated assistant
     content, the pending `tool_use` (id, tool name, arguments) and a `pendingWrite` marker. Nothing is
     written. Parallel tool use is possible, so a turn may contain reads *and* a write — run the reads, then
     halt on the write, and return both.
3. The client renders the draft card from the tool name + arguments, using the parameter descriptions as
   labels. The owner edits any field or discards.
4. `POST /api/chat/confirm` with the transcript, the `tool_use_id` and the **final** arguments (which may
   differ from what the model proposed — that is the point). The server executes the tool, appends the real
   `tool_result`, and continues the loop from step 2 so the assistant can acknowledge or ask about the next
   photo.
5. Discard posts a `tool_result` with `is_error: true` and a "the owner declined this write" body, so the
   model is told rather than left waiting — dropping the block instead would break the turn.

**Every `tool_use` block must be answered by a `tool_result` with the matching id**, including the declined
one. The whole assistant `content` array is echoed back on the next request unchanged, thinking blocks
included — editing or dropping them is rejected by the API.

### Attribution and auth

- **`EntrySource.Chat = 5`** — see `database-schema.md`. `ToolHelpers` / the write services must take the
  source rather than hardcoding `Mcp`; today the MCP tools stamp `Mcp` inline, so this is a threaded
  parameter with a default, not a rewrite.
- **No new auth scheme.** `/api/chat` sits behind the existing Auth0 fallback policy like every other `/api`
  route. `CurrentUserMiddleware` has already pinned the local user on `ICurrentUserAccessor` by the time the
  endpoint runs, so the **global EF query filter on `Vehicle`** applies to every tool the chat invokes: a
  vehicle the signed-in owner does not own simply never resolves, and the tool 404s. This is the same
  guarantee the web endpoints get, for free, because it is one filter and not ~35 call sites.
  - **Verify during build:** the tools were written for the assistant-token principal and some read
    `AssistantClaims.UserId` (`add_vehicle`, the token-management endpoints). Under an Auth0 principal that
    claim is absent. Either those tools already fall through to `ICurrentUserAccessor`, or they need to — a
    test asserting each write tool works under an Auth0 principal is the cheapest way to find out.
  - The `McpWrite` policy gates `/mcp`'s tools by scope claim. The chat does not carry those claims and does
    not need them: its gate is the human, and its authorisation is Auth0 plus the ownership filter. Do not
    mint a synthetic assistant token to satisfy the policy — that would put a bearer credential in the web
    path for no security gained.
- **`AssistantWriteAudit` is not extended.** It is keyed to an `AssistantToken`, and a chat write has none.
  Making the FK nullable and adding a surface column is a coherent follow-on spec; doing it here would mean a
  migration and a lifecycle question ("who owns an untokened audit row?") in service of a trail the row's own
  `EntrySource.Chat` already answers for this spec's stories. Recorded as a known gap, not an oversight.

### Files

**Accepted, and this is the single authoritative list** — `spec.md` scope item 3 must match it exactly:
`image/jpeg`, `image/png`, `image/webp`, and `application/pdf`.

- **HEIC from iOS is converted client-side to JPEG.** The API does not accept it, and a silent rejection at
  the far end reads as "the assistant ignored my photo".
- **This list deliberately differs from `DocumentStore.AllowedContentTypes`**
  (`src/CarTracker.Domain/Documents/DocumentStore.cs:48-58`), which additionally takes `image/heic`,
  `image/heif` and `image/gif`. That is not drift: Documents only ever stores and re-serves those bytes,
  whereas these are sent to a model with its own format constraints. Two lists, two jobs — but say so in a
  comment at both ends, because a future reader will otherwise "fix" one to match the other.
- **Images:** downscale client-side to a 2576 px long edge (canvas resize) — Opus 5's maximum useful
  resolution; larger is bytes on the wire for no fidelity. Do not downscale below it by default: receipts and
  odometers are exactly the dense-small-digit case the high-resolution tier exists for. Base64 into `image`
  content blocks.
- **PDFs:** passed through as `document` content blocks, **no client-side transform** — rasterising a PDF in
  the browser would throw away the text layer, which is the most reliable thing in an emailed certificate.
  Cap the page count (a 40-page policy booklet is not a filing task) and reject over it with a message that
  says so.
- **Cap: 5 files per message**, images and PDFs counted together, and a request-body limit sized for that.
  Kestrel's default multipart limit will otherwise reject a phone photo set with an opaque 413.
- **Never written to disk, never to the database, never logged.**
- **CSP:** the model call is server-side, so no `connect-src` change. The client-side *preview* needs
  `img-src` to permit `blob:` (or `data:`), and a PDF preview (even just a filename chip and page count) must
  not reach for a CDN viewer — check `index.html`/the CSP middleware before assuming, because the strict
  policy here is deliberate and has already caught a CDN font regression.

### Classification

- Classification is **prompted behaviour, not code** — the system prompt tells the model to identify each
  file, state its reading, decline to draft what it cannot place, and ask when a file could be two things.
  There is no classifier service, no type enum on the wire, and no server-side branch on file kind. Building
  one would put a second, dumber judgement in front of the model's.
- What *is* code: the draft card renders the model's stated reading as ordinary assistant text above the card
  (it arrives as `text` content in the same turn), and the client must not suppress that text when a
  `pending_write` follows it. A card with no sentence above it is the failure mode this rule exists to
  prevent.
- **Test it at the prompt level.** A fixture image that is not a vehicle document must produce no
  `pending_write` event; a fixture MOT certificate must produce exactly one naming `add_service`. These are
  the two ends of the behaviour and they are cheap to assert against recorded responses.

### Front-end

- **`ChatPanel`** (`components/` or a new `chat/` folder) is the conversation UI, rendered two ways from one
  component — a docked right-hand panel above 900 px, the full `/:reg/assistant` route below. 900 px is the
  breakpoint `TopNav` and `BottomNav` already split on and must not become a second, nearly-equal number.
- Entry points: an icon button in `TopNav` beside `<ReminderBadge>` and an entry in `BottomNav`/`NavMoreSheet`.
  Uses the existing SVG sprite (DEC-013) — a new glyph is added to `IconSprite`, not an inline `<svg>`.
- Vehicle scope comes from `useVehicleReg()`. **Do not render `plate={reg}`** — `usePlate()` is the single
  source, and `coverage.test.ts` fails the build on the mistake. On the unscoped garage route the icon opens
  the panel with no vehicle bound and the tools fall back to the default vehicle.
- Streaming text renders progressively; the draft card is a distinct block in the transcript, not a modal, so
  the conversation around it stays readable.
- The draft card reuses the sheet vocabulary — `Field` with its `error` prop, `Combobox` on place fields
  (garage, station) so a model-proposed garage is picked from or corrected against the real reference list,
  `<ConfirmButton>` for Discard. It should look like the add sheets because it *is* one, pre-filled.
- Transcript lives in component state. No global store — same reasoning as `VehicleContext`: a second source
  of truth for something the request already carries.
- Tests mock the chat client at the `api/client.ts` seam, as the rest of the app does, and
  `@auth0/auth0-react` stays globally mocked as signed-in via `src/test/setup.ts`.

## External Dependencies

- **`Anthropic`** (official Anthropic C# SDK, NuGet) — the Messages API client: vision content blocks, tool
  use, streaming, prompt caching.
  - **Justification:** the alternative is hand-rolling HTTP against `/v1/messages`, which means owning the
    SSE parsing, the content-block union and the tool-use round trip by hand — the same "reimplements a
    maintained protocol" argument DEC-014 used to reject hand-rolling MCP. Added to
    `Directory.Packages.props` under a new `Anthropic (in-app chat)` item group, pinned like everything else
    (central package management with transitive pinning is on).
  - **Not** Microsoft Agent Framework. It is an orchestration framework for building an agent loop; the loop
    here is ~80 lines and must suspend across an HTTP boundary in a way a general runner does not model. Its
    name in `tech-stack.md` was a placeholder from before the SDK existed, and this spec retires it.
