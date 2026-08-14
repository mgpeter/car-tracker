# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-06-in-app-chat-assistant/spec.md

## Architecture

```
Browser                        Gateway        WebApi                              Provider
────────────────────────────   ───────────    ─────────────────────────────────   ──────────────
ChatPanel / AssistantPage  ──▶ /api/chat  ──▶ ChatConversationService         ──▶ IChatClient
  transcript in component                       │                                  └─ Anthropic
  state (never on the server)                   ├─ FunctionInvokingChatClient          claude-*
                                                │   (M.E.AI: runs reads, halts
  draft card ◀────────────────────────────────  │    on approval-required writes)
  (write suspended, server-held)                │
                                                ├─ CarTrackerToolCatalogue
  confirm ────────────────────▶ /api/chat/confirm    (ONE AIFunction[] — /mcp
                                                │     wraps the same objects)
                                                ▼
                            LogWriteService / ServiceRecordFactory /
                            FuelEntryFactory / VehicleUpdateService …
                            (one write path, whichever surface)
```

Two seams carry this design, and neither is invented here — both are the .NET-native ones:

- **`Microsoft.Extensions.AI.IChatClient`** is the provider seam. The Anthropic SDK ships an implementation
  (`new AnthropicClient().AsIChatClient("claude-opus-5")`), so choosing Anthropic first costs nothing in
  portability: swapping provider is swapping the innermost client in one registration.
- **`Microsoft.Extensions.AI.AIFunction`** is the tool seam, and it is the same currency the MCP C# SDK
  already speaks — `McpServerTool.Create(AIFunction, McpServerToolCreateOptions?)` exists precisely to wrap
  one. So "the chat and `/mcp` share one catalogue" stops being a discipline and becomes a type.

**The chat is a consumer of the MCP tool surface, not a second implementation of it** (DEC-014, DEC-017,
DEC-019). It does **not** speak MCP over HTTP to our own `/mcp` — see the rejection below, which is about
ownership, not tidiness.

## Technical Requirements

### Project and dependencies

- **New project `CarTracker.Chat`**, referenced by `CarTracker.WebApi`, referencing
  `CarTracker.ModelContextProtocol`, `CarTracker.Domain` and `CarTracker.Shared`. It holds the conversation
  service, the system prompt, the cost guard and the request/response DTOs. It holds **no domain logic** —
  every write goes through an existing service — and **no tool definitions**, which live where they already do.
- Registration mirrors `McpServerRegistration`: `AddCarTrackerChat(this IServiceCollection)` so `Program.cs`
  stays two lines, called **after** `AddCarTrackerDomain()` and `AddCarTrackerMcp()`.
- **Packages** (added to `Directory.Packages.props` under an `AI (in-app chat)` item group, pinned like
  everything else — central package management with transitive pinning is on):
  - `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.Abstractions` — the abstraction, the function-calling
    loop, and the approval protocol. Already an indirect dependency: the MCP SDK is built on it.
  - `Anthropic` — the official C# SDK, and the `IChatClient` implementation behind the seam.
  - Add `Anthropic.Foundry` **only if** the Foundry route is taken (see *Buying the tokens*). It is a client
    swap in the same SDK, not a second integration.

### The provider seam — what `IChatClient` buys, and what it costs

The requirement is that the model and the provider can change without touching the tool layer or the endpoints.
`IChatClient` delivers that, with one honest caveat that must be settled by a spike before the rest is built.

**What is portable**, because the abstraction models it: messages and multi-modal content (`DataContent` carries
the photo bytes and media type), `Tools`, `ToolMode`, `AllowMultipleToolCalls`, `MaxOutputTokens`, `ModelId`,
`Instructions`, `ResponseFormat`, `Reasoning` (thinking depth/effort as a first-class option), streaming, and
the whole function-calling + approval loop.

**What is not portable**, because it is Anthropic's own request shape: `cache_control` breakpoints, the
`fallbacks` parameter, `task_budget`, and the exact thinking-block round-trip. The documented escape hatch is
`ChatOptions.RawRepresentationFactory` — a callback that builds the provider's own options object — with
`ChatOptions.AdditionalProperties` beside it. Everything Anthropic-specific goes through **one** adapter class,
`AnthropicChatExtras`, so the count of files that know which provider we are on stays at one.

