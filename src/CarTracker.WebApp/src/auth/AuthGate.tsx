import { useAuth0 } from '@auth0/auth0-react'
import { useEffect, useState, type ReactNode } from 'react'
import { setAccessTokenProvider } from '../api/client'
import { LandingPage } from './LandingPage'

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

  // The public welcome. The auth knowledge stays here — LandingPage takes callbacks, so it can be tested
  // without a session and this file remains the only place that knows what `screen_hint` is for.
  return (
    <LandingPage
      onLogIn={() => loginWithRedirect()}
      onSignUp={() => loginWithRedirect({ authorizationParams: { screen_hint: 'signup' } })}
      {...(error && { error: error.message })}
    />
  )
}

function Splash({ children }: { children: ReactNode }) {
  return (
    <main style={{ minHeight: '100dvh', display: 'grid', placeItems: 'center', textAlign: 'center', padding: '2rem' }}>
      <div style={{ display: 'grid', gap: '1rem', justifyItems: 'center' }}>{children}</div>
    </main>
  )
}
