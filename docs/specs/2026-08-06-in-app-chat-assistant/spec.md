# Spec Requirements Document

> Spec: In-App Chat Assistant — conversational access and photo-to-record drafting
> Created: 2026-08-06
> Status: Planning

## Overview

Put the assistant inside the app: a chat icon in the shell that opens a side panel on desktop and a dedicated
screen on mobile, backed by a server-side Claude conversation that calls the **same tools the MCP server
already exposes**. Photos are its distinguishing input — an MOT certificate, a fuel receipt, an odometer shot
— which the model reads and turns into a filled-in draft of the appropriate write, presented for the owner to
check and confirm before anything is saved.

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

The model reads the image and proposes the write: an MOT pass becomes `add_service` with `type = "MOT"`, its
date, the odometer and the garage filled in; a fuel receipt plus an odometer photo becomes `log_fuel_fillup`
with litres, price per litre, total and mileage. **The draft is never saved on the model's say-so.** It
renders as an editable card — every field visible, every field correctable — and only the owner's tap runs
the tool. This is the rule `2026-07-16-receipt-photo-capture` already wrote down and deliberately deferred:
*"a wrong auto-filled amount silently entered is worse than a field the owner typed"*. The difference is that
this spec makes the extraction real while keeping the human in the loop, so the risk the earlier spec pushed
to v2 never materialises.

The photo itself is **not stored**. It travels browser → API → Claude, produces a draft, and is discarded.
Attaching a receipt as durable evidence is the Documents feature's job and stays there — building a second,
half-formed upload path here would leave two to keep in step.

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
3. **Photo input** — the chat sheet accepts camera capture or file upload (JPEG/PNG/WebP/HEIC, multiple per
   message), sends the bytes as image content blocks, and never persists them. Client-side downscale caps the
   long edge and the request size.
4. **Shell surface** — a chat icon in `TopNav` (desktop) and `BottomNav` (mobile) opening a docked side panel
   above 900px and the `/:reg/assistant` route below it, using the same breakpoint the two navs already split
   on. Vehicle-scoped, so tools default to the car you are looking at.
5. **`EntrySource.Chat`** — a fifth attribution value and the migration that widens the
   `ck_<table>_source` check constraint on every auditable table, so a chat-drafted row is distinguishable
   from a web-typed or MCP-written one.

## Out of Scope

- **Storing the photo.** No `Document` row, no volume write, no `Sha256`. The Documents spec owns upload and
  storage; the receipt-photo-capture spec owns attaching a receipt to its expense. This spec reads an image
  and forgets it. The write request carries a nullable `documentId` that nothing sets today, so a later
  Documents spec can attach evidence without a contract change.
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
3. The chat is reachable from the top nav above 900px as a side panel and from the bottom nav below it as the
   `/:reg/assistant` screen, and a read-only assistant question works identically on both.
