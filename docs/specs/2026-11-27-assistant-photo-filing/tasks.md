# Spec Tasks

These are the tasks to be completed for the spec detailed in
@docs/specs/2026-11-27-assistant-photo-filing/spec.md

> Runs after `docs/specs/2026-11-27-photo-gallery-document-categories/` and builds on its tasks as written,
> including its open assumptions A1 (old `Photo` rows become `ConditionPhoto`), A2 (no `"Photo"` alias) and A3
> (GPS stripped from previews only). Task 1 here needs that spec's tasks 1 and 3 (`DocumentType` members such as
> `CarPhoto` and `PartsReceipt`, `DocumentCategories`, `DocumentKind`, `DocumentGroup`, `GET /api/document-types`).
> Task 3 needs its task 4 (`uploadDocument(reg, file, meta)` with renditions, `useDocumentTypes`, `kindOf`, and
> `UploadSheet` with an `initial` prefill, per its A13). Task 4 needs its tasks 4.5, 4.6 and 5.4 (`UploadSheet`,
> the papers-only `DocumentsPage`, `PhotosPage`). Nothing here re-does categories, renditions or the upload sheet.
>
> Server first: the tool and its link are proved against real PostgreSQL (task 1), then the stream event and the
> prompt (task 2), then the browser (tasks 3 and 4), then configuration and docs (task 5). There is no
> `CarTracker.WebApi.Tests` project, so the rules live in the domain and the tool, where Data tests reach them.
> UI tests are Vitest + Testing Library component tests with axe sweeps (assumed: A6). Writes are asserted on the
> request, not the DOM. Nothing in this spec gives the assistant a new write: `offer_to_file` is a read, and
> every filing is the owner's own upload (assumed: A1).

## Tasks

