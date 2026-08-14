# Spec Tasks

## Tasks

- [x] 0. Two spikes, before any of the design below is committed to
  **Ran 2026-08-14 in `tests/CarTracker.Chat.Tests` (live-gated: they skip with no key, so CI stays green).
  Both gating spikes pass — the `IChatClient` seam holds and DEC-019 stands.** 0.3 was answered with the
  catalogue in task 3, and its answer changed the design: see below.
  - [x] 0.1 **Prompt caching survives the abstraction — PASSES, with one binding constraint.**
        `write 9602, read 0` then `write 0, read 9602` through `IChatClient`. But the breakpoint must be
        **placed by hand on the system block**: top-level auto-caching puts it on the *last* cacheable block,
        which in a chat request is the user's own turn, so the first attempt rewrote the entire prefix on every
        request and read nothing — silently, at 1.25× forever. **Therefore `ChatOptions.Instructions` must not
        carry the system prompt** (there is nowhere to attach a breakpoint to it); it goes on
        `MessageCreateParams.System` as a `TextBlockParam` with `CacheControlEphemeral`, inside
        `AnthropicChatExtras`
  - [x] 0.2 **Thinking blocks round-trip — PASSES.** They arrive as `TextReasoningContent` with `Text` empty
        and `ProtectedData` populated (the signature, which is the part the API rejects if tampered with), and
        echoing the assistant turn back verbatim produced a clean second turn. Nothing to build; one thing not
        to do — never filter reasoning content out because its text looks empty
  - [x] 0.3 **Answered 2026-08-14, and the answer is no — one object per tool is impossible.**
        `CatalogueSeamTests` asserts it: `McpServerTool` descends straight from `System.Object` while
        `AIFunction` sits under `AITool`, so neither type can hold the other and there is no
        `McpServerTool.Create(AIFunction)` to keep an attribute working through. **The single definition is
        therefore the `MethodInfo`** — `CarTrackerToolCatalogue.Methods` — with each surface building its own
        wrapper from it and `CatalogueDriftTests` comparing the two name-for-name and schema-for-schema. The
        seam test is written so that a future SDK which *does* unify them fails a test rather than going
        unnoticed
  - [x] 0.4 **Measured: 16,905 tokens for 49 tools** — the 8–12k estimate was low. 10.6p to write the cache on
        Opus 5, 4.2p on Sonnet 5; per-turn reads are under a penny either way. **The first run said 65,957**,
        because the catalogue was built without a service provider and the five tools taking a
        `CarTrackerDbContext` published the DbContext's entire public surface as a tool argument (~19,000 chars
        each). Nothing errors — the tools just become enormous. `CatalogueShapeTests` prints per-tool sizes so
        the next occurrence is one command away

- [x] 1. Prerequisites in the existing code, before any chat code exists
  - [x] 1.1 Write tests: the read/write tool classification is one list, and a tool present in the catalogue
        but absent from both sets (or in both) **fails a test, not a request**
  - [x] 1.2 Lift `McpAuditFilter.WriteToolNames` (currently **`private`**, `McpAuditFilter.cs:17-27`) into a
        shared `McpToolClassification`. Three things now read it: the audit filter, the approval-required
        marking, and the confirm gate. Two copies of "which tools are writes" is exactly the drift that makes
        the gate skippable
  - [x] 1.3 Write tests: a write tool invoked with `EntrySource.Chat` stamps `chat` on the row it creates
  - [x] 1.4 Thread `EntrySource` through the write tools instead of `WriteTools.cs:28`'s
        `private const EntrySource Source = EntrySource.Mcp`. Landed as a **DI-resolved `WriteSurface`**
        (`CarTracker.Domain/Writes/`) rather than a defaulted argument: a defaulted argument would appear in the
        tool's JSON schema, and a model able to set its own attribution could claim a figure it read off a
        photograph had been typed by a person. Scoped, defaulting to `Mcp`, mirroring `CurrentUserAccessor` — so
        every existing call site and test is unchanged. Threaded through 25 of the 30 write tools; the five
        vehicle-settings tools take no surface because `VehicleUpdateService` inherits the *vehicle's* source
        for its purchase mirror
  - [x] 1.5 **Done as `ToolFaultPolicy` + `GuardedTool`** (unblocked by 0.3: the decorator wraps `AIFunction`s).
        The fault half of `McpDatabaseFaultFilter` — the 15s call budget, the Postgres-fault explanation — moved
        into a `ToolFaultPolicy` both surfaces call, and the filter now delegates to it. **The audit half is
        deliberately not reproduced**: `AssistantWriteAudit` is keyed to an assistant token and a chat write has
        none; the human who pressed Save is the record, and the row's `EntrySource.Chat` is the attribution.
        Wrapping also turned up a third divergence nobody had listed: the tools throw `McpException` to say
        "no vehicle matches that plate", which `/mcp` renders as a readable tool result and the chat would have
        raised as an *exception* — counted against `MaximumConsecutiveErrorsPerRequest`, so two honest refusals
        in one turn would have ended the conversation. `GuardedTool` returns the message instead
  - [x] 1.6 Verify tests pass

