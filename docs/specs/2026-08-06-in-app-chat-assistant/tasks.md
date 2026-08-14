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

- [x] 5. The three endpoints
      **Landed 2026-08-14 (`0.13.8`). `/api/chat`, `/api/chat/confirm`, `/api/chat/decline`, streaming.**
  - [x] 5.1 Write tests: `POST /api/chat` **cannot change a row** under any input; `/confirm` with a forged or
        foreign `pendingWriteId` finds nothing; an answered draft cannot be answered twice; `/decline` answers
        the suspension rather than dropping it. The endpoint cannot change a row **structurally** rather than by
        checking the request — every write tool is an `ApprovalRequiredAIFunction`, so the loop suspends instead
        of invoking one, and `StreamingTurnTests` asserts that the streaming path suspends exactly as the
        buffered one does (a second path through the loop is not a rendering of the first)
  - [x] 5.2 Server-held pending writes in `IMemoryCache` — tool name, call id, vehicle, owner, 10-minute
        expiry, opaque id from `RandomNumberGenerator`. **There is no `tool` field on the request.** A foreign
        id returns null rather than a distinct refusal, so it presents exactly as an expired or invented one —
        telling them apart would confirm that the id is real. **In memory, unlike the spending ledger, and the
        difference is deliberate**: a restart that forgets a half-finished draft costs one repeated sentence; a
        restart that forgets a day's spending costs money
  - [x] 5.3 `ChatEndpoints` under `/api/chat` behind the standard Auth0 fallback policy — no synthetic assistant
        token, no new scheme, no new policy
  - [x] 5.4 SSE (`text`, `tool`, `pending_write`, `done`, `error`), buffering disabled explicitly with
        `X-Accel-Buffering: no` for anything in front. **The first event is pulled before the response headers
        are written**, which is what lets a spent budget be a real 429 rather than an `error` event inside a 200;
        anything that fails after the stream has opened becomes an `error` event, because by then the status
        line is gone. On YARP: `/api/{**catch-all}` is the same route `/mcp` has streamed Streamable HTTP
        through since Phase 4, so the path is proven — but it is proven for `/mcp`, and task 8 watches this one
        with a real browser attached
  - [x] 5.5 Domain validation failures on `/confirm` — **and here the shipped behaviour is narrower than this
        line asked for, deliberately.** What is checked against the tool's schema before anything runs is a
        field the tool does not have and a required field cleared to nothing: the two mistakes a draft card can
        actually make, both reportable against the field that caused them, returned as the same RFC 9457
        `errors` map every add sheet marks its fields from. A *domain* refusal — a mileage below the current
        reading, a fuel row typed as an expense — is not a schema problem: it comes back through the loop as the
        tool's own sentence and the assistant explains it, and the draft is gone rather than marked. Catching it
        as a field error would mean invoking the tool outside the approval protocol and then invoking it again
        inside it, or re-implementing the domain's rules here — and the copy would be the one that drifted
  - [x] 5.6 Write tests: a write tool works under an **Auth0** principal — asserted as the whole path rather than
        as the worry, in `ChatToolScopeTests`: the same `AIFunction` the model was shown, invoked with the
        request's scope, writing a real row that lands on the owner's own vehicle and stamps `chat`
  - [x] 5.7 Regenerated the OpenAPI contract and TS types; staleness gate green. `messages` is declared as an
        opaque JSON element on all three requests, because the transcript is `Microsoft.Extensions.AI`'s own
        shape and must round-trip byte-for-byte — a hand-written DTO would carry the text of a reasoning block
        and silently drop its signature, which the provider rejects on the next turn
  - [x] 5.8 Verify tests pass — 273 Domain, 239 Data, 41 Chat, 544 front-end

- [ ] 6. Files in, classification out
      **Server half landed 2026-08-14 (`0.13.9`). 6.3 and 6.5 are client-side and land with the surface.**
  - [x] 6.1 Write tests: `files` accepts the four media types and rejects others with a field error; more than
        5 says how many there were; an oversize file says how big it was. **One bad file means none are sent** —
        the alternative is a turn that quietly reads three of five and answers confidently about paperwork it
        never saw
  - [x] 6.2 Each entry becomes an image or document content block by `mediaType`, attached to the message it
        came with. The comment is at both ends: **this list is shorter than `DocumentStore.AllowedContentTypes`
        on purpose** — the documents screen stores bytes it never has to understand and takes HEIC and GIF
        happily, while these are sent to a model to be *read*. HEIC is converted in the browser, which is why a
        phone can attach one and this list still cannot
  - [ ] 6.3 Client: capture-or-file input, HEIC→JPEG conversion, 2576 px long-edge downscale for images, PDFs
        passed through untransformed. **Lands with task 7** — it is part of the composer, and building it before
        the panel exists would mean building it twice
  - [x] 6.4 System prompt carries the classification rules: identify each file, **state the reading before
        drafting**, decline to draft what it cannot place, ask when a file could be two things. Already written
        into the frozen prompt in task 3, and it is frozen, so it cannot drift from this line
  - [ ] 6.5 Write tests against recorded responses: a non-vehicle image produces **no** pending write; an MOT
        certificate produces exactly one naming `add_service`. **Needs real captures**, so it runs with task 8's
        dogfooding rather than against invented fixtures
  - [x] 6.6 The privacy paragraph — in the README now, and repeated where the owner actually attaches something
        when the composer lands. An export cannot recall what has been sent to a processor, so saying so is the
        honest counterpart to the account-data endpoints that shipped the same month
  - [x] 6.7 Verify tests pass — 51 Chat

      > **The page cap is not implemented, and this is the honest note rather than a quiet omission.** 6.1 asked
      > for an over-cap PDF to say how many pages it had. Counting pages needs a PDF parser — object streams make
      > `/Type /Page` unreliable — and adding one to weigh a limit the provider already enforces is a dependency
      > bought for a message. What ships instead are byte caps, which are exact and cover the realistic case (a
      > phone photo set), and a provider refusal surfaces with its own wording as a 502. If real use turns up
      > people attaching workshop manuals, that is when a parser earns its place.