- [x] 1. **`offer_to_file` tool and the filing link** (assumed: A1, A3, A4, A8, A9, A10, A14)
  - [x] 1.1 Write tests and watch them fail for the missing types, not for a fixture problem.
        Unit tests in `tests/CarTracker.Domain.Tests/FilingLinkTests.cs`: the path is
        `/file?vehicle=...&type=...&title=...&date=...&<link>=...`; `BT53 AKJ` encodes as `BT53%20AKJ`; a title
        with `&`, `#`, `+`, `%`, `£` and spaces round-trips through `HttpUtility.ParseQueryString` unchanged; an absent
        date and an absent link leave their keys out; `url` is origin + path when `PublicOrigin` is set and null
        when it is blank or whitespace; `AppOptions` trims one or more trailing slashes (assumed: A3). Keep one
        server-built path as a literal for the web test in 4.1.
        Integration tests (Testcontainers) in `tests/CarTracker.Data.Tests/FilingOfferTests.cs`, calling
        `FilingTools.OfferToFile(...)` directly as `McpWriteSurfaceTests` calls `WriteTools.AddTask`, with the
        context pinned by `TestOwner.As(ownerId)`: a `PartsReceipt` linked to the vehicle's expense returns the
        registration, type, kind, group and label from `DocumentCategories`, the trimmed title, the date,
        `LinkedTo` equal to the filed-document chip for that expense (assumed: A9), `attachment` echoed, and
        `allowance` equal to `DocumentService.CapacityAsync`; the same for a service record and an issue; no link
        is fine; an omitted `vehicle` uses the default vehicle; another owner's issue id (seeded under a second
        owner) is refused as not this vehicle's; the same owner's other vehicle's expense id is refused; two links
        are refused; a 161-character title is refused and 160 is accepted; a blank title is refused; an unknown
        vehicle is refused with the `list_vehicles` hint; an account at its allowance still gets the offer with a
        summary saying to remove a document first (assumed: A10); and the row count of every table in the
        `public` schema (read from `information_schema.tables`, so `assistant_write_audits` and `documents` are
        included) is the same before and after each call.
        Extend `tests/CarTracker.Data.Tests/McpToolClassificationTests.cs`: its tool-type list gains
        `FilingTools`; `offer_to_file` is not a write and has no `McpWrite` policy on the method or the type; and
        `CarTrackerToolCatalogue.ToolTypes` equals every `[McpServerToolType]` class in the MCP assembly (assumed:
        A4). In `tests/CarTracker.Chat.Tests`, the existing drift tests must pass with the new tool, plus one
        assertion that `offer_to_file`'s schema asks for exactly `vehicle`, `type`, `title`, `documentDate`,
        `serviceRecordId`, `expenseEntryId`, `issueId` and `attachment`, with `type` and `title` required and no
        `AppOptions` or `DocumentService` property
  - [x] 1.2 Bump `VERSION` minor in this task's commit (`./scripts/release.ps1 -Minor` or by hand): 0.31.0 to
        0.32.0 if the two earlier specs in this batch have bumped to 0.30.0 and 0.31.0, otherwise the next minor
        from what the file holds
  - [x] 1.3 `AppOptions` in `CarTracker.Domain` with `PublicOrigin` and a normalised reader that trims trailing
        slashes and returns null for blank; a blank default registered with `TryAddSingleton` in
        `AddCarTrackerDomain`; `Program.cs` binds the `App` section and registers the bound instance first, beside
        `LegalOptions` (assumed: A3)
  - [x] 1.4 `FilingOffer` and `FilingAllowance` records in `CarTracker.Shared/Logs/FilingDtos.cs`, with
        `LinkedTo` as `DocumentLink?` (assumed: A8); static `FilingLink` in `CarTracker.Domain/Documents` that
        builds `path` with `Uri.EscapeDataString` and `url` from `AppOptions`. It never builds the vehicle slug
  - [x] 1.5 `DocumentService.ProposeFilingAsync(VehicleRef, type, title, documentDate, serviceRecordId,
        expenseEntryId, issueId, attachment, publicOrigin)` returning `WriteResult<FilingOffer>`: trim and check
        the title (blank, over 160), reuse `ValidateLinkAsync`, describe the link with `DescribeLinkAsync`
        refactored to take the three ids (assumed: A9), read `CapacityAsync`, and save nothing (assumed: A10)
  - [x] 1.6 New `[McpServerToolType]` `FilingTools` in `CarTracker.ModelContextProtocol/Tools` with
        `[McpServerTool(Name = "offer_to_file")]`, the parameters in api-spec.md, and the description from
        technical-spec.md (call for every shared photo or document; it cannot upload and returns a link; a car
        photo that is not a paper or issue evidence is `CarPhoto`; damage or wear for a known issue is
        `ConditionPhoto` linked to it; after a write, call it once the write succeeds and link the new record).
        Resolve the vehicle with `ToolHelpers.ResolveVehicleAsync`, map the result with `ToolHelpers.ToResult`,
        and write the summary as "Ready to file as Parts receipt, linked to <label>. Open: <url>", or "open
        Cambelt and go to <path>" when `url` is null. Add the type to `CarTrackerToolCatalogue.ToolTypes` and
        `.WithTools<FilingTools>(ToolSerializerOptions)` in `McpServerRegistration`. Do not add it to
        `McpToolClassification.WriteToolNames` (assumed: A1)
  - [x] 1.7 `dotnet build` and confirm `api-contract/v1.json` is unchanged (the tool is not a REST endpoint)
  - [x] 1.8 Verify all tests pass (`dotnet test`; `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)

- [ ] 2. **`filing_offer` on the chat stream and the prompt rules** (assumed: A1, A5, A7)
  - [ ] 2.1 Write tests. Unit tests in `tests/CarTracker.Chat.Tests/SystemPromptTests.cs`: the prompt is still a
        compile-time constant and names `offer_to_file`, one call per attachment, calling it after the owner
        confirms a drafted write and linking the new record, no link when the write was declined, `CarPhoto`
        as the fallback, and never saying a file has been filed. Unit tests in
        `tests/CarTracker.Chat.Tests/ChatStreamJsonTests.cs`: a `FilingOffer` serialises to one line, camelCase,
        with `"type":"PartsReceipt"`, `"kind":"Paper"`, `"group":"WorkAndParts"` and `"kind":"Expense"` in
        `linkedTo` as strings (assumed: A7).
        Integration tests (Testcontainers, scripted model) in `tests/CarTracker.Data.Tests/FilingStreamTests.cs`,
        linking `ScriptedChatClient.cs` and `FakeBudget.cs` from Chat.Tests into the project (assumed: A7), with
        the request's provider pinned to the owner as `ChatToolScopeTests` does: a turn calling `offer_to_file`
        yields one `ChatFilingOfferEvent` whose offer equals the tool's `Data`, after the `tool` running event and
        before `done`; a refused call (another owner's issue id) yields no `ChatFilingOfferEvent` and the model's
        next request carries the refusal text; a turn calling `list_vehicles` yields none; a response carrying
        `log_expense` and `offer_to_file` together suspends on the write only, and `StreamResumeAsync` with the
        write approved yields the swept offer's `ChatFilingOfferEvent` and its `tool` narration (assumed: A5); the
        expense row exists only after the confirm, as today
  - [ ] 2.2 `ChatFilingOfferEvent(FilingOffer Offer)` in `ChatStreamEvent.cs`, documented as narration built from a
        tool result and never an authorisation. In `ChatConversationService.StreamAsync`, seed the call-name map
        from the `FunctionCallContent`s already in `messages`, and on a `FunctionResultContent` for
        `offer_to_file` read the offer from the typed `McpResult<FilingOffer>` or its serialised `JsonElement`
        `data`; a string result emits nothing (assumed: A5)
  - [ ] 2.3 `ChatStreamJson.Options` in `CarTracker.Chat` (web defaults, `JsonStringEnumConverter`, not indented).
        `ChatEndpoints.WriteAsync` emits `event: filing_offer` with the offer as `data` through `EmitAsync` and
        these options, beside the existing `tool` event (assumed: A7)
  - [ ] 2.4 `ChatSystemPrompt.Text` gains the rule set from technical-spec.md under "Reading files", worded to
        match the tool description, and `ChatFiles`' remarks replace "Filing a certificate is a separate,
        deliberate act on the documents screen" with one line saying filing is a separate upload from the
        browser and nothing here changes
  - [ ] 2.5 Verify all tests pass (`dotnet test`; `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)

