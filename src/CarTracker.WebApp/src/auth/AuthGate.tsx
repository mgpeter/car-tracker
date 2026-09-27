import { useAuth0 } from '@auth0/auth0-react'
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { setAccessTokenProvider } from '../api/client'
import { ApiFailure, isNotInvited, useAccessCheck, useMeta } from '../api/queries'
import { Btn } from '../components/Btn'
import { Panel } from '../components/layout'
import { LandingPage } from './LandingPage'

/**
 * The login wall. Nothing below it renders until Auth0 confirms a session, so no screen can flash another
 * user's data before a redirect settles.
 *
 * **It is a layout route now, not a wrapper around the router.** Until the legal documents needed real URLs it
 * sat above `RouterProvider`, which made every screen gated by construction; it is now the element of one
 * branch of the route table (`routes.tsx`), with `/privacy`, `/cookies` and `/terms` as its public siblings.
 * That trades a structural guarantee for a positional one, and `routes.gating.test.tsx` is what enforces the
 * replacement: a route nested outside this branch is public, silently, and that test fails the build naming it.
 *
 * It still takes `children` rather than rendering an `<Outlet />` itself, so it remains a component that can be
 * tested in each of its three states without standing up a router. The route supplies the outlet.
 *
 * DEC-024 is the decision, including the two properties of the old arrangement that deliberately survive: this
 * page has no URL, and the token provider is still wired before anything below it renders.
 *
 * **Three states, not two.** A valid Auth0 session is not necessarily an account. Sign-up is open by default
 * since DEC-022, but a deployment running `Signup:Mode=InviteOnly` admits only the addresses on
 * `Signup:AllowedEmails`/`AllowedDomains`, so someone can sign in perfectly and still have no account behind
 * the token. That person gets neither the app — there is nothing in it for them — nor `LandingPage`, which
 * would invite them to sign up for what they have just been refused. They get a short panel that says so, and
 * a way back out.
 *
 * The same setting decides what the signed-out half says: `meta.signupInviteOnly` is passed to `LandingPage`
 * so the copy beside the sign-up buttons and this panel cannot describe two different doors.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { isLoading, isAuthenticated, error, loginWithRedirect, logout, user, getAccessTokenSilently } = useAuth0()
  const queryClient = useQueryClient()

  // Register the access-token getter BEFORE the app renders, and gate the children on it. Otherwise the first
  // data query can fire between mount and this effect - with no bearer - and get a 401 the query layer will not
  // retry.
  //
  // **Wired is not enough; a token has to actually be obtained.** `isAuthenticated` only says the SDK found a
  // user in its localStorage cache. After a few weeks away the refresh token behind that user has expired, the
  // SDK's own `checkSession` swallows the `invalid_grant`, and the cached user still reads as signed in. Every
  // request then went out without a bearer, 401'd, and asked Auth0 again, until the tenant answered 429 and the
  // page sat on this splash for good. So the gate asks for one token before rendering anything that fetches.
  const [token, setToken] = useState<TokenState>('checking')
  const [attempt, setAttempt] = useState(0)
  const [expired, setExpired] = useState(false)
  const ending = useRef(false)

  // One way to end a session the SDK still believes in. `openUrl: false` clears its cache without navigating, so
  // the visitor lands on the sign-in page here rather than on Auth0's logout endpoint. Ref-guarded because
  // several requests in flight can each discover the same dead refresh token.
  const endSession = useCallback(() => {
    if (ending.current) return
    ending.current = true
    setAccessTokenProvider(null)
    queryClient.clear()
    setExpired(true)
    void logout({ openUrl: false })
  }, [logout, queryClient])

  useEffect(() => {
    if (!isAuthenticated) {
      setAccessTokenProvider(null)
      setToken('checking')
      return
    }
    ending.current = false

    // Mid-visit as well as at boot: a tab left open past the refresh token's lifetime ends its session on the
    // first request that cannot get a token, rather than letting every later request 401.
    setAccessTokenProvider(() =>
      getAccessTokenSilently().catch((cause: unknown) => {
        if (isDeadSession(cause)) endSession()
        throw cause
      }),
    )

    let cancelled = false
    setToken('checking')
    getAccessTokenSilently().then(
      () => {
        if (!cancelled) setToken('ready')
      },
      (cause: unknown) => {
        if (cancelled) return
        if (isDeadSession(cause)) endSession()
        else setToken({ failed: cause })
      },
    )

    return () => {
      cancelled = true
      setAccessTokenProvider(null)
    }
  }, [isAuthenticated, getAccessTokenSilently, endSession, attempt])

  const tokenReady = token === 'ready'

  // The first API call the app makes, and the only one made above the router. Enabled only once a token has been
  // obtained, or it would ask the question without a credential and answer it wrongly.
  const access = useAccessCheck(isAuthenticated && tokenReady)

  // Anonymous, and the same cache entry `Footer` fills for the build-version line - so on the signed-in path
  // this is free, and on the signed-out path it is the only request the page makes. Read as `=== true` so an
  // in-flight answer renders the open-door copy, which is the default posture rather than a guess.
  const inviteOnly = useMeta().data?.signupInviteOnly === true

  if (isAuthenticated && typeof token === 'object') {
    // Anything but a dead session: the network, or the tenant rate-limiting. Nothing below renders, so nothing
    // below can keep asking; the visitor decides when to try again.
    return (
      <Splash>
        <p style={{ margin: 0 }}>Could not reach the sign-in service.</p>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', justifyContent: 'center' }}>
          <Btn onClick={() => setAttempt((n) => n + 1)}>Try again</Btn>
          <Btn variant="ghost" onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}>
            Sign out
          </Btn>
        </div>
      </Splash>
    )
  }

  if (isLoading || (isAuthenticated && !tokenReady)) {
    return <Splash>Checking your session…</Splash>
  }

  if (isAuthenticated) {
    if (access.isPending) return <Splash>Checking your session…</Splash>

    if (isNotInvited(access.error)) {
      return (
        <NotInvited
          email={user?.email}
          // The server's own sentence, not ours. It distinguishes three refusals — nobody could read your
          // address, nobody has proved it is yours, or it is yours and not on the list — and they are three
          // different things to do next. This panel used to discard it and assert the third, which sent
          // someone whose deployment had no Management credential off to ask for an invitation they already
          // had. See the comment in AccountProvisioner that this was defeating.
          reason={access.error instanceof ApiFailure ? access.error.message : undefined}
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
      inviteOnly={inviteOnly}
      {...(expired && { notice: 'Your session has expired. Sign in again to carry on.' })}
      {...(error && { error: error.message })}
    />
  )
}

type TokenState = 'checking' | 'ready' | { failed: unknown }

/**
 * The Auth0 error codes that mean the session is over and only a fresh sign-in will do: the refresh token has
 * expired or been revoked (`invalid_grant`), was never stored (`missing_refresh_token`), or the tenant wants the
 * visitor in front of it (`login_required` and its relatives). Anything else - a dropped connection, a 429 - is
 * worth retrying, and ending the session over it would sign people out whenever the tenant hiccupped.
 */
