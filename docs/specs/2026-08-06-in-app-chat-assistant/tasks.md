# Spec Tasks

## Tasks

- [ ] 0. Two spikes, before any of the design below is committed to
  - [ ] 0.1 **Prompt caching survives the abstraction.** Prove `cache_read_input_tokens > 0` on a second
        identical-prefix request made **through `IChatClient`** — the Anthropic extras
        (`ChatOptions.RawRepresentationFactory`) must be able to put a `cache_control` breakpoint on the tool
        block. If it cannot, the seam moves up to `IChatConversationService` and the Anthropic SDK is used
        directly underneath. Both are acceptable; guessing which one we have is not
  - [ ] 0.2 **Thinking blocks round-trip byte-identical** through the same path. On `claude-opus-5` they arrive
        with their text **omitted** — an empty string, not a missing block — and an edited or dropped block is
        rejected on the next turn. If M.E.AI's reasoning mapping is lossy, that is the same fork as 0.1
  - [ ] 0.3 **`McpServerTool.Create(AIFunction)` keeps `[Authorize(Policy = "McpWrite")]` working** under
        `AddAuthorizationFilters()`. If it does, `/mcp` and the chat share literally one object per tool; if it
        does not, keep `WithTools<T>()` and build the chat's functions from the same `MethodInfo` set with a
        drift test (3.4). Decide here, write it into the technical spec, and move on
  - [ ] 0.4 **Measure the prefix** with the API's `count_tokens` — never a client-side tokenizer. The estimate
        is 8–12k tokens; every cost decision below rests on the real number

- [ ] 1. Prerequisites in the existing code, before any chat code exists
  - [ ] 1.1 Write tests: the read/write tool classification is one list, and a tool present in the catalogue
        but absent from both sets (or in both) **fails a test, not a request**
  - [ ] 1.2 Lift `McpAuditFilter.WriteToolNames` (currently **`private`**, `McpAuditFilter.cs:17-27`) into a
        shared `McpToolClassification`. Three things now read it: the audit filter, the approval-required
        marking, and the confirm gate. Two copies of "which tools are writes" is exactly the drift that makes
        the gate skippable
  - [ ] 1.3 Write tests: a write tool invoked with `EntrySource.Chat` stamps `chat` on the row it creates
  - [ ] 1.4 Thread `EntrySource` through the write tools instead of `WriteTools.cs:28`'s
        `private const EntrySource Source = EntrySource.Mcp` — a parameter with a default of `Mcp`, so every
        existing call site and test is unchanged
  - [ ] 1.5 Extract the shared tool pipeline: `McpDatabaseFaultFilter` and `McpAuditFilter` are wired onto the
        **server** pipeline (`McpServerRegistration.cs:33-36`), so a chat invocation would skip both. Wrap the
        shared functions once so both surfaces run the same decorator — **this is where the "second route into
        the domain" actually hides**
  - [ ] 1.6 Verify tests pass

- [ ] 2. `EntrySource.Chat` and its migration
  - [ ] 2.1 Write tests: a row with `source = 'chat'` is accepted on every `IAuditable` table; the down
        migration fails if any `'chat'` row exists (which is correct — it must not silently delete attribution)
  - [ ] 2.2 `EntrySource.Chat = 5` in `src/CarTracker.Shared/EntrySource.cs`, preserving the no-zero-member
        rule, and widen `AuditConfiguration.ConfigureAudit<T>`'s check constraint to include `'chat'`
  - [ ] 2.3 Migration `AddChatEntrySource` — drop/recreate `ck_<table>_source` on every auditable table. A
        dozen-plus constraint pairs in one migration is expected. **No column widening**: `'chat'` is 4 chars
        and the column is `varchar(8)`
  - [ ] 2.4 Verify tests pass

