import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useRef, useState } from 'react'
import type { ApiError, ApiResult } from '../api/client'
import {
  confirmChatWrites,
  declineChatWrite,
  sendChatMessage,
  type ChatEvent,
  type ChatFile,
  type ChatMessage,
  type ChatWriteDecision,
  type JsonSchema,
} from '../api/chat'

/** One line in the panel. The transcript the server needs is held separately and never rendered. */
export type ChatEntry =
  | { id: number; kind: 'you'; text: string; files: number }
  | { id: number; kind: 'assistant'; text: string }
  | { id: number; kind: 'tool'; name: string }
  | { id: number; kind: 'note'; text: string; tone: 'ok' | 'bad' }

/**
 * An entry before it has an id — distributive, so each member of the union keeps its own shape. A plain
 * `Omit<ChatEntry, 'id'>` collapses them into one object with no discriminated members, which type-checks a
 * `you` entry carrying a tool name.
 */
type NewEntry = ChatEntry extends infer T ? (T extends { id: number } ? Omit<T, 'id'> : never) : never

/** A write the assistant has proposed and the owner has not answered. */
export interface ChatDraft {
  callId: string
  tool: string
  title: string
  values: Record<string, unknown>
  schema?: JsonSchema
}

/**
 * Every draft of one turn, under the id that answers them.
 *
 * They are held together because they are answered together: the server needs a decision for each, and a
 * suspension left unanswered is rejected upstream and breaks the conversation from then on.
 */
export interface ChatDraftBatch {
  pendingWriteId: string
  drafts: ChatDraft[]
}

/**
 * The conversation, as a component sees it.
 *
 * Two states are kept side by side and they are not the same thing: `entries` is what the panel renders, and
 * `transcript` is the server's own message shape, held opaque and echoed back. Rendering the transcript
 * instead would mean understanding reasoning blocks, tool results and approval requests — and dropping one
 * because it looks empty breaks the next turn, since the provider rejects an edited history.
 */
