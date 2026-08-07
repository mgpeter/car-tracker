# Spec Requirements Document

> Spec: In-App Chat Assistant — conversational access and photo-to-record drafting
> Created: 2026-08-06
> Status: Planning

## Overview

Put the assistant inside the app: a chat icon in the shell that opens a side panel on desktop and a dedicated
screen on mobile, backed by a server-side Claude conversation that calls the **same tools the MCP server
already exposes**. Files are its distinguishing input — an MOT certificate, a fuel receipt, an odometer shot,
an insurance schedule PDF — which the model **identifies and reads**, turning into a filled-in draft of the
appropriate write, presented for the owner to check and confirm before anything is saved.

**This is now the only file-to-record path in the app.** It absorbs `2026-07-16-receipt-photo-capture`, which
proposed a camera input on the add-expense sheet where the owner read the photo and typed the figures by
hand. That spec named two routes to real extraction and deferred both; this is the second of them — *"the MCP
assistant reading the attached photo … the assistant already can log expenses; reading a receipt it can see is
a natural extension"* — so it is deleted rather than built. Manual transcription is not wanted.

## User Stories

### Ask the car a question, from the car

As the owner, I want to ask "what needs my attention?" in the app and get the same answer the dashboard shows,
so that I don't have to know which of seventeen screens holds the figure I want.

This is Phase 4's promise pointed at the web UI instead of at Claude Desktop. `docs/mcp-connect.md` describes
connecting an *external* client over Streamable HTTP; this spec is the *internal* one, and it is the reason
`tech-stack.md` has always said the Agent Framework "is a candidate for the future in-app chat that would
*consume* these tools". The read tools already cover every screen (`get_due_items` first, then
`get_vehicle_summary`, `get_fuel_status`, `get_spend_summary`, `get_check_status`, `get_budget`,
`get_data_integrity`, the `list_*` set), and every one of them calls `IDerivedMetricsService` — so the chat
and the dashboard cannot disagree, by construction rather than by discipline. **Reads run without asking.**
Nothing is written, nothing can be silently wrong, and a confirmation step on "what's my MPG?" would be
friction with no safety value.

### Photograph the paperwork instead of transcribing it

As the owner, I want to photograph the MOT certificate and have the service record drafted for me, so that
filing it is a glance and a tap rather than eight fields typed on a phone at a garage forecourt.

The model reads the file and proposes the write: an MOT pass becomes `add_service` with `type = "MOT"`, its
date, the odometer and the garage filled in; a fuel receipt plus an odometer photo becomes `log_fuel_fillup`
with litres, price per litre, total and mileage. **The draft is never saved on the model's say-so.** It
renders as an editable card — every field visible, every field correctable — and only the owner's tap runs
the tool.

The governing rule, inherited from the spec this one replaces: **a wrong auto-filled amount silently entered
is worse than a field the owner typed.** That is why the earlier spec refused to promise extraction at all.
This spec makes the extraction real and keeps the human in the loop, so the risk that rule guards against
never materialises — the model proposes, the owner disposes, and nothing reaches a table in between.

The file itself is **not stored**. It travels browser → API → Claude, produces a draft, and is discarded.
Filing evidence is the Documents feature's job and stays there — building a second, half-formed upload path
here would leave two to keep in step.

> **Consequence, stated plainly:** there is no longer any single action that both logs an expense from a
> receipt *and* keeps the receipt. The receipt spec offered that pairing; this one deliberately does not.
> Filing a document against an expense remains the Documents screen's job through the `ExpenseEntryId` link
> that `Document` already carries. This is a chosen scope reduction, not an oversight — one upload path that
> stores nothing is easier to reason about than two that disagree about what durable means.

### Drop the file in and let it work out what it is

As the owner, I want to hand over whatever the garage or the insurer gave me without first classifying it
myself, so that filing is one action rather than a decision followed by an action.

This is what Claude Desktop already does against this project's MCP server: you attach a document, say
something vague or nothing at all, and it works out what it is looking at and calls the right tool. The
in-app assistant should not be worse at this than the external client hitting the same tools.

Four rules make it safe:

- **Each file is classified independently.** One message may legitimately carry an MOT certificate, a fuel
  receipt and an odometer shot, and produce more than one draft — the API spec already contemplates a message
  yielding two writes.
- **The model states its reading before it drafts.** "This looks like an MOT pass for BT53 AKJ, tested 8 Jul
  2026 at 80,705 miles." The classification is *visible*, not implied by which card happens to appear. An
  owner who can see the conclusion can catch a wrong one; an owner shown only a pre-filled form cannot tell a
  misread receipt from a misclassified one.
- **Unrecognised, or not a vehicle document at all, means say so and draft nothing.** A draft is itself a
  claim about what the file is. A confident wrong one costs the owner a correction they did not ask for,
  which is worse than an honest "I can't tell what this is."
- **Ambiguity is a question, not a guess** — "is this a receipt to log, or a service invoice to file against a
  record?" This is the same instinct as the rest of the app: flag, never act on the owner's behalf.

No instruction is required, and none is refused: "here's my MOT certificate" and a bare attachment take the
same path, the first simply skipping a step the model would otherwise infer.

### One brain, whichever surface

As the owner, I want the in-app chat and the desktop MCP client to behave identically, so that "log the fill"
means the same thing whether I say it in the app or to Claude on my laptop.

