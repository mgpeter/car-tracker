import { useAuth0 } from '@auth0/auth0-react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useCallback, useState } from 'react'
import { apiDownload, apiRequest } from '../../api/client'
import { ApiFailure, queryKeys, useMeta } from '../../api/queries'
import { Btn } from '../../components/Btn'
import { Panel } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

/** `GET /api/account/summary` — the weight the confirmation states before it will arm. */
interface AccountSummary {
  email: string
  createdAt: string
  vehicleCount: number
  logEntryCount: number
  documentCount: number
  documentBytes: number
  assistantTokenCount: number
}

const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`

/**
 * YOUR ACCOUNT — take everything out, or destroy it (UK GDPR Art. 15, 17 and 20).
 *
 * **The export sits above the deletion, in the same panel, on purpose.** Offering someone their data next to
 * the button that destroys it is the honest ordering and costs nothing; putting the destructive control first
 * would make the safe option look like an afterthought.
 *
 * **`ConfirmButton` is deliberately not used.** Its two-step is calibrated for deleting one fuel fill from a
 * table — the right weight for a mistake that takes thirty seconds to re-enter. This is not that. The
 * confirmation is a sheet that says how much is about to go, lists what goes with it including the login
 * itself, and will not arm until the account's address is typed out.
 */
export function DangerZonePanel() {
  const { data: meta } = useMeta()

  const summary = useQuery({
    queryKey: queryKeys.account,
    queryFn: async () => {
      const r = await apiRequest<AccountSummary>('/api/account/summary')
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const [confirming, setConfirming] = useState(false)
  const closeSheet = useCallback(() => setConfirming(false), [])

  return (
    <Panel>
      <div style={{ padding: 18, display: 'grid', gap: 22, gridTemplateColumns: 'minmax(0, 1fr)' }}>
        <ExportBlock />
        <DeleteBlock
          summary={summary.data}
          // Undefined while `meta` is in flight: a panel that assumed "not configured" for a second would
          // flash "deletion is unavailable here" at every owner on every visit, which is a lie about their
          // deployment rather than a loading state.
          configured={meta?.identityDeletionConfigured}
          onOpen={() => setConfirming(true)}
        />
      </div>

      {summary.data !== undefined && (
        <DeleteSheet open={confirming} onClose={closeSheet} summary={summary.data} />
      )}
    </Panel>
  )
}

/**
 * The download.
 *
 * Through the authenticated fetch seam and an object URL, never a plain `<a href>` — a navigation carries
 * cookies, not our bearer, so a direct link to `/api/account/export` would answer 401 and save the browser's
 * error page. Same lesson the documents screen learned first.
 */
function ExportBlock() {
  const { toast } = useToast()
  const [pending, setPending] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  const download = async () => {
    setPending(true)
    setFailure(null)

    const result = await apiDownload('/api/account/export')
    setPending(false)

    if (!result.ok) {
      setFailure(
        result.error.kind === 'network'
          ? 'Could not reach the server. Try again in a moment.'
          : result.error.kind === 'unauthorized'
            ? 'Your session has expired. Sign in again, then retry.'
            : result.error.message,
      )
      return
    }

    const url = URL.createObjectURL(result.value.blob)
    const anchor = window.document.createElement('a')
    anchor.href = url
    // The server names the file, and its name carries the server's export date. The fallback is only for a
    // deployment behind something that strips Content-Disposition.
    anchor.download = result.value.filename ?? 'cartracker-export.json'
    anchor.click()

    // Long enough for the save to have taken the bytes, then let them go — an object URL pins its blob for the
    // life of the document, and an export is the largest thing this app ever hands the browser.
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    toast('Export downloaded · every row this account owns')
  }

  return (
    <div style={{ display: 'grid', gap: 10, gridTemplateColumns: 'minmax(0, 1fr)' }}>
      <h3 style={{ margin: 0, fontSize: 15 }}>Download everything</h3>
      <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '56ch' }}>
        One JSON file holding every row this account owns — your cars, every log entry, your reference lists and
        your assistant tokens. It carries <b>no calculated figures</b>: MPG, cost per mile, check status and the
        spend totals are all worked out fresh from these rows every time you open a screen, and a file full of
        frozen copies is exactly the thing this app exists not to keep. Document files are not included; their
        details are, and the files download individually from the documents screen.
      </p>
      {failure !== null && (
        <p className="hint err" role="alert" style={{ margin: 0 }}>
          {failure}
        </p>
      )}
      <div>
        <Btn variant="ghost" onClick={() => void download()} disabled={pending}>
          {pending ? 'Preparing…' : 'Download my data'}
        </Btn>
      </div>
    </div>
  )
}

function DeleteBlock({
  summary,
  configured,
  onOpen,
}: {
  summary: AccountSummary | undefined
  configured: boolean | undefined
  onOpen: () => void
}) {
  return (
    <div
      style={{
        display: 'grid',
        gap: 10,
        gridTemplateColumns: 'minmax(0, 1fr)',
        borderTop: '1px solid var(--line)',
        paddingTop: 22,
      }}
    >
      <h3 style={{ margin: 0, fontSize: 15 }}>Delete this account</h3>
      <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '56ch' }}>
        Erases your cars, every log entry, your uploaded documents, your reference lists and your assistant
        tokens — and the login itself. It cannot be undone and there is no copy kept. Download your data first
        if you want it.
      </p>

      {configured === undefined ? null : configured === false ? (
        // No button at all, rather than one that answers 503. This deployment has no credential for erasing the
        // login, and deleting the data while leaving a working sign-in behind is the one outcome worse than
        // doing nothing — so the server refuses it, and this says so instead of offering the press.
        <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '56ch' }}>
          <b>Deletion is unavailable on this deployment.</b> It has no way to erase the login behind the account,
          and removing your data while leaving a working sign-in would be worse than leaving both. Ask whoever
          runs this instance to delete the account for you — the export above is yours to keep either way.
        </p>
      ) : (
        <div>
          {/* Disabled only while the counts are still in flight: the confirmation cannot state the weight of
              what is about to go until it knows it, and a sheet that opened blank would be asking for consent
              on nothing. */}
          <Btn variant="danger" onClick={onOpen} disabled={summary === undefined}>
            Delete account…
          </Btn>
        </div>
      )}
    </div>
  )
}

/**
 * The confirmation.
 *
 * It states the counts before it will arm, because "this will delete everything" without saying how much
 * everything is asks for consent it has not informed. The typed address is a real gate and not theatre: the
 * endpoint requires it too, so a mis-wired button cannot delete an account on an empty body.
 */
function DeleteSheet({ open, onClose, summary }: { open: boolean; onClose: () => void; summary: AccountSummary }) {
  const { logout } = useAuth0()
  const [typed, setTyped] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [done, setDone] = useState(false)

  // Trimmed, because a pasted address often brings a space with it and that is not a different address — but
  // otherwise exact. The server compares case-insensitively; being stricter here costs a re-type at worst,
  // where being looser would arm the button on something the server then refuses.
  const matches = typed.trim() === summary.email

  const remove = useMutation({
    mutationFn: async () => {
      const r = await apiRequest<null>('/api/account', {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmEmail: typed.trim() }),
      })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: () => {
      // There is no account behind this session any more, so nothing is refreshed and nothing re-rendered —
      // the cache is left exactly as it is and the next thing that happens is a full-page navigation to Auth0's
      // logout endpoint. Invalidating anything here would only fire a round of queries at an account that no
      // longer exists and paint their 401s on the way out.
      setDone(true)
      logout({ logoutParams: { returnTo: window.location.origin } })
    },
    onError: (e) => setErrors(reportApiError(e, ['confirmEmail'])),
  })

  if (done) {
    return (
      <Sheet open={open} onClose={onClose} title="Account deleted" subtitle="Signing you out.">
        <p style={{ margin: 0, gridColumn: '1 / -1' }} role="status">
          Everything this account held is gone. You are being signed out — there is nothing left to sign back
          into.
        </p>
      </Sheet>
    )
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Delete this account"
      subtitle="Irreversible. Nothing is kept and nothing can be restored."
      onSubmit={() => matches && remove.mutate()}
      footer={
        <>
          <Btn variant="ghost" onClick={onClose}>
            Cancel
          </Btn>
          <Btn variant="danger" type="submit" onClick={() => {}} disabled={!matches || remove.isPending}>
            {remove.isPending ? 'Deleting…' : 'Delete everything'}
          </Btn>
        </>
      }
    >
      <div style={{ gridColumn: '1 / -1', display: 'grid', gap: 10 }}>
        <p style={{ margin: 0 }}>
          This account holds {plural(summary.vehicleCount, 'vehicle')},{' '}
          {plural(summary.logEntryCount, 'log entry', 'log entries')}, {plural(summary.documentCount, 'document')}{' '}
          and {plural(summary.assistantTokenCount, 'assistant token')}.
        </p>
        <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13 }}>
          Deleting removes all of it — every car and its whole history, the uploaded document files themselves,
          your garages, wash locations and expense categories, every assistant token (any assistant still using
          one stops working immediately) — and then <b>your login</b>, so the address you signed in with will no
          longer reach anything here.
        </p>
      </div>

      <Field
        label="Type your email to confirm"
        wide
        error={fieldError(errors, 'confirmEmail')}
        hint={`Type ${summary.email} exactly. The button below stays disabled until it matches.`}
      >
        {(p) => (
          <input
            type="text"
            autoComplete="off"
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            placeholder={summary.email}
            {...p}
          />
        )}
      </Field>

      {formError(errors) !== undefined && (
        <div className="field wide">
          <span className="hint err" role="alert">
            {formError(errors)}
          </span>
        </div>
      )}
    </Sheet>
  )
}