- [x] 2. `EntrySource.Chat` and its migration
  - [x] 2.1 Write tests: a row with `source = 'chat'` is accepted on every `IAuditable` table; the down
        migration fails if any `'chat'` row exists (which is correct — it must not silently delete attribution)
  - [x] 2.2 `EntrySource.Chat = 5` in `src/CarTracker.Shared/EntrySource.cs`, preserving the no-zero-member
        rule, and widen `AuditConfiguration.ConfigureAudit<T>`'s check constraint to include `'chat'`
  - [x] 2.3 Migration `AddChatEntrySource` — drop/recreate `ck_<table>_source` on every auditable table. A
        dozen-plus constraint pairs in one migration is expected. **No column widening**: `'chat'` is 4 chars
        and the column is `varchar(8)`
  - [x] 2.4 Verify tests pass

- [x] 3. `CarTracker.Chat`, the catalogue, and the approval loop
      **Landed 2026-08-14. 22 tests in `tests/CarTracker.Chat.Tests` (three of them live-gated) plus three
      DB-backed ones in `CarTracker.Data.Tests`. No endpoint yet — the loop is reachable only from tests, which
      is why this phase can be committed on its own.**
  - [x] 3.1 Write tests: read tools run inline; a write tool **suspends** and returns a pending write; a turn
        containing a read and a write does not gate the read behind the confirm button; every suspension is
        answered by a confirm or a decline.
        `ConfirmBeforeWriteTests` runs the real loop against a **scripted `IChatClient`** — no key, no cost, runs
        in CI — because the suspension is the safety property of the whole feature and "observed once by hand" is
        not good enough for it. Reads-run-inline is asserted structurally (no read tool is
        `ApprovalRequiredAIFunction`) and behaviourally in `ChatToolScopeTests`, which invokes two of them
        against a real database
  - [x] 3.2 New `CarTracker.Chat` project (references `ModelContextProtocol`, `Domain`, `Shared`; referenced
        by `WebApi`), `AddCarTrackerChat()` registered **after** `AddCarTrackerDomain()` and
        `AddCarTrackerMcp()`. **No domain logic and no tool definitions in it**
  - [x] 3.3 Packages into `Directory.Packages.props` under an `AI (in-app chat)` group, pinned:
        `Microsoft.Extensions.AI(.Abstractions)` and `Anthropic`. `Chat:ApiKey` from user-secrets in
        development — **`ASPNETCORE_ENVIRONMENT` must be `Development` or user-secrets do not load**, which has
        already produced three fake bugs here. In containers it comes from Key Vault beside the `Lookup:` values.
        The live tests read the key from the **WebApi's** user-secrets store by id, so a dev machine holds it in
        exactly one place
  - [x] 3.4 `CarTrackerToolCatalogue` — the `MethodInfo` set (see 0.3), projected to `AIFunction`s for the chat
        and to `McpServerTool`s for `/mcp`, ordered by tool name. `CatalogueDriftTests` compares the two
        name-for-name and schema-for-schema, so a tool in one and not the other fails a test.
        **Passing the service provider is the load-bearing part**: a parameter the factory cannot resolve from DI
        becomes a *published argument*, and built without one the five tools taking a `CarTrackerDbContext`
        advertised the DbContext's entire public surface — 66k tokens against 17k. Nothing errors
  - [x] 3.5 The loop is `FunctionInvokingChatClient`, **not hand-rolled**: write tools registered as
        `ApprovalRequiredAIFunction`, suspension as `ToolApprovalRequestContent`, resumption as
        `ToolApprovalResponseContent`. One thing the tests found: **a decline reaches the model as an ordinary
        `FunctionResultContent`**, not as an approval-protocol message — the loop translates it — which is what
        makes the resumed transcript valid to send back
  - [x] 3.6 Set the four load-bearing options, three of which are footguns:
        **`AllowMultipleToolCalls = false`** (documented: if any call in a response needs approval, *all* of
        them do — including the reads), `MaximumIterationsPerRequest` (8),
        `MaximumConsecutiveErrorsPerRequest` (2), and `AllowConcurrentInvocation = false` (the tools resolve
        request-scoped services; two calls on one `DbContext` is the failure). `ChatOptions` is built by an
        `internal` method so the first of those is asserted rather than reviewed — its wrong value is invisible
        until a turn happens to make two calls at once
  - [x] 3.7 `FunctionInvocationServices` must be the **request's scoped provider**, not the root.
        **The mechanism is `ChatClientBuilder.Build(sp)`**, so the loop is built per request while the provider
        client stays a singleton holding the HTTP connection — one registration is still the provider seam.
        `ChatToolScopeTests` proves it against a real database with two real accounts: each owner lists exactly
        their own car, and naming the other owner's plate refuses **identically to a typo** — there is no third
        answer for "that car belongs to someone else", which is the point. Note the failure mode is not a leak
        but its mirror image: a root provider pins no owner, the filter matches nothing, and the assistant tells
        everyone their garage is empty
  - [x] 3.8 `AnthropicChatExtras` — the one class that knows which provider we are on: the hand-placed cache
        breakpoint and the raw request shape. `CacheCounts` reads the counters from `AdditionalCounts` *or* the
        raw response and returns zeroes rather than throwing on a provider that has no such concept
  - [x] 3.9 System prompt **frozen** — asserted **structurally**: `ChatSystemPrompt.Text` must be a `const`
        (`FieldInfo.IsLiteral`), which forecloses an interpolated date, plate, owner or version outright rather
        than listing the strings to grep for. `The_second_turn_reads_the_cached_prefix` asserts
        `cache_read_input_tokens > 0` through the **shipped** path — settings, adapter, catalogue and service as
        registered — where spike 0.1 only proved the mechanism. It deliberately does **not** assert a cache
        *write* on the first turn: the entry outlives the test run, so a re-run inside the TTL reads what an
        earlier run wrote, and a cold-start assumption is flaky by construction
  - [x] 3.10 Verify tests pass — 273 Domain, 230 Data, 22 Chat, all green

