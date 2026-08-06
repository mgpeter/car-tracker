# Spec Summary (Lite)

Put the assistant inside the app: a chat icon opening a docked side panel on desktop and a dedicated screen on
mobile, backed by a server-side Claude conversation that calls the same in-process `[McpServerTool]` methods
the MCP server exposes — so the chat and the dashboard cannot disagree. Read tools run freely; a write tool
suspends the turn and returns an editable draft the owner must confirm before anything is saved. Photos
(MOT certificate, fuel receipt, odometer) are read for their figures and discarded, never stored, and become
pre-filled drafts of `add_service`, `log_fuel_fillup` and the rest, stamped `EntrySource.Chat`.