- [ ] 3. `CarTracker.Chat`, the catalogue, and the approval loop
  - [ ] 3.1 Write tests: read tools run inline; a write tool **suspends** and returns a pending write; a turn
        containing a read and a write does not gate the read behind the confirm button; every suspension is
        answered by a confirm or a decline
  - [ ] 3.2 New `CarTracker.Chat` project (references `ModelContextProtocol`, `Domain`, `Shared`; referenced
        by `WebApi`), `AddCarTrackerChat()` registered **after** `AddCarTrackerDomain()` and
        `AddCarTrackerMcp()`. **No domain logic and no tool definitions in it**
  - [ ] 3.3 Packages into `Directory.Packages.props` under an `AI (in-app chat)` group, pinned:
        `Microsoft.Extensions.AI(.Abstractions)` and `Anthropic`. `Chat:ApiKey` from user-secrets in
        development — **`ASPNETCORE_ENVIRONMENT` must be `Development` or user-secrets do not load**, which has
        already produced three fake bugs here. In containers it comes from Key Vault beside the `Lookup:` values
  - [ ] 3.4 `CarTrackerToolCatalogue` — one `AIFunction[]` from the four tool types, consumed by `/mcp` and by
        `ChatOptions.Tools`. **Drift test:** the two catalogues agree name-for-name and schema-for-schema, and a
        tool in one and not the other fails the build. Ordered deterministically by tool name, because an
        unordered tool list silently disables prompt caching
  - [ ] 3.5 The loop is `FunctionInvokingChatClient`, **not hand-rolled** — the previous revision's argument
        against the SDK runner does not apply to M.E.AI, which models this exactly: write tools registered as
        `ApprovalRequiredAIFunction`, suspension as `ToolApprovalRequestContent`, resumption as
        `ToolApprovalResponseContent`
  - [ ] 3.6 Set the four load-bearing options, three of which are footguns:
        **`AllowMultipleToolCalls = false`** (documented: if any call in a response needs approval, *all* of
        them do — including the reads), `MaximumIterationsPerRequest` (start at 8),
        `MaximumConsecutiveErrorsPerRequest`, and `AllowConcurrentInvocation = false` (the tools resolve
        request-scoped services; two calls on one `DbContext` is the failure)
  - [ ] 3.7 `FunctionInvocationServices` must be the **request's scoped provider**, not the root — a root
        provider hands the tools a `DbContext` with no owner pinned, and the vehicle filter then sees nothing.
        Write the test that proves a second owner's vehicle is invisible through a chat tool call
  - [ ] 3.8 `AnthropicChatExtras` — the one class that knows which provider we are on: cache breakpoints,
        `fallbacks: "default"`, refusal handling, effort/thinking. Everything in it degrades to nothing on
        another provider
  - [ ] 3.9 System prompt **frozen** (no interpolated date, reg, user id or version); per-turn context goes in
        the message body after the cached prefix. Assert `cache_read_input_tokens` in a test
  - [ ] 3.10 Verify tests pass

- [ ] 4. The cost ceiling — before the endpoints, not after
  - [ ] 4.1 Write tests: an owner over their daily budget gets **429 with a reset time and no model call is
        made**; the global ceiling refuses independently of any one owner; a zero budget means the chat is off
        for that account
  - [ ] 4.2 `ChatBudget` in `CarTracker.Chat`: per-owner daily token budget and a global daily ceiling, checked
        before the first model call and updated from reported usage after each. Configured under `Chat:` with a
        documented default; **zero means off**, the same fail-safe direction the signup allowlist uses
  - [ ] 4.3 `meta.chatConfigured` on the anonymous `GET /api/meta`, exactly as `vehicleLookupConfigured` does
        it — capability, not credential
  - [ ] 4.4 Verify tests pass

- [ ] 5. The three endpoints
  - [ ] 5.1 Write tests: `POST /api/chat` **cannot change a row** under any input — the whole safety property;
        `/confirm` with a forged or foreign `pendingWriteId` is a 404; an expired one is a 409; `/decline`
        answers the suspension rather than dropping it
  - [ ] 5.2 Server-held pending writes in `IMemoryCache` — tool name, arguments, vehicle, owner, 10-minute
        expiry, opaque id. **The tool name is never read from the request**; there is no `tool` field to send.
        This replaces the previous revision's check, which matched a client-supplied id against a
        client-supplied transcript and so validated the request against itself
  - [ ] 5.3 `ChatEndpoints` under `/api/chat` behind the standard Auth0 fallback policy — **do not mint a
        synthetic assistant token** to satisfy `McpWrite`; that policy binds to the assistant-token scheme
        (`Program.cs:164-171`) and a bearer credential in the web path buys nothing
  - [ ] 5.4 SSE responses (`text`, `tool`, `pending_write`, `done`, `error`). **Verify YARP does not buffer
        this path** — a buffered stream arrives as one lump and the streaming UI silently becomes a spinner
  - [ ] 5.5 Domain validation failures on `/confirm` return the same RFC 9457 `errors` map the web writes
        return, so the draft card marks a bad field inline exactly as an add sheet does
  - [ ] 5.6 Write tests: every write tool works under an **Auth0** principal. The earlier worry that some tools
        read `AssistantClaims.UserId` proved unfounded — `add_vehicle` reads `ICurrentUserAccessor`
        (`WriteTools.cs:146,163`) and only `CurrentUserMiddleware` reads that claim — but the test is cheap and
        it is what settled the question
  - [ ] 5.7 Regenerate the OpenAPI contract and TS types; staleness gate green
  - [ ] 5.8 Verify tests pass

