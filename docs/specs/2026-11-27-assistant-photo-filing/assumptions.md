# Assumptions

## A1 · open · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** spec, 1.6, 2.2, 3.4, 3.5, 4.1, 4.2
- **Question:** Filing a photo from the chat writes a `documents` row without going through `PendingWriteStore`, and `offer_to_file` runs for read-scope MCP tokens with no audit row. A filing link can be made by anyone, not only by the server. Does this keep "chat writes need a server-held confirmation id" and "MCP writes need `McpWrite` and are audited" true?
- **Assumed:** Yes, as the spec frames it. `offer_to_file` is a read: it is not in `McpToolClassification.WriteToolNames`, carries no `[Authorize(Policy = "McpWrite")]`, calls no `SaveChanges`, and so is neither approval-required in the chat nor audited on `/mcp`. The only write is the owner's own authenticated `POST /api/vehicles/{registration}/documents`, sent when they press File on a card or Upload on a sheet, and that endpoint validates the type, the link id (against the owner-filtered vehicle) and the allowance again. A card is filled only from a `filing_offer` frame the server built from the tool's own result, never from model prose or the client-held transcript, and even then it is a pre-fill the owner reviews. A `/file` link authorises nothing: it only navigates inside the app through `hrefFor`, never to a URL taken from the query, and a vehicle outside the signed-in garage is refused, not swapped for the default one. A crafted link can at most pre-fill a misleading title or category that the owner sees before saving.
- **Why:** The spec says so ("The link authorises nothing", "`offer_to_file` writes nothing, so it needs no confirmation and no audit row"), and the confirmation rule exists to stop the model writing on its own, which this design cannot do: the bytes only exist in the owner's browser or camera roll.
- **Touches:** src/CarTracker.ModelContextProtocol/Tools/FilingTools.cs, src/CarTracker.ModelContextProtocol/McpToolClassification.cs, src/CarTracker.Chat/ChatConversationService.cs, src/CarTracker.WebApp/src/chat/FilingCard.tsx, src/CarTracker.WebApp/src/screens/FileLinkPage.tsx
- **If wrong:** To audit offers, add `offer_to_file` to a separate "audited read" list read by `McpAuditFilter` (small). To require `McpWrite`, move the tool under the policy and the write list, which also makes it approval-required in the chat and so suspends every turn that offers (medium, and it defeats "by default").

## A2 · open · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.1, 4.3
- **Question:** A filing link opened while signed out goes through Auth0 and comes back to `/`: `main.tsx` sets `redirect_uri` to the origin and `onRedirectCallback` replaces history with the current pathname, and `AuthGate` calls `loginWithRedirect()` with no `appState`. The query is lost, so the link does nothing for the owner it was made for. The spec does not cover sign-in.
- **Assumed:** Preserve the return path for every gated route, not only `/file`. `AuthGate`'s log-in and sign-up calls pass `appState: { returnTo: location.pathname + location.search }`; `onRedirectCallback` restores it through a new `lib/returnTo.ts` guard that accepts only a same-origin relative path (starts with one `/`, not `//` or `/\`, no scheme, parsed against the current origin and compared) and otherwise falls back to `/`. `redirect_uri` stays the origin, so no Auth0 tenant change is needed.
- **Why:** Without it, deliverable 3 fails on a phone that is not already signed in, which is the common case for a link opened from Claude. A general fix is the same size as a `/file`-only one, and the guard closes the open-redirect that an unchecked `returnTo` would add.
- **Touches:** src/CarTracker.WebApp/src/auth/AuthGate.tsx, src/CarTracker.WebApp/src/main.tsx, src/CarTracker.WebApp/src/lib/returnTo.ts, src/CarTracker.WebApp/src/auth/AuthGate.test.tsx
- **If wrong:** Limit `returnTo` to paths starting `/file?` (one condition, small), or drop it and tell owners to sign in first in `docs/mcp-connect.md` (small, and deliverable 3 then needs a signed-in phone).

