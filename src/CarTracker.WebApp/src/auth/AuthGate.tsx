import { useAuth0 } from '@auth0/auth0-react'
import { useEffect, useState, type ReactNode } from 'react'
import { setAccessTokenProvider } from '../api/client'
import { isNotInvited, useAccessCheck } from '../api/queries'
import { Btn } from '../components/Btn'
import { Panel } from '../components/layout'
import { LandingPage } from './LandingPage'

/**
 * The login wall. Nothing in the app renders until Auth0 confirms a session, so no screen can flash another
 * user's data before a redirect settles. Placed above the router, so even the garage home is gated.
 *
 * **Three states, not two.** A valid Auth0 session is no longer the same thing as an account: this deployment
 * admits only invited addresses (`Signup:AllowedEmails`/`AllowedDomains`), so someone can sign in perfectly and
 * still have no account behind the token. That person gets neither the app — there is nothing in it for them —
 * nor `LandingPage`, which would invite them to sign up for what they have just been refused. They get a short
 * panel that says so, and a way back out.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { isLoading, isAuthenticated, error, loginWithRedirect, logout, user, getAccessTokenSilently } = useAuth0()

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

  // The first API call the app makes, and the only one made above the router. Enabled only once the bearer is
  // wired, or it would ask the question without a credential and answer it wrongly.
  const access = useAccessCheck(isAuthenticated && tokenReady)

  if (isLoading || (isAuthenticated && !tokenReady)) {
    return <Splash>Checking your session…</Splash>
  }

  if (isAuthenticated) {
    if (access.isPending) return <Splash>Checking your session…</Splash>

    if (isNotInvited(access.error)) {
      return (
        <NotInvited
          email={user?.email}
          onSignOut={() => logout({ logoutParams: { returnTo: window.location.origin } })}
        />
      )
    }

    // Any other failure — the API down, a 500, a dropped connection — renders the app anyway. This probe is
    // here to catch one specific refusal, and a gate that locked everyone out whenever it could not reach the
    // server would turn a transient outage into a lockout. The screens report their own errors.
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

/**
 * Signed in, and not admitted.
 *
 * Deliberately plain: no nav, no shell, nothing to explore. It names the address that was refused, because the
 * commonest cause is signing up with a different address from the one the invitation went to, and that is only
 * obvious once you can see which one you used.
 */
function NotInvited({ email, onSignOut }: { email?: string | undefined; onSignOut: () => void }) {
  return (
    <Splash>
      <Panel>
        <div style={{ padding: 24, display: 'grid', gap: 14, gridTemplateColumns: 'minmax(0, 1fr)', maxWidth: '46ch', textAlign: 'left' }}>
          <h1 style={{ margin: 0, fontSize: 22 }}>Not yet invited</h1>
          <p style={{ margin: 0, color: 'var(--muted)' }}>
            You are signed in{email !== undefined ? ' as ' : ''}
            {email !== undefined && <b style={{ color: 'var(--fg)' }}>{email}</b>}, but this address has not been
            invited to Car Tracker yet, so there is no garage behind it.
          </p>
          <p style={{ margin: 0, color: 'var(--muted)' }}>
            If you were invited, it may have gone to a different address — sign out and sign in again with that
            one. Nothing has been created for this address.
          </p>
          <div>
            <Btn variant="ghost" onClick={onSignOut}>
              Sign out
            </Btn>
          </div>
        </div>
      </Panel>
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
