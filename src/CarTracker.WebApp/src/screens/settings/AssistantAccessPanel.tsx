import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useState } from 'react'
import { apiRequest } from '../../api/client'
import { ApiFailure } from '../../api/queries'
import { Btn } from '../../components/Btn'
import { Panel } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
import { useToast } from '../../shell/Toast'

/**
 * ASSISTANT ACCESS — the scoped MCP tokens the assistant authenticates with, and the write-audit trail
 * (README §5.1). A token's secret is shown once on creation and never again; only its hash is stored.
 */

type Scope = 'ReadOnly' | 'ReadWrite'

interface TokenView {
  id: number
  name: string
  scope: Scope
  createdAt: string
  lastUsedAt: string | null
  revokedAt: string | null
  readCount: number
  writeCount: number
}

interface CreatedToken {
  id: number
  name: string
  scope: Scope
  secret: string
}

interface AuditView {
  id: number
  tokenId: number
  tool: string
  vehicleId: number | null
  summary: string
  timestampUtc: string
}

const tokensKey = ['assistant', 'tokens']
const auditKey = ['assistant', 'audit']

const when = (iso: string | null) =>
  iso ? new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : '—'

export function AssistantAccessPanel() {
  const qc = useQueryClient()
  const { toast } = useToast()

  const [adding, setAdding] = useState(false)
  const [name, setName] = useState('')
  const [scope, setScope] = useState<Scope>('ReadOnly')
  const [secret, setSecret] = useState<CreatedToken | null>(null)
  const [confirmRevoke, setConfirmRevoke] = useState<number | null>(null)

  const tokens = useQuery({
    queryKey: tokensKey,
    queryFn: async () => {
      const r = await apiRequest<TokenView[]>('/api/assistant/tokens')
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const audit = useQuery({
    queryKey: auditKey,
    queryFn: async () => {
      const r = await apiRequest<AuditView[]>('/api/assistant/audit')
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const create = useMutation({
    mutationFn: async () => {
      const r = await apiRequest<CreatedToken>('/api/assistant/tokens', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: name.trim(), scope }),
      })
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
    onSuccess: (created) => {
      setSecret(created)
      setAdding(false)
      setName('')
      setScope('ReadOnly')
      void qc.invalidateQueries({ queryKey: tokensKey })
    },
  })

  const revoke = useMutation({
    mutationFn: async (id: number) => {
      const r = await apiRequest<null>(`/api/assistant/tokens/${id}`, { method: 'DELETE' })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: () => {
      setConfirmRevoke(null)
      toast('Token revoked · it authenticates nothing now')
      void qc.invalidateQueries({ queryKey: tokensKey })
    },
  })

  const copy = (value: string) => {
    void navigator.clipboard?.writeText(value)
    toast('Copied to clipboard')
  }

  // Stable, so the Sheet's focus trap (which depends on onEscape) does not re-run and steal focus back to the
  // dialog on every keystroke — a fresh closure here re-focuses the container after each letter.
  const closeSheet = useCallback(() => setAdding(false), [])

  return (
    <Panel>
      <div style={{ padding: 18, display: 'grid', gap: 16 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
          <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '46ch' }}>
            Tokens the assistant (Claude Desktop, or an in-app chat later) uses to reach the MCP server at{' '}
            <code>/mcp</code>. A <b>read-only</b> token can read your data; a <b>read-write</b> token can also log
            on your behalf. The secret is shown once — store it then.
          </p>
          <Btn onClick={() => setAdding(true)}>Add token…</Btn>
        </div>

        {secret && (
          <div
            role="status"
            style={{
              border: '1px solid var(--ok)',
              background: 'var(--ok-wash)',
              borderRadius: 8,
              padding: 14,
              display: 'grid',
              gap: 8,
            }}
          >
            <strong style={{ fontSize: 13 }}>
              Secret for “{secret.name}” — copy it now, it will not be shown again
            </strong>
            <code
              style={{
                fontFamily: 'var(--mono)',
                fontSize: 13,
                wordBreak: 'break-all',
                background: 'var(--ok-wash)',
                padding: '8px 10px',
                borderRadius: 6,
              }}
            >
              {secret.secret}
            </code>
            <div style={{ display: 'flex', gap: 8 }}>
              <Btn onClick={() => copy(secret.secret)}>Copy</Btn>
              <Btn variant="ghost" onClick={() => setSecret(null)}>
                Done
              </Btn>
            </div>
          </div>
        )}

        <div style={{ display: 'grid', gap: 8 }}>
          {tokens.isPending && <p style={{ margin: 0, color: 'var(--muted)' }}>Loading…</p>}
          {tokens.data?.length === 0 && (
            <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13 }}>No tokens yet.</p>
          )}
          {tokens.data?.map((t) => {
            const revoked = t.revokedAt !== null
            return (
              <div
                key={t.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 12,
                  padding: '10px 12px',
                  border: '1px solid var(--line)',
                  borderRadius: 8,
                  opacity: revoked ? 0.55 : 1,
                }}
              >
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontWeight: 600, textDecoration: revoked ? 'line-through' : 'none' }}>{t.name}</div>
                  <div style={{ fontSize: 12, color: 'var(--muted)', fontFamily: 'var(--mono)' }}>
                    {t.scope === 'ReadWrite' ? 'read-write' : 'read-only'} · last used {when(t.lastUsedAt)} ·{' '}
                    {t.readCount} reads · {t.writeCount} writes{revoked ? ` · revoked ${when(t.revokedAt)}` : ''}
                  </div>
                </div>
                {!revoked &&
                  (confirmRevoke === t.id ? (
                    <Btn variant="danger" onClick={() => revoke.mutate(t.id)}>
                      Confirm revoke
                    </Btn>
                  ) : (
                    <Btn variant="ghost" onClick={() => setConfirmRevoke(t.id)}>
                      Revoke
                    </Btn>
                  ))}
              </div>
            )
          })}
        </div>

        {audit.data && audit.data.length > 0 && (
          <div style={{ display: 'grid', gap: 6 }}>
            <div style={{ fontSize: 12, textTransform: 'uppercase', letterSpacing: 0.5, color: 'var(--muted)' }}>
              Write trail — reads are counted, not listed
            </div>
            {audit.data.slice(0, 20).map((a) => (
              <div key={a.id} style={{ fontSize: 12, fontFamily: 'var(--mono)', color: 'var(--muted)' }}>
                {new Date(a.timestampUtc).toLocaleString()} · <b style={{ color: 'var(--fg)' }}>{a.tool}</b> ·{' '}
                {a.summary}
              </div>
            ))}
          </div>
        )}
      </div>

      <Sheet
        open={adding}
        onClose={closeSheet}
        title="New assistant token"
        subtitle="The secret is shown once, then only its hash is kept."
        onSubmit={() => create.mutate()}
        footer={<Btn type="submit">Create token</Btn>}
      >
        <Field label="Name" wide hint="so you can recognise it to revoke — e.g. “Claude Desktop”">
          {(props) => (
            <input {...props} value={name} onChange={(e) => setName(e.target.value)} placeholder="Claude Desktop" />
          )}
        </Field>
        <Field label="Scope" hint="read-write can log on your behalf; read-only cannot mutate data">
          {(props) => (
            <select {...props} value={scope} onChange={(e) => setScope(e.target.value as Scope)}>
              <option value="ReadOnly">Read-only</option>
              <option value="ReadWrite">Read-write</option>
            </select>
          )}
        </Field>
      </Sheet>
    </Panel>
  )
}
