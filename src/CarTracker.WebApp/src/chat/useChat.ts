import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useRef, useState } from 'react'
import type { ApiError, ApiResult } from '../api/client'
import {
  confirmChatWrite,
  declineChatWrite,
  sendChatMessage,
  type ChatEvent,
  type ChatFile,
  type ChatMessage,
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
  pendingWriteId: string
  tool: string
  title: string
  values: Record<string, unknown>
  schema?: JsonSchema
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
  const [draft, setDraft] = useState<ChatDraft | null>(null)
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
              setDraft({
                pendingWriteId: event.pendingWriteId,
                tool: event.tool,
                title: event.title,
                values: event.arguments,
                ...(event.schema && { schema: event.schema }),
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
      transcript.current = [
        ...transcript.current,
        { role: 'user', contents: [{ $type: 'text', text: message }] },
      ]

      await consume(await sendChatMessage(transcript.current, vehicle, files))
    },
    [add, busy, consume, vehicle],
  )

  const confirm = useCallback(
    async (values: Record<string, unknown>) => {
      if (draft === null) return

      setError(null)
      setBusy(true)
      const answered = draft
      setDraft(null)

      const result = await confirmChatWrite(transcript.current, answered.pendingWriteId, values)

      // A field the tool refused: the card comes back with the values the owner typed, so nothing is retyped.
      if (!result.ok && result.error.kind === 'http' && result.error.errors !== undefined) {
        setDraft({ ...answered, values })
        setError(result.error.message)
        setBusy(false)
        return { errors: result.error.errors }
      }

      // Deliberately no "Saved" note here. The confirm posts and the tool runs on the far side of the stream,
      // and it can still be refused by the domain — a mileage below the current reading, a fuel row typed as an
      // expense. The assistant's own next sentence says what happened, and it is the one that knows. A note
      // written before the answer arrives is a claim the client cannot back.
      await consume(result)

      // Every screen behind the panel is now potentially stale, and which ones depends on a tool the client
      // deliberately does not model — so everything is refetched rather than a guessed subset. The alternative
      // is a garage still reading "0 vehicles tracked" beside an assistant that has just added one, which is
      // the state this was found in.
      await queries.invalidateQueries()

      return undefined
    },
    [consume, draft, queries],
  )

  const decline = useCallback(async () => {
    if (draft === null) return

    setError(null)
    setBusy(true)
    const answered = draft
    setDraft(null)

    add({ kind: 'note', text: `Discarded · ${answered.title}`, tone: 'bad' })

    // A refusal is a request, not a silence — the model is told, and the turn completes rather than hanging.
    await consume(await declineChatWrite(transcript.current, answered.pendingWriteId, 'The owner discarded it.'))
  }, [add, consume, draft])

  return { entries, streaming, draft, busy, error, send, confirm, decline }
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
