import { useEffect, useRef, useState } from 'react'
import type { ChatFile } from '../api/chat'
import { Btn, Mark } from '../components/Btn'
import { Icon } from '../components/Icon'
import { prepare } from '../lib/attachments'
import { DraftCard } from './DraftCard'
import { useChat, type ChatEntry } from './useChat'

/**
 * The assistant, in the one component both surfaces render.
 *
 * `dock` is the right-hand panel above 900 px; `page` is the `/:reg/assistant` screen below it. **Two
 * renderings of one component, not two components** — a transcript, a composer and a draft card that behaved
 * even slightly differently by viewport would be two things to fix every time and one thing to forget.
 *
 * 900 px is `TopNav`/`BottomNav`'s existing breakpoint. It is not re-declared here; the dock is simply never
 * mounted below it, because the button that opens it lives in the top bar, which is itself hidden there.
 */
export function ChatPanel({
  vehicle,
  variant,
  onClose,
}: {
  vehicle: string | null
  variant: 'dock' | 'page'
  onClose?: () => void
}) {
  const { entries, streaming, draft, busy, error, send, confirm, decline } = useChat(vehicle)
  const [message, setMessage] = useState('')
  const [files, setFiles] = useState<{ file: ChatFile; name: string }[]>([])
  const [rejected, setRejected] = useState<string[]>([])
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]> | undefined>(undefined)

  const foot = useRef<HTMLDivElement>(null)

  // Follow the conversation down. `?.` because jsdom has no layout engine and does not implement it.
  useEffect(() => {
    foot.current?.scrollIntoView?.({ block: 'end' })
  }, [entries, streaming, draft])

  const submit = async () => {
    const text = message.trim()
    if (text === '' && files.length === 0) return

    setMessage('')
    const attached = files.map((f) => f.file)
    setFiles([])
    setRejected([])
    await send(text === '' ? 'Read this.' : text, attached)
  }

  const attach = async (picked: FileList | null) => {
    if (picked === null) return

    const accepted: { file: ChatFile; name: string }[] = []
    const refused: string[] = []

    for (const file of Array.from(picked)) {
      const result = await prepare(file)
      if (result.ok) accepted.push({ file: result.value, name: result.preview })
      else refused.push(result.reason)
    }

    setFiles((previous) => [...previous, ...accepted].slice(0, 5))
    setRejected(refused)
  }

  return (
    <section className={variant === 'dock' ? 'chat chat-dock' : 'chat chat-page'} aria-label="Assistant">
      <header className="chat-head">
        <span className="chat-eyebrow">Assistant</span>
        <h2>{vehicle === null ? 'Your garage' : vehicle}</h2>
        {onClose !== undefined && <Mark onClick={onClose}>Close</Mark>}
      </header>

      <div className="chat-log">
        {entries.length === 0 && streaming === '' && <Opening vehicle={vehicle} />}

        {entries.map((entry) => (
          <Entry key={entry.id} entry={entry} />
        ))}

        {streaming !== '' && (
          <p className="chat-msg chat-them">
            {streaming}
            <span className="chat-caret" aria-hidden="true" />
          </p>
        )}

        {busy && streaming === '' && (
          <p className="chat-thinking" role="status">
            Thinking…
          </p>
        )}

        {draft !== null && (
          <DraftCard
            draft={draft}
            busy={busy}
            errors={fieldErrors}
            onSave={async (values) => {
              const outcome = await confirm(values)
              setFieldErrors(outcome?.errors)
            }}
            onDiscard={() => {
              setFieldErrors(undefined)
              void decline()
            }}
          />
        )}

        {error !== null && (
          <p className="chat-error" role="alert">
            {error}
          </p>
        )}

        <div ref={foot} />
      </div>

      <div className="chat-compose">
        {rejected.length > 0 && (
          <ul className="chat-rejected" role="alert">
            {rejected.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        )}

        {files.length > 0 && (
          <ul className="chat-files">
            {files.map((file, index) => (
              <li key={`${file.name}-${index}`}>
                {file.name}
                <button
                  type="button"
                  className="chat-drop"
                  onClick={() => setFiles((previous) => previous.filter((_, i) => i !== index))}
                  aria-label={`Remove ${file.name}`}
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}

        <form
          className="chat-row"
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
        >
          <label className="chat-attach">
            {/* `capture` is not set: it forces the camera and hides the photo library, and the commonest
                attachment is a certificate already in the roll or an emailed PDF. The accept list is what
                offers the camera on a phone anyway. */}
            <input
              type="file"
              accept="image/*,application/pdf"
              multiple
              onChange={(e) => {
                void attach(e.target.files)
                e.target.value = ''
              }}
            />
            <span aria-hidden="true">+</span>
            <span className="sr-only">Attach a photo or PDF</span>
          </label>

          <textarea
            className="chat-input"
            value={message}
            rows={1}
            placeholder={vehicle === null ? 'Ask about your cars…' : `Ask about ${vehicle}…`}
            aria-label="Message"
            onChange={(e) => setMessage(e.target.value)}
            onKeyDown={(e) => {
              // Enter sends, Shift+Enter breaks the line — the convention every chat uses, and the reason this
              // is a textarea rather than an input in the first place.
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                void submit()
              }
            }}
          />

          <Btn type="submit" onClick={() => {}} disabled={busy}>
            Send
          </Btn>
        </form>

        <p className="chat-note">
          Messages and anything attached are sent to Anthropic to be answered. Attachments are not stored.
        </p>
      </div>
    </section>
  )
}

/** What the panel says before anyone has asked anything. */
function Opening({ vehicle }: { vehicle: string | null }) {
  return (
    <div className="chat-open">
      <p>
        Ask about {vehicle === null ? 'your cars' : vehicle} — what needs attention, what the fuel has cost,
        when the MOT runs out. Every figure comes from your own records.
      </p>
      <p>
        Or photograph the paperwork. An MOT certificate, a fuel receipt, an odometer shot: the assistant reads
        it and fills in the record for you to check. <strong>Nothing is saved until you press Save.</strong>
      </p>
    </div>
  )
}

function Entry({ entry }: { entry: ChatEntry }) {
  switch (entry.kind) {
    case 'you':
      return (
        <p className="chat-msg chat-you">
          {entry.text}
          {entry.files > 0 && (
            <span className="chat-clip"> · {entry.files === 1 ? '1 attachment' : `${entry.files} attachments`}</span>
          )}
        </p>
      )

    case 'assistant':
      return <p className="chat-msg chat-them">{entry.text}</p>

    case 'tool':
      // Narration, not a result: it says what the assistant is looking at, which is the difference between a
      // pause that reads as work and a pause that reads as a hang.
      return (
        <p className="chat-tool">
          <Icon name="arrow-right" />
          {entry.name.replace(/_/g, ' ')}
        </p>
      )

    case 'note':
      return <p className={entry.tone === 'ok' ? 'chat-note-ok' : 'chat-note-bad'}>{entry.text}</p>
  }
}