## A3 · open · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.1, 1.3, 5.4
- **Question:** How is `App:PublicOrigin` bound and validated, which deploy files carry it, and how does the tool reach it without the value leaking into the tool's schema?
- **Assumed:** A new `AppOptions { PublicOrigin }` in `CarTracker.Domain`, bound from the `App` section in `Program.cs` beside `LegalOptions`. Blank or whitespace means path-only links. Trailing slashes are trimmed when read; nothing else is validated, so a malformed origin shows up on the admin panel (5.3) rather than failing boot. `AddCarTrackerDomain` registers a blank default with `TryAddSingleton`, and `Program.cs` registers the bound instance first, so `TestCatalogue.Services` resolves it as a service and the catalogue never publishes it as an argument. The key goes in `deploy/.env.example`, `deploy/docker-compose.yml` and `deploy/docker-compose.nas.yml` (the two compose files that already carry `Legal__*`), as `App__PublicOrigin: ${APP_PUBLIC_ORIGIN:-}` with its polarity stated where it is set. The shared host's compose copy has to be updated by hand before links become absolute there.
- **Why:** Matches how every other options class is bound, the CLAUDE.md polarity rule, and the "service parameter becomes a published argument" trap the drift test guards.
- **Touches:** src/CarTracker.Domain/AppOptions.cs, src/CarTracker.Domain/ServiceCollectionExtensions.cs, src/CarTracker.WebApi/Program.cs, deploy/.env.example, deploy/docker-compose.yml, deploy/docker-compose.nas.yml, docs/deployment-shared-host.md
- **If wrong:** Reject a value that is not an absolute `https` URI at boot (small), or derive the origin from the request host instead of configuration (small, but behind the gateway the forwarded host must then be trusted).

## A4 · open · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.1, 5.6
- **Question:** The spec's test list asks that "a read-scope `AssistantToken` can call it". There is no `CarTracker.WebApi.Tests` project and no `/mcp` HTTP harness, so no test can present a token.
- **Assumed:** Prove it from the two facts `/mcp` enforces with: `McpToolClassificationTests` (Data.Tests, no database) gains `FilingTools` in its tool-type list and asserts `offer_to_file` is not a write and carries no `McpWrite` policy on the method or the type, and a new assertion that `CarTrackerToolCatalogue.ToolTypes` equals every `[McpServerToolType]` class in the assembly, so a tool class left out of the catalogue fails. The `/mcp` route is gated by `McpRead` only, so a read-scope token reaches every tool without the write policy. A by-hand call with a read-scope token runs in 5.6.
- **Why:** Same decision as the gallery spec's A6 and clear-vehicle-fields' A4: no new test project inside a feature.
- **Touches:** tests/CarTracker.Data.Tests/McpToolClassificationTests.cs
- **If wrong:** Add a `WebApplicationFactory` test project with a minted read-scope token (medium).

## A5 · open · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.1, 2.2
- **Question:** The spec says the stream emits `filing_offer` "when an `offer_to_file` call completes". `ChatConversationService.StreamAsync` names a result by looking up the call id in calls seen in the same stream. When a read shares a response with a write it is swept into the approval protocol and runs on the `/confirm` resume, where its call is in the transcript and not in the stream, so neither the `tool` narration nor a `filing_offer` would fire. The result's runtime shape (typed record, `JsonElement`, or the refusal string `GuardedTool` returns) is also not pinned.
- **Assumed:** `StreamAsync` seeds its call-name map from the `FunctionCallContent`s already in the incoming transcript, which also fixes the narration for swept reads. A `filing_offer` is emitted only when the result is a successful offer: the payload is read from the typed `McpResult<FilingOffer>` or its serialised `JsonElement` (`data`), and a string result (a refusal or a fault explanation) emits nothing. Tests drive the real catalogue wrapper rather than a hand-built result.
- **Why:** The system prompt tells the model to offer after a confirmed write, which is exactly the resume path; a model that ignores that and offers in the same response should still get its card refined.
- **Touches:** src/CarTracker.Chat/ChatConversationService.cs, src/CarTracker.Chat/ChatStreamEvent.cs, tests/CarTracker.Data.Tests/FilingStreamTests.cs
- **If wrong:** Drop the seeding and accept that a swept offer leaves the default card in place (small).