- [ ] 3. **Filing cards in the in-app chat** (assumed: A1, A6, A8, A11)
  - [ ] 3.1 Write component tests, stubbing `createImageBitmap`, `HTMLCanvasElement.prototype.toBlob` and the chat
        stream as `ChatPanel.test.tsx` and the gallery's `documents.test.ts` do, with `/api/document-types`
        mocked. New `src/chat/FilingCard.test.tsx` and additions to `src/chat/ChatPanel.test.tsx`: sending a
        message with a JPEG and a PDF renders two cards before any stream event, "Car photo" and "Other" (labels
        from the mocked document types), titled from the file names without extensions, dated from each file's
        `lastModified` day, marked as a default suggestion, and targeting the panel's vehicle, or the garage's
        default vehicle when the panel has none (assumed: A11); a `filing_offer` with `attachment: 2` changes the
        second card's category, title, date and link chip and marks it as the assistant's suggestion, and leaves
        the first unchanged; an offer with no `attachment` fills the first card without one; an offer for an
        out-of-range position, or for a filed or dismissed card, changes nothing; a `filing_offer` arriving on the
        `/confirm` stream refines the card of the latest message with files; File posts multipart to the
        documents endpoint with the original `File` (not the prepared chat JPEG), the rendition parts, `type`,
        `title`, `documentDate` and the offer's link id, asserted on the request, then shows "Filed" with a link
        built by `hrefFor` to Photos or Documents by kind; a duplicate refusal shows the existing title; an
        allowance refusal shows the plan message; "N left" shows when the offer's allowance has under ten
        remaining; Edit opens `UploadSheet` with the card's type, title, date, link and file; Not now removes the
        card; across send, offer, File and Not now, `Storage.prototype.setItem` never receives attachment bytes
        or file names, and `lib/clientStorage.ts`'s registry test still passes; an axe sweep of the panel with
        two cards
  - [ ] 3.2 `api/chat.ts`: a `FilingOffer` type whose `type`, `kind`, `group` and `linkedTo` come from
        `components['schemas']` (assumed: A8), and `{ type: 'filing_offer'; offer: FilingOffer }` in `ChatEvent`,
        parsed from the frame's `data`
  - [ ] 3.3 `ChatPanel` keeps each picked `File` beside the prepared `ChatFile` and passes both to `send`; a file
        `prepare` refuses gets no card
  - [ ] 3.4 `useChat`: originals in a `useRef` map keyed by the "you" entry id, never in state that is persisted
        or in storage; one card per file on send, with the defaults; `consume` applies `filing_offer` to the
        latest entry with files; originals dropped on File, Not now and unmount (assumed: A11)
  - [ ] 3.5 New `src/chat/FilingCard.tsx`: category label, title, date, link chip, the default or assistant
        marker, the remaining allowance under ten, and File (through the gallery's `uploadDocument`), Edit
        (through its `UploadSheet` with `initial`) and Not now. Styles in `components.css`, no `style`
        attributes. File never runs without the owner's press (assumed: A1)
  - [ ] 3.6 Verify all tests pass (`NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`; `dotnet test`)

- [ ] 4. **`/file` route, sign-in return path and pre-filled upload sheets** (assumed: A1, A2, A6, A12)
  - [ ] 4.1 Write component and unit tests. New `src/screens/FileLinkPage.test.tsx` with a mocked garage and
        `/api/document-types`: `vehicle=bt53akj`, `BT53 AKJ` and `BT53AKJ` resolve to the same car; a
        `CarPhoto` link replaces history with `/bt53akj/photos?file=1&type=CarPhoto&title=...&date=...`; a
        `PartsReceipt` link with `expenseEntryId` goes to `/bt53akj/documents?file=1&...` carrying the id; an
        unknown type goes to Documents with `Other`; a vehicle outside the garage shows "This link is for a
        vehicle that isn't in your garage" with a link to the garage and navigates nowhere; a `vehicle` of
        `../admin`, `//evil.example` or `https://evil.example` never navigates outside the app (assumed: A1); the
        server-built path kept in 1.1 parses back to the same title. `src/routes.gating.test.tsx`: `/file` is
        gated and `PUBLIC_PATHS` is unchanged. New `src/lib/returnTo.test.ts` and additions to
        `src/auth/AuthGate.test.tsx`: log in and sign up from `/file?vehicle=...` pass
        `appState.returnTo` with the path and query; the redirect callback restores a safe path and sends
        `//evil.example`, `/\evil.example`, `https://evil.example` and `javascript:` to `/` (assumed: A2).
        `PhotosPage.test.tsx` and `DocumentsPage.test.tsx`: `?file=1&...` opens the upload sheet pre-filled with
        type, title, date and a link chip reading "Linked to an expense" (assumed: A12); closing it removes the
        query with a replace navigation; no `file=1` opens nothing; choosing a file and pressing Upload posts the
        link id, asserted on the request
  - [ ] 4.2 Route `{ path: 'file', element: <FileLinkPage /> }` inside the `AuthGate` branch of `routes.tsx`,
        a sibling of `account` and `admin`, not in `SCREEN_IDS`. New `src/screens/FileLinkPage.tsx`: match the
        registration with case and spaces ignored, read the kind with the gallery's `kindOf`, then
        `navigate(hrefFor(kind === 'Photo' ? 'photos' : 'documents', reg, prefill), { replace: true })`
  - [ ] 4.3 New `src/lib/returnTo.ts` (`returnToFrom(location)` and `safeReturnTo(value)`); `AuthGate`'s
        `loginWithRedirect` calls pass `appState: { returnTo }`; `main.tsx`'s `onRedirectCallback` replaces
        history with `safeReturnTo(appState?.returnTo)` (assumed: A2)
  - [ ] 4.4 New `src/screens/documents/useFilePrefill.ts`: reads `file=1` and the prefill keys, returns the
        `UploadSheet` `initial` (link chip label per kind, assumed: A12) and a `clear()` that removes the query
        with `replace`. Use it in `PhotosPage` and `DocumentsPage`
  - [ ] 4.5 Verify all tests pass (`NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`; `dotnet test`)

- [ ] 5. **`App:PublicOrigin` on the admin panel, deploy files and docs** (assumed: A3, A6, A13)
  - [ ] 5.1 Write tests. `src/screens/AdminPage.test.tsx`: the Deployment panel shows a "Filing links" row with
        the origin when `publicOrigin` is set, and "path only: App:PublicOrigin is not set" when it is null; axe
        sweep unchanged. `src/api/contract.test.ts`: `AdminUsageResponse` has `publicOrigin` (assumed: A13)
  - [ ] 5.2 `AdminUsageResponse` gains `PublicOrigin` (`string?`) from `AppOptions`; `dotnet build`,
        `npm run gen:api`, and confirm the contract diff is additive
  - [ ] 5.3 `DeploymentPanel.tsx`: a `DerivedRow` for filing links
  - [ ] 5.4 `App__PublicOrigin: ${APP_PUBLIC_ORIGIN:-}` in `deploy/docker-compose.yml` and
        `deploy/docker-compose.nas.yml`, with a comment stating that blank means path-only links; the variable in
        `deploy/.env.example`; `docs/deployment-shared-host.md` names the key, its value for cambelt.app, and that
        the host's compose copy must be updated before links become absolute (assumed: A3)
  - [ ] 5.5 Docs: in `docs/mcp-connect.md`, a section on filing photos from an MCP client (the tool returns a
        link, the owner picks the same photo and saves) and `App:PublicOrigin`, with `offer_to_file` in the read
        table and the read count moved from 19 to 20; an entry for this spec in `docs/product/roadmap.md`; one
        `docs/engineering-log.md` entry. Check that `git grep -nP '\x{2014}' -- '*.md'` stays empty
  - [ ] 5.6 By hand against the AppHost with `Chat:ApiKey` set and a read-scope assistant token (assumed: A4, A6):
        deliverable 1 (a parts receipt photo, confirm the expense, the card proposes "Parts receipt" linked to it,
        File puts it on that expense's "Papers & photos" strip); deliverable 2 (a plain car photo with no
        request gets a "Car photo" card and File adds it to Photos); deliverable 3 (an MOT certificate photo
        shared over MCP returns a link, opened signed out it survives sign-in and opens Documents with the sheet
        set to MOT, titled and dated, and saving files it); and `offer_to_file` answers a read-scope token while
        a write tool refuses it
  - [ ] 5.7 Verify all tests pass: the full suite once (`dotnet test`;
        `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)
