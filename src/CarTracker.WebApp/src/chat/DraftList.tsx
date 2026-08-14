import { useState } from 'react'
import type { ChatWriteDecision } from '../api/chat'
import { Btn } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { DraftCard, labelFor } from './DraftCard'
import type { ChatDraft, ChatDraftBatch } from './useChat'

/**
 * The writes one turn proposed, as a list you can look down before saving.
 *
 * A single draft renders exactly as it always has — the card, on its own. The list only appears when there is
 * more than one, which is what transcribing a workbook page looks like: sixteen fills, or a service record and
 * a fill read off two photographs.
 *
 * **Every draft is decided in one request**, including the ones left unticked. A suspension the server never
 * hears about is rejected upstream and breaks the conversation from then on, which is exactly the failure this
 * component was written for.
 */
export function DraftList({
  batch,
  busy,
  errors,
  onSave,
  onDiscard,
}: {
  batch: ChatDraftBatch
  busy: boolean
  errors: Record<string, string[]> | undefined
  onSave: (decisions: ChatWriteDecision[]) => void
  onDiscard: () => void
}) {
  const [chosen, setChosen] = useState<Set<string>>(() => new Set(batch.drafts.map((d) => d.callId)))
  const [open, setOpen] = useState<string | null>(null)
  const [edited, setEdited] = useState<Record<string, Record<string, unknown>>>({})

  const single = batch.drafts.length === 1

  const decisions = (): ChatWriteDecision[] =>
    batch.drafts.map((draft) =>
      chosen.has(draft.callId)
        ? { callId: draft.callId, arguments: edited[draft.callId] ?? draft.values }
        : { callId: draft.callId, declined: true },
    )

  if (single) {
    const only = batch.drafts[0]!
    return (
      <DraftCard
        draft={only}
        busy={busy}
        errors={fieldErrors(errors, only.callId)}
        onSave={(values) => onSave([{ callId: only.callId, arguments: values }])}
        onDiscard={onDiscard}
      />
    )
  }

  const toggle = (callId: string) =>
    setChosen((previous) => {
      const next = new Set(previous)
      if (next.has(callId)) next.delete(callId)
      else next.add(callId)
      return next
    })

  return (
    <div className="draft draftlist">
      <div className="draft-head">
        <span className="draft-eyebrow">Ready to save</span>
        <h3>
          {batch.drafts.length} drafts · {batch.drafts[0]!.title}
        </h3>
      </div>

      <ul className="dl-rows">
        {batch.drafts.map((draft) => {
          const expanded = open === draft.callId
          const bad = fieldErrors(errors, draft.callId)

          return (
            <li key={draft.callId} className={bad === undefined ? 'dl-row' : 'dl-row dl-bad'}>
              <div className="dl-line">
                <label className="dl-pick">
                  <input
                    type="checkbox"
                    checked={chosen.has(draft.callId)}
                    onChange={() => toggle(draft.callId)}
                  />
                  <span className="sr-only">Save {summarise(draft)}</span>
                </label>

                <button
                  type="button"
                  className="dl-open"
                  aria-expanded={expanded}
                  onClick={() => setOpen(expanded ? null : draft.callId)}
                >
                  <span className="dl-summary">{summarise(draft)}</span>
                  <span className="dl-caret" aria-hidden="true">
                    {expanded ? '▾' : '▸'}
                  </span>
                </button>
              </div>

              {expanded && (
                <DraftCard
                  draft={draft}
                  busy={busy}
                  errors={bad}
                  embedded
                  onChange={(values) => setEdited((previous) => ({ ...previous, [draft.callId]: values }))}
                />
              )}
            </li>
          )
        })}
      </ul>

      <div className="draft-foot">
        <ConfirmButton label="Discard all" confirmLabel="Discard all of them?" onConfirm={onDiscard} />
        <Btn onClick={() => onSave(decisions())} disabled={busy || chosen.size === 0}>
          {chosen.size === batch.drafts.length ? `Save all ${chosen.size}` : `Save ${chosen.size}`}
        </Btn>
      </div>
    </div>
  )
}

/**
 * The two or three values that identify a draft in one line.
 *
 * Read off whatever the tool actually filled in rather than a per-tool list of interesting fields: thirty
 * write tools would need thirty such lists, and they would drift the week after they were written. A date
 * leads when there is one, because these are log rows and the date is what the eye looks for.
 */
function summarise(draft: ChatDraft): string {
  const entries = Object.entries(draft.values).filter(([, v]) => v !== null && v !== undefined && v !== '')
  const dates = entries.filter(([k]) => /date|on$/i.test(k))
  const rest = entries.filter(([k]) => !/date|on$/i.test(k))

  const shown = [...dates, ...rest].slice(0, 3)

  return shown.length === 0
    ? labelFor(draft.tool)
    : shown.map(([k, v]) => `${labelFor(k).toLowerCase()} ${String(v)}`).join(' · ')
}

/** The server keys a batch's field errors `callId.field`; this is the half that belongs to one draft. */
function fieldErrors(
  errors: Record<string, string[]> | undefined,
  callId: string,
): Record<string, string[]> | undefined {
  if (errors === undefined) return undefined

  const mine = Object.entries(errors)
    .filter(([key]) => key.startsWith(`${callId}.`))
    .map(([key, messages]) => [key.slice(callId.length + 1), messages] as const)

  return mine.length === 0 ? undefined : Object.fromEntries(mine)
}