- [ ] 6. Files in, classification out
  - [ ] 6.1 Write tests: `files` accepts the four media types and rejects others with 400; more than 5 files
        is 400; an over-cap PDF says how many pages it had; nothing is ever written to disk or logged
  - [ ] 6.2 Map each `files` entry to an image or document content block by `mediaType`. Add the comment at
        both ends explaining why this list differs from `DocumentStore.AllowedContentTypes` — otherwise a
        future reader "fixes" one to match the other
  - [ ] 6.3 Client: capture-or-file input, HEIC→JPEG conversion, 2576 px long-edge downscale for images, PDFs
        passed through untransformed (rasterising discards the text layer, the most reliable thing in an
        emailed certificate). Check `img-src` allows `blob:` before assuming the preview works
  - [ ] 6.4 System prompt carries the classification rules: identify each file, **state the reading before
        drafting**, decline to draft what it cannot place, ask when a file could be two things
  - [ ] 6.5 Write tests against recorded responses: a non-vehicle image produces **no** pending write; an MOT
        certificate produces exactly one naming `add_service`. The client must not suppress the assistant text
        preceding a draft card — a card with no sentence above it is the failure this guards
  - [ ] 6.6 Add the privacy paragraph: uploaded documents reach a third-party processor. The account-data
        endpoints shipped the same month; this is the honest counterpart
  - [ ] 6.7 Verify tests pass

- [ ] 7. The chat surface
  - [ ] 7.1 Write tests: the draft card renders every argument from the tool's schema, edits are what get
        sent, Discard writes nothing; the panel and the route render from one component; a 429 renders as a
        sentence in the transcript, not a toast
  - [ ] 7.2 A streaming consumer in `src/CarTracker.WebApp/src/api/client.ts` — neither `request()` (which
        does `await response.text()`) nor `apiBlob()` can consume SSE, and there is no multipart helper.
        Tests mock at this seam, so it belongs beside them
  - [ ] 7.3 `ChatPanel` rendered two ways: docked right-hand panel above 900 px, `/:reg/assistant` route
        below. **900 px is `TopNav`/`BottomNav`'s existing breakpoint** and must not become a second,
        nearly-equal number. New glyph goes in `IconSprite` (DEC-013), not an inline `<svg>`
  - [ ] 7.4 The entry points render only on `meta.chatConfigured === true` — strictly `=== true`, so an
        in-flight `meta` hides the icon rather than offering one that 503s
  - [ ] 7.5 Vehicle scope from `useVehicleReg()`. **Do not render `plate={reg}`** — `usePlate()` is the single
        source and `coverage.test.ts` fails the build on it. The unscoped garage route opens with no vehicle
  - [ ] 7.6 The draft card reuses sheet vocabulary (`Field` with `error`, `Combobox` on garage/station,
        `<ConfirmButton>` for Discard) — it should look like an add sheet because it *is* one, pre-filled
  - [ ] 7.7 Axe sweep + `coverage.test.ts` exemptions; verify tests pass

- [ ] 8. Prove it on BT53
  - [ ] 8.1 **Choose the model by measurement, not by price.** Run `claude-sonnet-5` and `claude-opus-5` over
        the workbook's own receipts and BT53's MOT certificate. Sonnet 5 is in the same high-resolution vision
        tier at 40% of the cost, so it is the one to beat — but a misread litre figure is the failure this spec
        exists to avoid. Record the result and set the default
  - [ ] 8.2 Sweep `effort` `low`/`medium`/`high` on the recorded transcripts and set the default. After
        caching, effort is the cost lever
  - [ ] 8.3 Ask "what needs my attention?" and confirm the answer matches the dashboard's attention panel item
        for item — both called `IDerivedMetricsService`, so a difference is a bug in this loop
  - [ ] 8.4 Photograph BT53's MOT pass, correct a misread field, save; confirm the record, its mileage reading
        and its mirrored expense exist and are stamped `chat`. Discard a second draft; confirm nothing
  - [ ] 8.5 Attach an MOT PDF and an odometer photo together **with no message**; confirm a stated reading of
        each and two drafts. Attach something that is not a vehicle document; confirm no draft card
  - [ ] 8.6 Attempt a fuel receipt → `Fuel`-category `log_expense`; confirm it is refused as on the expense
        sheet, and that `log_fuel_fillup` is what gets drafted instead
  - [ ] 8.7 Record the real cost of one photo-to-record conversation from `usage`, and write it into the spec.
        Every cost claim in this document is an estimate until this task replaces it
  - [ ] 8.8 Full suite, both builds, codegen gate; update roadmap/README/CLAUDE.md and record DEC-019
