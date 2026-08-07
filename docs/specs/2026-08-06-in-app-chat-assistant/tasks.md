# Spec Tasks

## Tasks

- [ ] 1. Prerequisites in the existing code, before any chat code exists
  - [ ] 1.1 Write tests: the read/write tool classification is one list, and a tool present in the catalogue
        but absent from both sets (or in both) **fails a test, not a request**
  - [ ] 1.2 Lift `McpAuditFilter.WriteToolNames` (currently **`private`**, `McpAuditFilter.cs:17-27`) into a
        shared `McpToolClassification` and have the filter read from it. The confirm-gate depends on this
        list; two copies of "which tools are writes" is exactly the drift that makes the gate skippable
  - [ ] 1.3 Write tests: a write tool invoked with `EntrySource.Chat` stamps `chat` on the row it creates
  - [ ] 1.4 Thread `EntrySource` through the write tools instead of `WriteTools.cs:28`'s
        `private const EntrySource Source = EntrySource.Mcp` — a parameter with a default of `Mcp`, so every
        existing call site and test is unchanged
  - [ ] 1.5 Verify tests pass

- [ ] 2. `EntrySource.Chat` and its migration
  - [ ] 2.1 Write tests: a row with `source = 'chat'` is accepted on every `IAuditable` table; the down
        migration fails if any `'chat'` row exists (which is correct — it must not silently delete attribution)
  - [ ] 2.2 `EntrySource.Chat = 5` in `src/CarTracker.Shared/EntrySource.cs`, preserving the no-zero-member
        rule, and widen `AuditConfiguration.ConfigureAudit<T>`'s check constraint to include `'chat'`
  - [ ] 2.3 Migration `AddChatEntrySource` — drop/recreate `ck_<table>_source` on every auditable table. A
        dozen-plus constraint pairs in one migration is expected. **No column widening**: `'chat'` is 4 chars
        and the column is `varchar(8)`
  - [ ] 2.4 Verify tests pass

- [ ] 3. The `CarTracker.Chat` project and the conversation loop
  - [ ] 3.1 Write tests: the loop runs read tools inline and appends their `tool_result`; it **halts on the
        first write tool** and returns the pending call; a turn containing both reads and a write runs the
        reads and still halts; every `tool_use` block is answered by a `tool_result` with a matching id
  - [ ] 3.2 New `CarTracker.Chat` project (references `ModelContextProtocol`, `Domain`, `Shared`; referenced
        by `WebApi`), `AddCarTrackerChat()` registered **after** `AddCarTrackerDomain()` and
        `AddCarTrackerMcp()`. No domain logic in it
  - [ ] 3.3 Add the `Anthropic` SDK to `Directory.Packages.props` under its own item group, pinned like
        everything else. `Anthropic:ApiKey` from user-secrets in development — **`ASPNETCORE_ENVIRONMENT`
        must be `Development` or user-secrets do not load**, which has already produced three fake bugs here
  - [ ] 3.4 Tool schemas generated from the existing `[McpServerTool]`/`[Description]` attributes — one
        catalogue, no second list. Prompt caching on system prompt + tool definitions, ordered deterministically
        by tool name, and the system prompt **frozen** (no interpolated date, reg or user id)
  - [ ] 3.5 Hand-rolled loop, **not the SDK's `BetaToolRunner`** — the runner gates a tool synchronously and
        this gate spans an HTTP round trip and a human, so the loop must halt, return, and resume from a later
        request
  - [ ] 3.6 Verify tests pass

- [ ] 4. The three endpoints
  - [ ] 4.1 Write tests: `POST /api/chat` **cannot change a row** under any input — the whole safety property;
        `/confirm` rejects a `tool`/`toolUseId` mismatch with 400; `/confirm` on a non-write tool is refused;
        `/decline` appends an error `tool_result` rather than dropping the block
  - [ ] 4.2 `ChatEndpoints` under `/api/chat` behind the standard Auth0 fallback policy — **do not mint a
        synthetic assistant token** to satisfy `McpWrite`; that policy binds to the assistant-token scheme
        (`Program.cs:164-171`) and a bearer credential in the web path buys nothing
  - [ ] 4.3 SSE responses (`text`, `tool`, `pending_write`, `done`, `error`). **Verify YARP does not buffer
        this path** — a buffered stream arrives as one lump and the streaming UI silently becomes a spinner
  - [ ] 4.4 Domain validation failures on `/confirm` return the same RFC 9457 `errors` map the web writes
        return, so the draft card marks a bad field inline exactly as an add sheet does
  - [ ] 4.5 Write tests: every write tool works under an **Auth0** principal — `add_vehicle` and the token
        tools read `AssistantClaims.UserId`, which is absent there. This is the cheapest way to find out which
  - [ ] 4.6 Regenerate the OpenAPI contract and TS types; staleness gate green
  - [ ] 4.7 Verify tests pass