- [x] 7. The chat surface
      **Landed 2026-08-14 (`0.14.0`). 553 front-end tests.**
  - [x] 7.1 Write tests: the draft card renders every argument from the tool's schema, **edits are what get
        sent** (an "80,705" typed over a misread mileage arrives as the integer 80705), Discard writes nothing
        and answers the suspension, and a 429 renders as the server's own sentence in the transcript rather
        than as a toast. The SSE fake pushes its frames **split across chunk boundaries**, because the buffer in
        `readEvents` exists for exactly that and a fake delivering whole frames would never exercise it
  - [x] 7.2 `apiStream` in `src/api/client.ts` — neither `request()` nor `apiBlob()` can consume SSE, both
        being "wait for the whole thing". It returns **an `ApiResult` of a stream, not a stream of results**:
        everything that fails before the first byte is a status the server already chose, so it stays an
        ordinary `ApiError`; only once events flow does failure become an `error` event. Not `EventSource`,
        which is GET-only and cannot carry a bearer — the same wall `apiDownload` exists for. The bearer logic
        is now one `authHeaders()` rather than a third copy
  - [x] 7.3 `ChatPanel` rendered two ways: docked right-hand panel above 900 px, `/:reg/assistant` route below.
        **900 px is not restated anywhere** — the dock is mounted from a button in the top bar, which is itself
        hidden below that breakpoint, so the two surfaces divide on the existing line rather than on a second,
        nearly-equal number. The mobile entry point is a row in the More sheet, which is the only menu there.
        New `ct-chat` glyph in `IconSprite` (DEC-013), not an inline `<svg>`
  - [x] 7.4 Both entry points render only on `meta.chatConfigured === true` — strictly, so an in-flight `meta`
        offers nothing rather than a control that 503s
  - [x] 7.5 Vehicle scope from `useVehicleReg()`, and the panel shows `usePlate()` rather than the slug.
        The garage's dock opens with no vehicle, and says so in its own placeholder
  - [x] 7.6 The draft card reuses sheet vocabulary — `Field` with `error`, `Combobox` on garage and wash
        location reading the same cached reference lists the add sheets read, `<ConfirmButton>` for Discard.
        **Every field is built from the tool's JSON Schema**: thirty hand-written cards would drift from their
        tools the week after they were written, and the drift would show as a field the owner cannot fill
        rather than as a broken build. It is deliberately **not** a `Sheet` — a modal would cover the sentence
        above it saying what was read off the photograph, which is the thing the form is checked against
  - [x] 7.7 Axe sweep (empty and with a draft open) + `coverage.test.ts` exemptions; 558 front-end tests pass

      > **Four things the browser found that no test had.** Running it against the real app was worth more than
      > the tests it passed on the way.
      >
      > 1. **The `done` frame never reached the client, and the symptom was a 500 on a different endpoint three
      >    requests later.** `AIJsonUtilities.DefaultOptions` is `WriteIndented`, so the transcript went out as
      >    twenty lines under a single `data:` prefix. The client parsed the first line, failed, and skipped the
      >    event — after which `/confirm` answered a suspension the transcript it had been handed no longer
      >    contained, and `Answer` threw. Two fixes, because either alone would have left the other latent: the
      >    transcript is now serialised compact, and **the frame writer prefixes every line**, which is what the
      >    SSE spec says and what makes the writer correct whatever an options instance decides. `client.test.ts`
      >    pins the multi-line case — a test that hand-writes its own frames would never have produced it.
      > 2. **The draft card's title was the tool's `[Description]`** — a paragraph written for the model
      >    ("Registration must be unique. Example: registration \"BT53 AKJ\"…") set in uppercase display type.
      >    It is the tool's *name*, re-spaced.
      > 3. **`add_vehicle` has fourteen optional parameters**, and rendering all of them buried the three figures
      >    the owner was there to check under eleven empty boxes. What the assistant filled in, plus what the
      >    tool requires, is the card; the rest folds behind a disclosure.
      > 4. **The garage still read "0 vehicles tracked" beside an assistant that had just added one.** A
      >    confirmed write now invalidates every query, because which screens went stale depends on a tool the
      >    client deliberately does not model.
      >
      > Also corrected while there: the panel used to post a "Saved" note the moment it sent the confirm. The
      > tool runs on the far side of the stream and can still be refused by the domain, so that was a claim the
      > client could not back — the assistant's own next sentence says what happened, and it is the one that knows.

  - [x] 6.3 (from task 6) Capture-or-file input, HEIC→JPEG conversion and a 2576 px downscale, PDFs passed
        through untransformed — rasterising one discards its text layer, the most reliable thing in an emailed
        certificate. The conversion is a canvas round trip, which does the downscale and the re-encoding in one
        pass; where the browser cannot decode HEIC at all (Chrome on Windows) the refusal **names the format**
        and says to share it as a JPEG, because "could not read that" leaves someone standing at a pump with
        nothing to do next. `capture` is deliberately not set on the input: it forces the camera and hides the
        photo library, and the commonest attachment is a certificate already in the roll. No image preview is
        rendered, so the `img-src blob:` question the task raises does not arise

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