const DEAD_SESSION = new Set([
  'invalid_grant',
  'missing_refresh_token',
  'login_required',
  'consent_required',
  'interaction_required',
])

function isDeadSession(cause: unknown): boolean {
  return (
    typeof cause === 'object' && cause !== null && DEAD_SESSION.has(String((cause as { error?: unknown }).error))
  )
}

/**
 * Signed in, and not admitted.
 *
 * Deliberately plain: no nav, no shell, nothing to explore. It names the address that was refused, because the
 * commonest cause is signing up with a different address from the one the invitation went to, and that is only
 * obvious once you can see which one you used.
 */
function NotInvited({
  email,
  reason,
  onSignOut,
}: {
  email?: string | undefined
  reason?: string | undefined
  onSignOut: () => void
}) {
  return (
    <Splash>
      <Panel>
        <div style={{ padding: 24, display: 'grid', gap: 14, gridTemplateColumns: 'minmax(0, 1fr)', maxWidth: '46ch', textAlign: 'left' }}>
          <h1 style={{ margin: 0, fontSize: 22 }}>Not yet invited</h1>
          {/* The address comes from the ID token, which the browser has and the API does not — the access
              token carries only the subject. So this panel can name an address the server never resolved,
              which is exactly how "could not read your address" used to read as "you were not invited". */}
          <p style={{ margin: 0, color: 'var(--muted)' }}>
            You are signed in{email !== undefined ? ' as ' : ''}
            {email !== undefined && <b style={{ color: 'var(--fg)' }}>{email}</b>}, and there is no garage
            behind it yet.
          </p>
          <p style={{ margin: 0, color: 'var(--muted)' }}>
            {reason ?? 'Nothing has been created for this address.'}
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
