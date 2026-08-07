# Spec Summary (Lite)

Put the assistant inside the app: a chat icon opening a docked side panel on desktop and a dedicated screen on
mobile, backed by a server-side Claude conversation that calls the same in-process `[McpServerTool]` methods
the MCP server exposes — so the chat and the dashboard cannot disagree. Read tools run freely; a write tool
suspends the turn and returns an editable draft the owner must confirm before anything is saved.

Files (photos and PDFs — MOT certificate, fuel receipt, odometer shot, insurance schedule) are **identified**
by the model, read for their figures, and discarded. It states what it thinks each file is before drafting,
declines to draft on anything it cannot place, and asks rather than guesses — so a bare attachment with no
instruction works the way it already does in Claude Desktop against this project's MCP server. Drafts are
pre-filled `add_service`, `log_fuel_fillup` and the rest, stamped `EntrySource.Chat`.

This absorbs and replaces `2026-07-16-receipt-photo-capture`, whose v1 had the owner reading the photo and
typing the figures. Its governing rule survives — *a wrong auto-filled amount silently entered is worse than a
field the owner typed* — enforced now by the confirm step rather than by refusing to extract. So does its Fuel
refusal: a fuel receipt drafts `log_fuel_fillup`, never a typed `Fuel` expense, because that is the £163.16
gap. What does not survive is storing the file: nothing here writes a `Document`, so logging from a receipt
and keeping the receipt are two separate actions.