export function useChat(vehicle: string | null) {
  const [entries, setEntries] = useState<ChatEntry[]>([])
  const [streaming, setStreaming] = useState('')
  const [batch, setBatch] = useState<ChatDraftBatch | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const transcript = useRef<ChatMessage[]>([])
  const nextId = useRef(0)

  const queries = useQueryClient()

  const add = useCallback((entry: NewEntry) => {
    setEntries((previous) => [...previous, { ...entry, id: nextId.current++ } as ChatEntry])
  }, [])

  const consume = useCallback(
    async (result: ApiResult<AsyncIterable<ChatEvent>>) => {
      if (!result.ok) {
        setError(describe(result.error))
        setBusy(false)
        return
      }

      let text = ''

      try {
        for await (const event of result.value) {
          switch (event.type) {
            case 'text':
              text += event.delta
              setStreaming(text)
              break

            case 'tool':
              // Only the start: the pair would read as two events for one lookup, and the panel is narrating,
              // not reporting.
              if (event.status === 'running') add({ kind: 'tool', name: event.name })
              break

            case 'pending_write':
              setBatch({
                pendingWriteId: event.pendingWriteId,
                drafts: event.drafts.map((d) => ({
                  callId: d.callId,
                  tool: d.tool,
                  title: d.title,
                  values: d.arguments,
                  ...(d.schema && { schema: d.schema }),
                })),
              })
              break

            case 'done':
              transcript.current = [...transcript.current, ...event.messages]
              break

            case 'error':
              setError(event.detail)
              break
          }
        }
      } catch (cause) {
        setError(String(cause))
      }

      // Flushed once, at the end: the assistant's whole turn is one paragraph in the panel, however many
      // chunks it arrived in.
      if (text !== '') add({ kind: 'assistant', text })
      setStreaming('')
      setBusy(false)
    },
    [add],
  )

  const send = useCallback(
    async (message: string, files: ChatFile[] = []) => {
      if (busy) return

      setError(null)
      setBusy(true)
      add({ kind: 'you', text: message, files: files.length })

      // The wire shape of a user turn, built by hand because the browser has no ChatMessage type. It is pinned
      // server-side by TranscriptShapeTests, so a library upgrade that renames a property fails a test rather
      // than a panel.
      //
      // Any draft still on screen is abandoned by typing instead of answering it, so its bookkeeping goes now.
      transcript.current = [
        ...withoutApprovals(transcript.current),
        { role: 'user', contents: [{ $type: 'text', text: message }] },
      ]

      await consume(await sendChatMessage(transcript.current, vehicle, files))
    },
    [add, busy, consume, vehicle],
  )

  const confirm = useCallback(
    async (decisions: ChatWriteDecision[]) => {
      if (batch === null) return

      setError(null)
      setBusy(true)
      const answered = batch
      setBatch(null)

      const result = await confirmChatWrites(transcript.current, answered.pendingWriteId, decisions)

      // A field the tool refused: the batch comes back with the values the owner typed, so nothing is retyped.
      // The server keys those errors `callId.field`, which is what lets a list of fifteen mark the one bad row.
      if (!result.ok && result.error.kind === 'http' && result.error.errors !== undefined) {
        setBatch({
          ...answered,
          drafts: answered.drafts.map((d) => {
            const values = decisions.find((x) => x.callId === d.callId)?.arguments
            return values === undefined ? d : { ...d, values }
          }),
        })
        setError(result.error.message)
        setBusy(false)
        return { errors: result.error.errors }
      }

      // Deliberately no "Saved" note here. The confirm posts and the tools run on the far side of the stream,
      // and they can still be refused by the domain — a mileage below the current reading, a fuel row typed as
      // an expense. The assistant's own next sentence says what happened, and it is the one that knows. A note
      // written before the answer arrives is a claim the client cannot back.
      await consume(result)

      // The pair has served its purpose: the turn came back with the call and its result, which is the same
      // write in the shape the model actually sees.
      transcript.current = withoutApprovals(transcript.current)

      // Every screen behind the panel is now potentially stale, and which ones depends on a tool the client
      // deliberately does not model — so everything is refetched rather than a guessed subset. The alternative
      // is a garage still reading "0 vehicles tracked" beside an assistant that has just added one, which is
      // the state this was found in.
      await queries.invalidateQueries()

      return undefined
    },
    [batch, consume, queries],
  )

  const decline = useCallback(async () => {
    if (batch === null) return

    setError(null)
    setBusy(true)
    const answered = batch
    setBatch(null)

    add({
      kind: 'note',
      text: answered.drafts.length === 1 ? `Discarded · ${answered.drafts[0]!.title}` : `Discarded · ${answered.drafts.length} drafts`,
      tone: 'bad',
    })

    // A refusal is a request, not a silence — the model is told, and the turn completes rather than hanging.
    await consume(await declineChatWrite(transcript.current, answered.pendingWriteId, 'The owner discarded it.'))

    transcript.current = withoutApprovals(transcript.current)
  }, [add, batch, consume])

  return { entries, streaming, batch, busy, error, send, confirm, decline }
}

/**
 * The transcript without the approval bookkeeping.
 *
 * A suspension travels as a `toolApprovalRequest`, and answering it produces a matching response — but the
 * loop *consumes* both and hands back the call and its result instead. Replaying the pair beside the call it
 * became sends the same write twice in two shapes, and the whole conversation is rejected from then on:
 *
 *     ToolApprovalRequestContent found with FunctionCall.CallId(s) '…' that have no matching
 *     ToolApprovalResponseContent
 *
 * — which is what a message after a saved draft used to produce. So once a batch is answered, its bookkeeping
 * goes; the call and the result stay, and they are the honest record of what happened. A draft the owner
 * abandons is dropped the same way when they type something else instead, because an unanswered request is
 * rejected just as firmly as a duplicated one.
 */
function withoutApprovals(messages: ChatMessage[]): ChatMessage[] {
  const kept: ChatMessage[] = []

  for (const message of messages) {
    const shape = message as { contents?: { $type?: string }[] }

    if (!Array.isArray(shape.contents)) {
      kept.push(message)
      continue
    }

    const contents = shape.contents.filter((c) => c.$type !== 'toolApprovalRequest' && c.$type !== 'toolApprovalResponse')

    if (contents.length === shape.contents.length) kept.push(message)
    // A message that held nothing else is dropped: an empty content list is not a message the provider accepts.
    else if (contents.length > 0) kept.push({ ...shape, contents } as ChatMessage)
  }

  return kept
}

/**
 * An error the owner can act on.
 *
 * The server's `detail` is written for a person — a spent allowance says when it resets, an unconfigured
 * deployment says which setting turns it on — so it is shown rather than replaced with a status word.
 */
function describe(error: ApiError): string {
  switch (error.kind) {
    case 'unauthorized':
      return 'Your session has expired. Sign in again to carry on.'
    case 'network':
      return 'The assistant could not be reached. Check your connection and try again.'
    case 'http':
      return error.message
  }
}