## A6 · open · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.1, 4.1, 5.1, 5.6
- **Question:** With `tests=auto` and acceptance criteria about UI, which UI layer? The three deliverables also need a live model: the in-app chat (Chat:ApiKey) and an MCP client.
- **Assumed:** Vitest + Testing Library component tests with axe sweeps, the project's existing tool, as the gallery spec's A9 decided. No Playwright. The model is scripted on the server side (Data.Tests) and the stream is mocked on the client side. Deliverables 1 to 3 are checked by hand against the AppHost in 5.6, with a real chat key and a read-scope token from an MCP client, and the execute run reports them as criteria checked manually.
- **Why:** Adding a browser test framework is its own decision, and no automated test here can run a real model turn.
- **Touches:** src/CarTracker.WebApp/src/**/*.test.ts(x)
- **If wrong:** Add Playwright with one flow: send a photo with a mocked stream, press File, assert the upload request (medium).

## A7 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.1, 2.3
- **Question:** The spec puts the `filing_offer` stream test under "Chat", but `CarTracker.Chat.Tests` has no database (its `TestCatalogue` context points at `Database=none`), so `offer_to_file` would fail there. And `ChatEndpoints`' frame serialiser is web defaults with no enum converter, which would send `"type": 13`.
- **Assumed:** The stream tests live in `tests/CarTracker.Data.Tests/FilingStreamTests.cs` against real PostgreSQL, reusing `ScriptedChatClient` and `FakeBudget` by linking the two files from `CarTracker.Chat.Tests` (`<Compile Include ... Link=...>`). Prompt tests stay in `SystemPromptTests`. The frame is serialised with a new `ChatStreamJson.Options` in `CarTracker.Chat` (web defaults, `JsonStringEnumConverter`, compact), unit-tested in Chat.Tests, and `ChatEndpoints` uses it for `filing_offer`.
- **Why:** The tool needs a database to run, and the endpoint has no test project, so the serialiser has to live where a test can reach it.
- **Touches:** tests/CarTracker.Data.Tests/CarTracker.Data.Tests.csproj, tests/CarTracker.Data.Tests/FilingStreamTests.cs, src/CarTracker.Chat/ChatStreamJson.cs, tests/CarTracker.Chat.Tests/ChatStreamJsonTests.cs
- **If wrong:** Move `ScriptedChatClient` into a shared test-support project (small).

## A8 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.4, 3.2
- **Question:** Where does `FilingOffer` live, and how does the web client type a payload that is not in `api-contract/v1.json` (it is a tool result and an SSE frame, not a REST response)?
- **Assumed:** `FilingOffer` and `FilingAllowance` are records in `CarTracker.Shared/Logs/` beside `DocumentDtos.cs`, with `LinkedTo` typed as the existing `DocumentLink`. The TS `FilingOffer` is declared by hand in `api/chat.ts`, but every enum and nested shape in it comes from the generated contract: `type: components['schemas']['DocumentType']`, `kind: ...['DocumentKind']`, `group: ...['DocumentGroup']`, `linkedTo: ...['DocumentLink'] | null`, so a renamed member fails `tsc -b`.
- **Why:** Follows the "source values, never guess them" rule without inventing a REST endpoint to get a schema into the contract.
- **Touches:** src/CarTracker.Shared/Logs/FilingDtos.cs, src/CarTracker.WebApp/src/api/chat.ts
- **If wrong:** Add the record to the OpenAPI document with a schema transformer and generate the type (small).

