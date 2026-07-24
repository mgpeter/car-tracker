import { useAuth0 } from '@auth0/auth0-react'
import { useEffect, useState, type ReactNode } from 'react'
import { setAccessTokenProvider } from '../api/client'
import { Btn } from '../components/Btn'

/**
 * The login wall. Nothing in the app renders until Auth0 confirms a session, so no screen can flash another
 * user's data before a redirect settles. Placed above the router, so even the garage home is gated.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { isLoading, isAuthenticated, error, loginWithRedirect, getAccessTokenSilently } = useAuth0()

  // Register the access-token getter BEFORE the app renders, and gate the children on it. Otherwise the first
  // data query can fire between mount and this effect — with no bearer — and get a 401 the query layer will not
  // retry. `tokenReady` makes "the token provider is wired" a precondition of rendering anything that fetches.
  const [tokenReady, setTokenReady] = useState(false)

  useEffect(() => {
    if (!isAuthenticated) {
      setAccessTokenProvider(null)
      setTokenReady(false)
      return
    }
    setAccessTokenProvider(() => getAccessTokenSilently())
    setTokenReady(true)
    return () => setAccessTokenProvider(null)
  }, [isAuthenticated, getAccessTokenSilently])

  if (isLoading || (isAuthenticated && !tokenReady)) {
    return <Splash>Checking your session…</Splash>
  }

  if (isAuthenticated) {
    return <>{children}</>
  }

  return (
    <Splash>
      <h1 style={{ fontFamily: 'var(--disp)', textTransform: 'uppercase', letterSpacing: '0.02em', margin: 0 }}>
        Car Tracker
      </h1>
      <p style={{ color: 'var(--muted)', maxWidth: '34ch', margin: 0 }}>
        Sign in to reach your garage. Each account sees only its own vehicles.
      </p>
      {error && (
        <p role="alert" style={{ color: 'var(--due)', margin: 0 }}>
          Sign-in failed: {error.message}
        </p>
      )}
      <div style={{ display: 'flex', gap: '0.75rem' }}>
        <Btn variant="solid" onClick={() => loginWithRedirect()}>
          Log in
        </Btn>
        <Btn variant="ghost" onClick={() => loginWithRedirect({ authorizationParams: { screen_hint: 'signup' } })}>
          Sign up
        </Btn>
      </div>
    </Splash>
  )
}

function Splash({ children }: { children: ReactNode }) {
  return (
    <main style={{ minHeight: '100dvh', display: 'grid', placeItems: 'center', textAlign: 'center', padding: '2rem' }}>
      <div style={{ display: 'grid', gap: '1rem', justifyItems: 'center' }}>{children}</div>
    </main>
  )
}