- [x] 4. The cost ceiling — before the endpoints, not after
      **Landed 2026-08-14 (`0.13.7`). 8 DB-backed tests plus 6 offline ones.**
  - [x] 4.1 Write tests: an owner over their daily budget gets refused **with a reset time and no model call
        made**; the global ceiling refuses independently of any one owner; a zero budget means the chat is off
        for that account. The 429 itself belongs to task 5 — what is asserted here is the property behind it,
        `ChatBudgetExceededException` raised inside `ContinueAsync` with `scripted.Requests` empty
  - [x] 4.2 `ChatBudget` in `CarTracker.Chat`: per-owner daily allowance and a global daily ceiling, checked
        before the first model call and updated from reported usage after each. **Kept in a table
        (`chat_usage`, migration `AddChatUsage`), not in memory** — Watchtower recreates the container minutes
        after every CI publish, and an in-memory counter would hand every account a fresh allowance each time,
        silently, and most often on the days work is being done. Keyed `(OwnerId, Day)` on a Europe/London day
        so the reset lands at the owner's midnight, which `Clock.StartOfNextDay()` converts through the zone
        rather than by stamping today's offset onto tomorrow (an hour wrong twice a year, on the one message
        whose entire content is a time). **No query filter on the table**: the global ceiling is a question
        about every account at once, and a filter would answer it with one account's usage while looking
        exactly right. Recorded *after* the turn, because what a turn costs is not knowable before it runs — so
        one turn can overshoot, bounded by `MaxOutputTokens` and the iteration cap, and the next is refused
  - [x] 4.3 `meta.chatConfigured` on the anonymous `GET /api/meta`, exactly as `vehicleLookupConfigured` does
        it — capability, not credential. **The budget is deliberately not part of the answer**: an account over
        its daily allowance still has a chat, and hiding the icon would tell it the feature had been removed
  - [x] 4.4 Verify tests pass — 273 Domain, 238 Data, 31 Chat, 544 front-end

      > **The deployment file writes every key it knows about, so an unset variable arrives as an empty string
      > rather than as an absent key** — and that has two edges here. An empty string binds to a plain `long` by
      > *throwing*, which would take the application down at boot over an allowance nobody filled in; so both
      > allowances are `long?` with the default applied after binding, and `ChatSettingsTests` pins the binder
      > behaviour itself so that tidying the `?` away fails a test rather than a container. And an empty string
      > binds to a `string` perfectly, so a blank `Chat__Model` would have replaced the shipped model id with
      > `""` — a 404 on the first turn, from a file that looks like it says nothing.
      >
      > That leaves **three polarities** across `deploy/.env.example`, which is why each is now stated where it
      > is set: a blank `Lookup__*` means that feature is off and everything else carries on; a blank
      > `Signup__*` means the door is shut; a blank `Chat__DailyTokens*` means *the generous default*, and only
      > an explicit `0` turns the chat off.

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