## A9 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.5
- **Question:** api-spec.md's example labels the linked expense "14 Nov 2026 · Euro Car Parts · £63.20", but the spec also says `LinkedTo` is "the same `DocumentLink` shape documents use", and `DocumentService.DescribeLinkAsync` labels an expense "{Category} · £{Amount}".
- **Assumed:** Reuse `DescribeLinkAsync` (refactored to take the three ids) so a card's chip and the filed document's chip read the same. The example is illustrative.
- **Why:** One labelling rule, and the chip the owner sees after filing matches the one they saw before.
- **Touches:** src/CarTracker.Domain/Documents/DocumentService.cs
- **If wrong:** Change the expense label in `DescribeLinkAsync`, which also changes the Documents screen (small).

## A10 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.1, 1.5, 1.6
- **Question:** Cases the spec does not list: a blank title, an account already at its document allowance, and `attachment` sent by an MCP client.
- **Assumed:** A blank title is refused like a long one. A full allowance still returns the offer (with `used` equal to `limit`), and the summary adds that a document must be removed before this one can be filed. `attachment` is echoed unchanged on both surfaces and only the chat client reads it. `title` is trimmed before the length check.
- **Why:** A refusal at full allowance would hide the link the owner needs once they have made room, and the upload refuses anyway.
- **Touches:** src/CarTracker.Domain/Documents/DocumentService.cs, src/CarTracker.ModelContextProtocol/Tools/FilingTools.cs
- **If wrong:** Refuse the offer when the allowance is full (one branch, small).

## A11 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.1, 3.4, 3.5
- **Question:** The spec drops the originals "when the conversation is cleared", but `useChat` has no clear action. It also does not say which vehicle a default card files to when the chat is at garage level (`vehicle` is null), what happens to cards when the send fails, which offers are ignored, or where the allowance comes from before any offer.
- **Assumed:** No clear action is added; originals are dropped on File, on Not now and when the panel unmounts. A default card files to the panel's vehicle, or to the garage's default vehicle when the panel has none, and shows that registration; an offer's `registration` replaces it. Cards stay when the send fails, because filing does not depend on the model. An offer whose `attachment` is out of range, or that targets a filed or dismissed card, is ignored. The remaining allowance is shown only once an offer has carried it; before that, the upload's own refusal message covers a full account.
- **Why:** The smallest behaviour that keeps "every attachment gets a card" true without new endpoints.
- **Touches:** src/CarTracker.WebApp/src/chat/useChat.ts, src/CarTracker.WebApp/src/chat/FilingCard.tsx
- **If wrong:** Add a Clear control that also drops the originals, or fetch the allowance for default cards from authenticated meta (small each).

## A12 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.4
- **Question:** The upload sheet shows a preset link as a read-only chip with a label (gallery A13), but a `/file` link carries only the link id, not its label.
- **Assumed:** Arriving from `/file`, the chip reads a fixed phrase per kind ("Linked to an expense", "Linked to a service record", "Linked to an issue"). The link query keeps the spec's fields; no label is added to it.
- **Why:** The spec fixes the query, and the upload revalidates the id anyway.
- **Touches:** src/CarTracker.WebApp/src/screens/documents/UploadSheet.tsx, src/CarTracker.WebApp/src/screens/documents/useFilePrefill.ts
- **If wrong:** Look the label up from the vehicle's expense, service or issue list (small), or add a display-only `label` to the query (small).

## A13 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 5.1, 5.2, 5.3
- **Question:** "Shown on the admin Deployment panel with the other resolved values." The panel reads `GET /api/admin/usage` (`AdminUsageResponse`), which carries the chat ceilings and `chatConfigured`.
- **Assumed:** `AdminUsageResponse` gains `PublicOrigin` (`string?`, null when blank), an additive contract change, and the panel adds a `DerivedRow` "Filing links" reading the origin, or "path only: App:PublicOrigin is not set" when null.
- **Why:** That response is already the panel's list of resolved deployment values.
- **Touches:** src/CarTracker.WebApi/Endpoints/AdminEndpoints.cs, src/CarTracker.WebApp/src/screens/admin/DeploymentPanel.tsx, src/CarTracker.WebApp/src/screens/AdminPage.test.tsx
- **If wrong:** Move it to its own small admin endpoint (small).
