import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { getAuthenticated, getMeta, type MetaResponse } from './api/client'
import { getSettings, updateSettings } from './lib/settings'
import { ThemeToggle } from './theme/ThemeToggle'

/**
 * Scaffold only. This exists to prove the loop end to end — gateway → API → auth → render — and to give the
 * API key somewhere to live. The real UI arrives with the design-system port
 * (docs/specs/2026-07-14-react-app-foundation, tasks 2-4).
 */

/** Two probes, because "the API is down" and "the key is wrong" need different answers. */
type Reachability = 'checking' | 'up' | 'down'
type KeyState = 'unset' | 'checking' | 'valid' | 'invalid' | 'unreachable'

export default function App() {
  const [meta, setMeta] = useState<MetaResponse | null>(null)
  const [reachability, setReachability] = useState<Reachability>('checking')
  const [keyState, setKeyState] = useState<KeyState>('unset')
  const [apiKey, setApiKey] = useState(getSettings().apiKey)

  // The open endpoint: is the API there at all?
  useEffect(() => {
    void (async () => {
      const result = await getMeta()
      if (result.ok) {
        setMeta(result.value)
        setReachability('up')
      } else {
        setReachability('down')
      }
    })()
  }, [])

  const checkKey = useCallback(async () => {
    if (getSettings().apiKey === '') {
      setKeyState('unset')
      return
    }

    setKeyState('checking')
    const result = await getAuthenticated()

    if (result.ok) {
      setKeyState('valid')
    } else if (result.error.kind === 'unauthorized') {
      setKeyState('invalid')
    } else {
      setKeyState('unreachable')
    }
  }, [])

  useEffect(() => {
    void checkKey()
  }, [checkKey])

  const save = (event: FormEvent) => {
    event.preventDefault()
    updateSettings({ apiKey: apiKey.trim() })
    void checkKey()
  }

  return (
    <main style={{ fontFamily: 'var(--body)', maxWidth: '42rem', margin: '3rem auto', padding: '0 1rem' }}>
      <h1 style={{ fontFamily: 'var(--disp)', textTransform: 'uppercase', letterSpacing: '0.02em' }}>Car Tracker</h1>
      <p style={{ color: 'var(--muted)' }}>Scaffold. The real UI lands with the design-system port.</p>

      <section>
        <h2 style={{ fontFamily: 'var(--disp)', textTransform: 'uppercase' }}>Theme</h2>
        <div style={{ maxWidth: '22rem' }}>
          <ThemeToggle />
        </div>
      </section>

      <section>
        <h2 style={{ fontFamily: 'var(--disp)', textTransform: 'uppercase' }}>API</h2>
        {reachability === 'checking' && <p>Checking…</p>}
        {reachability === 'down' && <p>Cannot reach the API. Is the AppHost running?</p>}
        {reachability === 'up' && meta !== null && (
          <dl>
            <dt>Application</dt>
            <dd>{meta.applicationName}</dd>
            <dt>Version</dt>
            <dd>{meta.version}</dd>
            <dt>Environment</dt>
            <dd>{meta.environment}</dd>
            <dt>Server time (UTC)</dt>
            <dd>{meta.serverTimeUtc}</dd>
          </dl>
        )}
      </section>

      <section>
        <h2>API key</h2>
        <form onSubmit={save}>
          <label htmlFor="apiKey">
            Sent as <code>X-Api-Key</code>. Stored in this browser.
          </label>
          <br />
          <input
            id="apiKey"
            type="password"
            value={apiKey}
            onChange={(e) => setApiKey(e.target.value)}
            placeholder="paste the server's ApiKey:Value"
            style={{ width: '24rem', padding: '0.4rem', marginTop: '0.5rem' }}
          />
          <button type="submit" style={{ marginLeft: '0.5rem', padding: '0.4rem 0.8rem' }}>
            Save
          </button>
        </form>

        <p>
          {keyState === 'unset' && 'No key set — protected endpoints will return 401.'}
          {keyState === 'checking' && 'Verifying…'}
          {keyState === 'valid' && 'Key accepted.'}
          {keyState === 'invalid' && 'Key rejected by the server.'}
          {keyState === 'unreachable' && 'Could not verify — the API did not answer.'}
        </p>
      </section>
    </main>
  )
}