The chat backend resolves and invokes the **same `[McpServerTool]` methods** from the same DI container — no
second catalogue, no reimplementation, no HTTP hop. A tool added to `CarTracker.ModelContextProtocol` appears
in the chat automatically; a fix to `LogWriteService` fixes both surfaces at once. Writes stamp
`EntrySource.Chat`, so the log can still say which surface a row came from.

## Spec Scope

1. **Chat backend** — a `CarTracker.Chat` conversation service in the WebApi that calls the Anthropic API
   (`claude-opus-5`, vision + tool use, streamed), generating tool schemas from the existing
   `[McpServerTool]`/`[Description]` attributes and invoking those same methods in-process.
2. **Read-now / confirm-to-write loop** — read tools execute automatically inside the turn; a *write* tool
   suspends the loop and returns the proposed call to the client as a draft. The client renders it, the owner
   edits or confirms, and a second request executes the tool and resumes the conversation.
3. **File input** — the chat sheet accepts camera capture or file upload, multiple per message, and never
   persists them. **Images** (JPEG/PNG/WebP) go as image content blocks, client-side downscaled to cap the
   long edge and the request size; **HEIC from iOS is converted to JPEG in the browser**, because the API
   does not accept it and a rejection at the far end reads as "the assistant ignored my photo". **PDFs** go
   as document content blocks under a page cap — an MOT certificate or insurance schedule usually arrives
   emailed, and a camera never sees it.
4. **Classification** — an unlabelled file is identified by the model, which states its reading before
   drafting, declines to draft on anything it cannot place, and asks rather than guesses when a file could be
   two things. Per file, so a mixed message resolves each part on its own.
5. **Shell surface** — a chat icon in `TopNav` (desktop) and `BottomNav` (mobile) opening a docked side panel
   above 900px and the `/:reg/assistant` route below it, using the same breakpoint the two navs already split
   on. Vehicle-scoped, so tools default to the car you are looking at.
6. **`EntrySource.Chat`** — a fifth attribution value and the migration that widens the
   `ck_<table>_source` check constraint on every auditable table, so a chat-drafted row is distinguishable
   from a web-typed or MCP-written one.
7. **The Fuel refusal holds** — inherited from the absorbed spec. A photographed fuel receipt cannot become a
   typed `Fuel` expense: that category is `IsMirrorOnly` and refused, because a hand-entered fuel figure is
   the workbook's lumped "fuel to date" row and that is the £163.16 gap. `log_expense` already routes through
   the service that enforces this, so it is an invariant to assert with a test, not code to write. A fuel
   receipt's figures belong to `log_fuel_fillup`, which mirrors into expenses by itself.

## Out of Scope

- **Storing the file.** No `Document` row, no volume write, no `Sha256`. The Documents feature owns upload,
  storage and attaching a document to a service record, expense or issue through the three link FKs
  `Document` already carries. This spec reads a file and forgets it. The write request carries a nullable
  `documentId` that nothing sets today, so the two halves can be joined later without a contract change —
  but joining them is not this spec's work, and until someone does it, logging from a receipt and keeping the
  receipt are two separate actions.
- **Persisted conversation history.** The Messages API is stateless and so is this endpoint: the client holds
  the transcript and sends it back each turn. No `chat_messages` table, no schema, no retention question.
  Reloading the page starts a new conversation, which is the honest behaviour for a v1.
- **Edit and delete via chat.** The write catalogue stays add/log + safe-updates, exactly as DEC-014 settled
  it for MCP. The chat inherits that boundary rather than widening it, even though the confirm step would
  arguably make deletion safe — one surface should not quietly hold more power than the other.
- **Unattended or background chat.** No scheduled prompts, no assistant-initiated messages, no relationship to
  `RemindersBackgroundService`. The reminder badge already surfaces what needs attention; the chat is
  something you open.
- **Voice input.** A microphone is a different capture pipeline (and a different permission prompt) from a
  camera, and nothing in the stories needs it.
- **The `AssistantWriteAudit` trail.** It is keyed to an `AssistantToken`, and a chat write has no token — the
  owner is authenticated by Auth0 instead. Extending the audit to a nullable-token, surface-tagged trail is a
  real follow-on; `EntrySource.Chat` on the row is this spec's attribution. See the technical spec.
- **A second tool catalogue or a chat-only tool.** If the chat needs a capability, it becomes an MCP tool and
  both surfaces get it.

## Expected Deliverable

1. Opening the chat from BT53's dashboard and asking "what needs my attention?" returns the same items the
   dashboard's attention panel shows — because both called `IDerivedMetricsService` — with no confirmation
   prompt and nothing written.
2. Photographing an MOT pass certificate produces an editable draft card naming `add_service` with `type`,
   `serviceDate`, `mileage` and `garage` filled from the image; correcting a misread mileage and tapping Save
   creates the service record (plus its mileage reading and mirrored expense, via `ServiceRecordFactory`)
   stamped `EntrySource.Chat`, and the row appears on the service-history screen. Tapping Discard writes
   nothing.
3. **Attaching an MOT certificate PDF and an odometer photo together, with no message at all**, produces a
   stated reading of each file and two drafts; attaching a photograph of something that is not a vehicle
   document produces a plain "I can't tell what this is" and no draft card.
4. **A photographed fuel receipt drafts `log_fuel_fillup`, never a `Fuel`-category `log_expense`** — and if
   the model proposes one anyway, confirming it is refused by the same rule the expense sheet obeys.
5. The chat is reachable from the top nav above 900px as a side panel and from the bottom nav below it as the
   `/:reg/assistant` screen, and a read-only assistant question works identically on both.