> **Both gating spikes ran on 2026-08-14 and both pass. The seam holds** —
> `tests/CarTracker.Chat.Tests/AbstractionSpikeTests.cs`.
>
> **Caching survives, but only with the breakpoint placed by hand.** Setting the top-level `CacheControl` (the
> "auto-caching" convenience) puts the breakpoint on the **last cacheable block**, which in a chat request is
> *the user's own turn* — so every request rewrote the whole 9.6k-token prefix and read nothing: `write 9609,
> read 0` then `write 9610, read 0`. Nothing errors; you simply pay the 1.25× write premium forever. Placing it
> explicitly on the system block gives `write 9602, read 0` then `write 0, read 9602`. **Consequence for the
> build: `ChatOptions.Instructions` must not carry the system prompt** — there is nowhere to attach a breakpoint
> to it. The prompt goes on `MessageCreateParams.System` as a `TextBlockParam` with `CacheControlEphemeral`,
> inside `AnthropicChatExtras`.
>
> **Thinking blocks round-trip intact.** They arrive as `TextReasoningContent` with `Text` empty and
> `ProtectedData` populated — the signature is preserved, which is the part the API rejects if tampered with —
> and echoing the assistant turn back verbatim produced a clean second turn. Nothing needs to be done here
> beyond *not* filtering out reasoning content because its text looks empty.

### The tool catalogue — one definition, two surfaces

**The tools are the methods.** `[McpServerTool]`-attributed methods on `VehicleReadTools`, `SummaryReadTools`,
`LogReadTools` and `WriteTools` are the single definition of the assistant's capability, and both surfaces are
built from the same `MethodInfo` set:

```
CarTrackerToolCatalogue (new, in CarTracker.ModelContextProtocol)
  └─ AIFunction[]  ← AIFunctionFactory.Create(methodInfo, …) over the four tool types
       ├─ /mcp  : McpServerTool.Create(fn, …)   (or WithTools<T>() — see below)
       └─ chat  : ChatOptions.Tools = […]