- [ ] 5. Files in, classification out
  - [ ] 5.1 Write tests: `files` accepts the four media types and rejects others with 400; more than 5 files
        is 400; an over-cap PDF says how many pages it had; nothing is ever written to disk or logged
  - [ ] 5.2 Map each `files` entry to an `image` or `document` content block by `mediaType`. Add the comment
        at both ends explaining why this list differs from `DocumentStore.AllowedContentTypes` — otherwise a
        future reader "fixes" one to match the other
  - [ ] 5.3 Client: capture-or-file input, HEIC→JPEG conversion, 2576 px long-edge downscale for images, PDFs
        passed through untransformed (rasterising discards the text layer, the most reliable thing in an
        emailed certificate). Check `img-src` allows `blob:` before assuming the preview works
  - [ ] 5.4 System prompt carries the classification rules: identify each file, **state the reading before
        drafting**, decline to draft what it cannot place, ask when a file could be two things
  - [ ] 5.5 Write tests against recorded responses: a non-vehicle image produces **no** `pending_write`; an
        MOT certificate produces exactly one naming `add_service`. The client must not suppress the assistant
        text preceding a `pending_write` — a card with no sentence above it is the failure this guards
  - [ ] 5.6 Verify tests pass

- [ ] 6. The chat surface
  - [ ] 6.1 Write tests: the draft card renders every argument from the tool's schema, edits are what get
        sent, Discard writes nothing; the panel and the route render from one component
  - [ ] 6.2 A streaming consumer in `src/CarTracker.WebApp/src/api/client.ts` — neither `request()` (which
        does `await response.text()`) nor `apiBlob()` can consume SSE, and there is no multipart helper.
        Tests mock at this seam, so it belongs beside them
  - [ ] 6.3 `ChatPanel` rendered two ways: docked right-hand panel above 900 px, `/:reg/assistant` route
        below. **900 px is `TopNav`/`BottomNav`'s existing breakpoint** and must not become a second,
        nearly-equal number. New glyph goes in `IconSprite` (DEC-013), not an inline `<svg>`
  - [ ] 6.4 Vehicle scope from `useVehicleReg()`. **Do not render `plate={reg}`** — `usePlate()` is the single
        source and `coverage.test.ts` fails the build on it. The unscoped garage route opens with no vehicle
  - [ ] 6.5 The draft card reuses sheet vocabulary (`Field` with `error`, `Combobox` on garage/station,
        `<ConfirmButton>` for Discard) — it should look like an add sheet because it *is* one, pre-filled
  - [ ] 6.6 Axe sweep + `coverage.test.ts` exemptions; verify tests pass

- [ ] 7. Prove it on BT53
  - [ ] 7.1 Ask "what needs my attention?" and confirm the answer matches the dashboard's attention panel
        item for item — both called `IDerivedMetricsService`, so a difference is a bug in this loop
  - [ ] 7.2 Photograph BT53's MOT pass, correct a misread field, save; confirm the record, its mileage
        reading and its mirrored expense exist and are stamped `chat`. Discard a second draft; confirm nothing
  - [ ] 7.3 Attach an MOT PDF and an odometer photo together **with no message**; confirm a stated reading of
        each and two drafts. Attach something that is not a vehicle document; confirm no draft card
  - [ ] 7.4 Attempt a fuel receipt → `Fuel`-category `log_expense`; confirm it is refused as on the expense
        sheet, and that `log_fuel_fillup` is what gets drafted instead
  - [ ] 7.5 Full suite, both builds, codegen gate; update roadmap/README/CLAUDE.md and record the DEC