```

- **Prefer the catalogue-first shape** (`McpServerTool.Create(AIFunction)`) so there is exactly one object per
  tool in the process. Two things must be proven before adopting it, because `/mcp` is live and audited:
  1. `[Authorize(Policy = "McpWrite")]` on the write tools still gates them under `AddAuthorizationFilters()`.
  2. Nothing depends on the MCP-specific parameter handling the `Create(AIFunction, …)` overload documents
     itself as *not* providing (`IMcpServer`, progress, elicitation). Today nothing does — the tools take DI
     services and primitives — and that is a constraint to keep, not a coincidence to rely on silently.
- **If either fails, keep `WithTools<T>()` for `/mcp`** and build the chat's `AIFunction[]` from the same
  `MethodInfo` set. The two derivations are then guarded by a test: **the MCP catalogue and the chat catalogue
  must agree name-for-name and schema-for-schema**, and a tool present in one and not the other fails the
  build. That test is not optional in either shape; it is what makes "one catalogue" a fact rather than a claim.
- **Read vs write is one list, read from one place.** `McpAuditFilter.WriteToolNames` is currently `private`
  (`McpAuditFilter.cs:17-27`). Lift it to a shared `McpToolClassification`; the audit filter, the
  approval-required marking and the confirm gate all read it. A tool in the catalogue and in neither set — or
  in both — fails a test, not a request.
- **Parameter binding.** The tools take their dependencies as method parameters (`VehicleResolver`,
  `CarTrackerDbContext`, `ICurrentUserAccessor`, …). Under `/mcp` the SDK resolves those from the request's
  `RequestContext.Services`; under chat they resolve from `FunctionInvokingChatClient.FunctionInvocationServices`,
  which **must be the request's scoped provider**, not the root. A root provider here would hand the tools a
  `DbContext` with no owner pinned on it, and the vehicle query filter would silently see nothing.
- **The `vehicle` parameter is pre-bound**, not left to the model: the request carries the current registration
  and the invoker injects it when the model omits it. The model may still name a different vehicle explicitly;
  ownership makes that safe.
- **The filters must not be left behind.** `McpDatabaseFaultFilter` and `McpAuditFilter` are wired onto the
  **server pipeline** (`McpServerRegistration.cs:33-36`), not onto the tool objects. Invoking a tool from the
  chat therefore skips both unless they are re-applied. Fault translation is the visible loss (an assistant
  saying "An error occurred" instead of a sentence about a blocked database); the audit hook is the quiet one.
  Wrap the shared `AIFunction`s once — a `ToolPipeline` decorator carrying fault translation and audit — and
  both surfaces run the same one. **This is the "second route into the domain" the design exists to prevent,
  and it hides in the filters rather than in the tools.**

### The suspend-on-write loop — `FunctionInvokingChatClient`, not a hand-rolled loop

The previous revision of this spec called for a manual loop on the grounds that the SDK's runner gates a tool
*synchronously* while this gate spans an HTTP round trip and a human. That is true of the Anthropic SDK's
`BetaToolRunner`. It is **not** true of `Microsoft.Extensions.AI`, which models exactly this:

1. Write tools are wrapped as **`ApprovalRequiredAIFunction`** (from `McpToolClassification`, so the set cannot
   drift from the audit set). Read tools are plain `AIFunction`s.
2. `POST /api/chat` runs the transcript through `FunctionInvokingChatClient`. Read tools execute inline and
   their results feed back automatically.
3. When the model requests a write, the client **does not invoke it**. It replaces the `FunctionCallContent`
   with a **`ToolApprovalRequestContent`** wrapping it, and the turn returns.
4. The client renders the draft card from the tool's JSON schema and the proposed arguments. The owner edits or
   discards.
5. `POST /api/chat/confirm` resumes by sending a **`ToolApprovalResponseContent`** — approved, with the owner's
   final arguments — and the loop continues so the assistant can acknowledge or move to the next photo.
   `/decline` sends the same content type, rejected, so the model is told rather than left waiting.

Four settings are load-bearing, and three of them are footguns:

- ~~**`ChatOptions.AllowMultipleToolCalls = false`.**~~ **Corrected 2026-08-14: this setting never reached the
  wire.** The Anthropic seam sends a `tool_choice` only when `ChatOptions.ToolMode` is non-null, and that
  defaults to null — so `disable_parallel_tool_use` was never sent and several tool calls per response have
  always been possible. The documented behaviour it was chosen for is real (if *any* call in a response
  requires approval, **every** call does, including reads), but the remedy is not this flag: the loop drops the
  approval requests marked `RequiresConfirmation = false`, which is what a read swept in alongside a write
  arrives as, and answers every remaining suspension together. `ToolMode = Auto` and
  `AllowMultipleToolCalls = true` are now set explicitly — no change on the wire, and the request stops
  claiming something untrue.
- **`MaximumIterationsPerRequest`** — the loop cap. Set it (start at 8) and surface exhaustion as an assistant
  message, not a silent stop.
- **`MaximumConsecutiveErrorsPerRequest`** — a tool failing in a cycle must end the turn, not the budget.
- **`AllowConcurrentInvocation = false`** (the default, and it must stay). Our tools resolve request-scoped
  services; the docs call out exactly this case. Concurrent invocation would put two calls on one `DbContext`.

**Every approval request must be answered.** A `ToolApprovalRequestContent` left unanswered breaks the
transcript for every later turn, which is why `/decline` exists and why discarding is a request, not a silence.

### The confirm gate — server-held, because the transcript is not evidence

The previous revision matched the confirm request's `toolUseId` against the `tool_use` block in the last
assistant message and called that "the check that stops a crafted request from executing a tool the model never
proposed". **The transcript is client-held and client-supplied, so that check validates the request against
itself.** A crafted POST can invent an assistant turn proposing `delete_service` and then confirm it.

The blast radius is small — the ownership filter still applies, and the owner can already delete through the
REST API — so this is not a breach. It is a guarantee the spec claimed and did not provide, and the fix is
cheap:

- On suspension, the server stores the pending write — tool name, arguments, vehicle, owner id — in
  `IMemoryCache` under an opaque `pendingWriteId`, with a **10-minute** expiry.
- `pending_write` returns that id. `/confirm` and `/decline` take **only** the id (plus the owner's edited
  arguments), and the server reads the tool name from its own cache. A tool name in the request body is not
  trusted; there is no reason to send one.
- The cache entry is keyed to the owner. A `pendingWriteId` presented by another account is a 404, which is
  also how a cross-owner vehicle presents.
- Expiry is a real state: `/confirm` on an expired id returns 409 with "that draft has expired — ask again",
  which is the honest failure. Silently re-proposing would write something the owner last saw ten minutes ago.

### Cost, and the ceiling that has to exist before this ships

This is the first feature that spends money per request, on a deployment other people sign into. **Absent a
ceiling, an authenticated stranger with a keyboard is an unbounded bill.** The spec is not finished without one.

**What a conversation costs.** **Measured 2026-08-14 with the API's own `count_tokens`** — the estimate this
paragraph used to carry (8–12k, from character counts) was low:

| | tokens | Opus 5 ($5/$25) | Sonnet 5 ($2/$10) |
|---|---|---|---|
| Tool catalogue, 49 tools | **16,905** | — | — |
| Cache **write** (first turn of a conversation, 1.25×) | | **10.6p** | **4.2p** |
| Cache **read** (each later turn, 0.1×) | | 0.85p | 0.34p |
| One 2576 px photo (≤4,784 image tokens) | ~4,800 | 2.4p | 1.0p |

A receipt → draft → confirm conversation lands around **15–35p on Opus 5, 6–14p on Sonnet 5**, and a month of
real use is single-digit pounds — less than the Azure VM it runs on. The risk is not the steady state.

> **The measurement itself has a trap in it, and it cost 4× before it was spotted.** The first run reported
> **65,957 tokens**. The SDK decides whether a parameter is a *service* (bound from DI, invisible to the model)
> or an *argument* (published in the tool's JSON schema) by asking a service provider — and the spike built the
> catalogue without one. The five tools that take a `CarTrackerDbContext` therefore published the DbContext's
> entire public surface as a tool argument: ~19,000 characters each, against ~1,300 for the same tool built
> correctly. **Nothing errors. The tools just become enormous and ask the model for a database.** The chat's
> catalogue must be built with the service provider for the same reason `/mcp`'s is, and
> `CatalogueShapeTests.Report_the_catalogue_shape` prints per-tool sizes so the next occurrence is one command
> away rather than a slow bleed on every request.

**The controls, in the order they matter:**

1. **A per-owner daily token budget**, enforced in `CarTracker.Chat` before the first model call and updated
   from `usage` after each. Over budget is **429** with an RFC 9457 body saying when it resets — the same shape
   every other refusal in this app uses. Configured under `Chat:` with a documented default; **zero means the
   chat is off for that account**, following the allowlist's fail-safe direction.
2. **A global daily ceiling** beside it. The per-owner budget bounds one stranger; this one bounds all of them.
3. **`MaximumIterationsPerRequest`**, the file cap (5) and the image downscale (2576 px long edge) are cost
   controls as much as UX ones — an un-downscaled phone photo is several times the tokens for no fidelity, and
   an unbounded loop is the only way a single turn gets genuinely expensive.
4. **Prompt caching**, which is worth ~90% of the prefix and is dealt with below.
5. **`meta.chatConfigured`** on the anonymous `GET /api/meta`, so a deployment with no key renders no chat icon
   at all. Exactly the precedent `vehicleLookupConfigured` set: a control that cannot work is not offered.

**Prompt caching, and the one property that makes it work.** Cache the system prompt + tool definitions as one
prefix. It only ever hits if the prefix is **byte-identical**, so: the tool list is serialised deterministically
(ordered by tool name), and the system prompt is **frozen** — no interpolated date, no registration, no user id,
no version string. Per-turn context (today's date, the current vehicle, the owner's units) goes in the message
body, after the cached prefix. Writes cost 1.25× and reads 0.1×; `claude-opus-5`'s minimum cacheable prefix is
512 tokens, which this clears many times over. Assert `cache_read_input_tokens` in a test, because a silent
invalidator (a timestamp, an unordered dictionary) is a 10× cost regression with no other symptom.

> **Tool search is not adopted, but it is closer than it was.** `tool_search_tool_regex_20251119` with
> `defer_loading` would cut the cold prefix from 16.9k tokens to ~2k by making the model search for tools. At
> 10.6p per cold conversation on Opus 5 the search round trip is still roughly a wash, and it adds a failure
> mode where the model cannot find a tool it needs. **Revisit when either is true:** the catalogue passes ~25k
> tokens, or measurement shows conversations are mostly one turn long — which is where a cold prefix hurts
> most, and which one-question-and-done usage would produce. `PrefixMeasurementTests` asserts the catalogue
> stays under 20k, so growth past that fails a test rather than quietly raising the bill.

### Model configuration

- **Start on `claude-sonnet-5`, and measure `claude-opus-5` against BT53's real paperwork.** This reverses the
  previous revision, on one fact: Sonnet 5 is the first Sonnet-tier model in the **high-resolution vision tier**
  — the same 2576 px long edge and 1:1 pixel coordinates that made Opus the choice — at **$2/$10 per Mtok**
  against Opus 5's $5/$25. The reason for preferring Opus was fidelity on small printed digits, and that reason
  is now available at 40% of the price. The rule the previous revision wrote still stands and now cuts both
  ways: **do not choose the model for cost without measuring on real BT53 paperwork.** A misread litre figure is
  the failure this spec exists to avoid, and the fixture for that measurement is the workbook's own receipts.
  `Chat:Model` is configuration; the default is whichever wins the measurement, and the measurement is a task.
- **Leave thinking on.** On `claude-opus-5` it is on by default (omitting the parameter runs adaptive). Do
  **not** disable it: with thinking off the model occasionally writes a tool call into its *visible text*
  instead of emitting a call — which here means a draft card that silently never appears — and can leak
  `<thinking>` tags into the response. On Opus 5, disabling is in any case refused above `effort: high`.
- **Effort: start at `medium`.** The previous revision said `high`. This is a small-catalogue extraction task,
  not long-horizon agentic work, and Opus 5 and Sonnet 5 are both unusually strong at the lower levels. Effort
  is the primary cost lever after caching; sweep `low`/`medium`/`high` on recorded transcripts.
- **`max_tokens` with headroom** — thinking counts against it. Streaming is required: text deltas relay to the
  client over SSE, and a non-streamed call at this size risks the SDK's HTTP timeout.
- **Handle `stop_reason: "refusal"` before reading content.** Opus 5 and Sonnet 5 both run elevated
  cybersecurity classifiers; a decline is an HTTP 200 with an empty or partial content array. Surface it as an
  assistant message explaining the request was declined — never as an error, which would lose the explanation —
  and opt into server-side `fallbacks: "default"` so a false positive on benign paperwork is re-served rather
  than dead-ending. Both go through `AnthropicChatExtras`; both are Anthropic-specific and must degrade to
  nothing on another provider.

### Buying the tokens: direct, or through Azure

Both are the same model at the same rate. The choice is billing and feature parity, and it is a one-line client
swap either way — `new AnthropicClient()` versus `new AnthropicFoundryClient(...)` (package `Anthropic.Foundry`),
both of which expose `AsIChatClient()`.

| | Anthropic direct | Claude in Microsoft Foundry |
|---|---|---|
| Rate | List ($5/$25 Opus 5, $2/$10 Sonnet 5) | Same rates, billed as Claude Consumption Units at $0.01/CCU |
| Invoice | Separate card | Azure Marketplace — one bill with the VM, and it can draw on an Azure commitment |
| Prompt caching | Yes, with explicit breakpoints | Yes, and enabled automatically |
| Auth | `Chat:ApiKey` (Key Vault, as `Lookup:` already is) | Entra ID / Foundry key — one less secret of a different kind |
| Known gaps | — | Batches API, Models API, mid-conversation system messages and task budgets are not available; verify anything else against the availability matrix before depending on it |

**Recommendation: Anthropic direct, and treat Foundry as a config switch rather than a plan.** None of the
Foundry gaps bite this feature today, and the Azure-invoice argument is real if the deployment ever runs on
Azure credit — but direct is the surface everything is documented against, and the swap costs one line the day
it stops being true. The other Azure options were priced and rejected: **Foundry Models' own catalogue** (Phi-4,
Llama, Mistral, serverless from ~$0.07/Mtok) is cheaper per token but gives up the vision fidelity the photo
path depends on, and **managed compute** bills by the hour — an Azure T4 is ~$0.53/hour, roughly £290/month,
against a chat bill of single-digit pounds. Running the model ourselves is the most expensive option on the
table by two orders of magnitude, which is why there is a "no local model" note in `spec.md`.

### Attribution and auth

- **`EntrySource.Chat = 5`** — see `database-schema.md`. **Built 2026-08-14.** The tools no longer hardcode
  `Mcp`: they take a **`WriteSurface`** (`CarTracker.Domain/Writes/`) resolved from DI, scoped and defaulting to
  `Mcp`, mirroring `CurrentUserAccessor`. The chat's request scope pins `Chat` before invoking anything.
  - **Deliberately not a tool argument with a default**, which is what an earlier draft of this spec said.
    A defaulted argument lands in the tool's JSON schema, and a model that can set its own attribution can claim
    a figure it read off a photograph was typed by a person — the one thing this column must never be able to
    say falsely. Taking it as a container-supplied parameter keeps it out of the schema entirely.
  - 25 of the 30 write tools take it. The five vehicle-settings tools (`set_insurance`, `set_road_tax`,
    `update_vehicle_profile`, `set_fluids`, `set_tyre_specs`) do not: `VehicleUpdateService` stamps its purchase
    mirror with the *vehicle's* own source, not the caller's, and changing that is a different decision on a
    path the web `PATCH` shares.
- **No new auth scheme.** `/api/chat` sits behind the existing Auth0 fallback policy like every other `/api`
  route. `CurrentUserMiddleware` has pinned the local user on `ICurrentUserAccessor` by the time the endpoint
  runs, so the **global EF query filter on `Vehicle`** applies to every tool the chat invokes: a vehicle the
  signed-in owner does not own never resolves, and the tool 404s. One filter, not ~35 call sites.
  - **A risk the previous revision flagged has already evaporated.** It warned that some tools read
    `AssistantClaims.UserId`, which is absent under an Auth0 principal. They do not: `add_vehicle` reads
    `ICurrentUserAccessor` (`WriteTools.cs:146,163`), and the only reader of that claim is
    `CurrentUserMiddleware` itself. Keep the test asserting every write tool works under an Auth0 principal —
    it is cheap and it is what caught this — but the warning is retired.
  - The `McpWrite` policy gates `/mcp`'s tools by scope claim. The chat does not carry those claims and does
    not need them: its gate is the human, and its authorisation is Auth0 plus the ownership filter. **Do not
    mint a synthetic assistant token to satisfy the policy** — that would put a bearer credential in the web
    path for no security gained.
- **`AssistantWriteAudit` is not extended.** It is keyed to an `AssistantToken` and a chat write has none.
  Making the FK nullable and adding a surface column is a coherent follow-on; the row's own `EntrySource.Chat`
  answers this spec's stories. Recorded as a known gap, not an oversight.

### Why not speak MCP to our own `/mcp`

Tempting, and wrong, for one reason that outranks the others: **ownership**. `/mcp` is gated by
`RequireAuthorization("McpRead")`, and per-user isolation rests on `CurrentUserMiddleware` pinning
`ICurrentUserAccessor` from the *request's* principal. A loopback HTTP call carries whatever credential the chat
presents, so making it work would mean minting an `AssistantToken` per signed-in user per request — a bearer
credential in the web path, and a minting bug is a cross-tenant leak. In-process invocation inherits the correct
owner for free, because the tool runs inside the user's own authenticated request.

Two lesser costs, for completeness: a serialisation round trip on every tool call, and Streamable HTTP session
plumbing to talk to ourselves. The `AIFunction` catalogue gives the same "one definition" guarantee with none of
it.

### Files

**Accepted, and this is the single authoritative list** — `spec.md` scope item 3 must match it exactly:
`image/jpeg`, `image/png`, `image/webp`, and `application/pdf`.

- **HEIC from iOS is converted client-side to JPEG.** The API does not accept it, and a silent rejection at the
  far end reads as "the assistant ignored my photo".
- **This list deliberately differs from `DocumentStore.AllowedContentTypes`**
  (`src/CarTracker.Domain/Documents/DocumentStore.cs:48-58`), which additionally takes `image/heic`,
  `image/heif` and `image/gif`. That is not drift: Documents only ever stores and re-serves those bytes, whereas
  these are sent to a model with its own format constraints. Two lists, two jobs — say so in a comment at both
  ends, because a future reader will otherwise "fix" one to match the other.
- **Images:** downscale client-side to a 2576 px long edge (canvas resize) — the maximum useful resolution on
  both candidate models; larger is bytes on the wire and tokens on the bill for no fidelity. Do not downscale
  below it by default: receipts and odometers are exactly the dense-small-digit case the high-resolution tier
  exists for.
- **PDFs:** passed through as document content, **no client-side transform** — rasterising in the browser would
  throw away the text layer, the most reliable thing in an emailed certificate. Cap the page count and reject
  over it with a message that says how many pages it had.
- **Cap: 5 files per message**, images and PDFs counted together, and a request-body limit sized for that.
  Kestrel's default multipart limit will otherwise reject a phone photo set with an opaque 413.
- **Never written to disk, never to the database, never logged.** And say out loud in the privacy note that they
  are sent to a third-party processor — this deployment now carries other people's documents, and Art. 15/17/20
  endpoints shipped in the same month.
- **CSP:** the model call is server-side, so no `connect-src` change. The client-side *preview* needs `img-src`
  to permit `blob:` (or `data:`), and a PDF preview must not reach for a CDN viewer — check `index.html` and the
  CSP middleware before assuming, because the strict policy here has already caught a CDN font regression.

### Classification

- Classification is **prompted behaviour, not code** — the system prompt tells the model to identify each file,
  state its reading, decline to draft what it cannot place, and ask when a file could be two things. There is no
  classifier service, no type enum on the wire, and no server-side branch on file kind. Building one would put a
  second, dumber judgement in front of the model's.
- What *is* code: the draft card renders the model's stated reading as ordinary assistant text above the card
  (it arrives as text in the same turn), and the client must not suppress that text when an approval request
  follows it. A card with no sentence above it is the failure mode this rule exists to prevent.
- **Test it at the prompt level**, against recorded responses: a fixture image that is not a vehicle document
  produces no approval request; a fixture MOT certificate produces exactly one naming `add_service`.

### Front-end

- **`ChatPanel`** (a new `chat/` folder) is the conversation UI, rendered two ways from one component — a docked
  right-hand panel above 900 px, the full `/:reg/assistant` route below. 900 px is the breakpoint `TopNav` and
  `BottomNav` already split on and must not become a second, nearly-equal number.
- Entry points: an icon button in `TopNav` beside `<ReminderBadge>` and an entry in `BottomNav`/`NavMoreSheet`,
  **rendered only when `meta.chatConfigured` is true** — strictly `=== true`, so an in-flight `meta` hides the
  icon rather than offering one that 503s. The same rule the DVLA lookup button now follows.
- Uses the existing SVG sprite (DEC-013) — a new glyph is added to `IconSprite`, not an inline `<svg>`.
- Vehicle scope comes from `useVehicleReg()`. **Do not render `plate={reg}`** — `usePlate()` is the single
  source and `coverage.test.ts` fails the build on the mistake. On the unscoped garage route the icon opens the
  panel with no vehicle bound and the tools fall back to the default vehicle.
- Streaming text renders progressively; the draft card is a distinct block in the transcript, not a modal, so
  the conversation around it stays readable.
- The draft card reuses the sheet vocabulary — `Field` with its `error` prop, `Combobox` on place fields
  (garage, station) so a model-proposed garage is picked from or corrected against the real reference list,
  `<ConfirmButton>` for Discard. It should look like the add sheets because it *is* one, pre-filled.
- A 429 from the budget guard renders as a plain sentence in the transcript saying when it resets — not a toast,
  and not an error banner. Running out of budget is a state, not a fault.
- Transcript lives in component state. No global store — same reasoning as `VehicleContext`: a second source of
  truth for something the request already carries.
- Tests mock the chat client at the `api/client.ts` seam, as the rest of the app does, and `@auth0/auth0-react`
  stays globally mocked as signed-in via `src/test/setup.ts`.

## External Dependencies

- **`Microsoft.Extensions.AI` / `.Abstractions`** — the provider seam, the function-calling loop, and the
  approval protocol.
  - **Justification:** it is the .NET-native abstraction, it is already in the dependency graph beneath the MCP
    SDK, and its `ApprovalRequiredAIFunction` → `ToolApprovalRequestContent` → `ToolApprovalResponseContent`
    round trip is *exactly* this spec's suspend-on-write loop. Writing that loop by hand — as the previous
    revision specified — would be reimplementing a maintained protocol, the same argument DEC-014 used to reject
    hand-rolling MCP.
  - **Not Microsoft Agent Framework.** DEC-014 and DEC-017 retired that name and this does not revive it: the
    Agent Framework is an orchestration layer; `Microsoft.Extensions.AI` is the abstraction library the Anthropic
    and MCP SDKs both already implement against. Different thing, same three words in the namespace.
- **`Anthropic`** (official C# SDK) — the `IChatClient` implementation, vision content, streaming, prompt
  caching, and the Anthropic-specific extras behind `AnthropicChatExtras`.
  - **Justification:** DEC-017 chose it; `AsIChatClient()` means choosing it no longer forecloses anything.
