import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ComponentProps } from 'react'
import { createQueryClient } from '../api/queries'
import { axe } from '../test/axe'
import { LandingPage } from './LandingPage'

// No Auth0 mock anywhere in this file, deliberately: LandingPage takes two callbacks and an optional error,
// so the page can be tested as the presentational thing it is. AuthGate keeps the auth knowledge, and
// AuthGate.test keeps the assertions on what loginWithRedirect is actually called with.
const noop = () => {}

/**
 * `GET /api/meta` - the anonymous build metadata, and the only call this page makes. It reaches here through
 * the shared `Footer`, which names the build; a signed-out visitor sends it with no bearer at all.
 */
const META = {
  applicationName: 'CarTracker',
  version: '0.0.0-test',
  environment: 'Test',
  serverTimeUtc: '2026-08-21T00:00:00.000Z',
  identityDeletionConfigured: false,
  vehicleLookupConfigured: false,
  chatConfigured: false,
}

/**
 * Stubbed for the whole file rather than per test: the page acquired its first fetch when the footer started
 * naming the build, and a test that left it unstubbed would make a real request out of jsdom.
 */
beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(META), { status: 200, headers: { 'Content-Type': 'application/json' } })),
  )
})

/**
 * `LandingPage` under a query client, because `Footer` reads the version from `useMeta()`.
 *
 * Every test in this file used to render the page bare, which was the honest shape while it touched no API at
 * all. Wrapping here rather than at 15 call sites keeps each test reading as what it asserts.
 */
function Landing(props: ComponentProps<typeof LandingPage>) {
  return (
    <QueryClientProvider client={createQueryClient()}>
      <LandingPage {...props} />
    </QueryClientProvider>
  )
}

afterEach(() => {
  document.documentElement.removeAttribute('data-theme')
  vi.unstubAllGlobals()
})

describe('LandingPage', () => {
  it('names the product and says what it does', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)

    // The test has always been titled "names the product" and asserted only that an h1 existed - which stayed
    // green through a rename. The name is the one string a visitor has to leave with.
    expect(screen.getByText('cambelt.app')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()
    // The claim the whole product rests on, said in words a car owner can check against their own experience.
    expect(screen.getByText(/worked out fresh/i)).toBeInTheDocument()
  })

  it('does not call the product by its old name', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)
    // Separate from the guard below because this is not jargon, it is a wrong name - and it would read as
    // perfectly good copy to anyone reviewing the page without knowing the product had been renamed.
    expect(screen.getByRole('main').textContent ?? '').not.toMatch(/car tracker/i)
  })

  it('has exactly one h1', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)
    // A marketing page is where heading order quietly rots; the rest must be h2 and below.
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  })

  it('offers both ways in, at the top and again at the foot', async () => {
    const onLogIn = vi.fn()
    const onSignUp = vi.fn()
    const user = userEvent.setup()
    render(<Landing onLogIn={onLogIn} onSignUp={onSignUp} />)

    // Twice each, deliberately: someone who reads to the bottom should not have to scroll back up.
    const signUps = screen.getAllByRole('button', { name: /sign up/i })
    const logIns = screen.getAllByRole('button', { name: /log in/i })
    expect(signUps).toHaveLength(2)
    expect(logIns).toHaveLength(2)

    await user.click(signUps[0]!)
    expect(onSignUp).toHaveBeenCalledOnce()

    await user.click(logIns[1]!)
    expect(onLogIn).toHaveBeenCalledOnce()
  })

  it('says sign-up is open, beside both sign-up buttons', () => {
    const { container } = render(<Landing onLogIn={noop} onSignUp={noop} />)

    // Both places the page offers sign-up carry a note about the door, or the reader who scrolled past the
    // hero never hears it. The default is open, and it stays the default while `meta` is in flight.
    expect(container.querySelectorAll('.lp-cta-note')).toHaveLength(2)
    expect(screen.getByText(/free to sign up, and your garage is private/i)).toBeInTheDocument()

    // The claim that was false for a fortnight after DEC-022 flipped `Signup:Mode` to Open. This page told
    // every visitor to cambelt.app that they needed an invitation they did not need.
    expect(screen.getByRole('main').textContent ?? '').not.toMatch(/invitation/i)
  })

  it('says access is by invitation when the deployment is invitation-only', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} inviteOnly />)

    // An uninvited address gets through Auth0 and is refused after it. Saying so before the click is the
    // difference between a closed door and a wasted five minutes - and it is said in both places, for the
    // same reason the open copy is.
    const notes = screen.getAllByText(/by invitation/i)
    expect(notes).toHaveLength(2)

    // Which address matters: Auth0 will happily create an identity under one we have never heard of.
    expect(screen.getByText(/sign up with the address the invitation went to/i)).toBeInTheDocument()
  })

  it('surfaces an Auth0 failure without losing the page', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} error="Something went wrong" />)

    expect(screen.getByRole('alert')).toHaveTextContent(/something went wrong/i)
    // The pitch and the buttons survive the error - a failed redirect must not strand someone on a bare message.
    expect(screen.getAllByRole('button', { name: /sign up/i }).length).toBeGreaterThan(0)
  })

  it('renders no alert when there is no error', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('describes each screenshot rather than calling it a screenshot', () => {
    const { container } = render(<Landing onLogIn={noop} onSignUp={noop} />)

    const images = [...container.querySelectorAll('img')]
    expect(images.length).toBeGreaterThan(0)
    for (const img of images) {
      const alt = img.getAttribute('alt') ?? ''
      expect(alt.length, 'every image needs a real description').toBeGreaterThan(12)
      // "Screenshot of the dashboard" tells a screen-reader user nothing they could not infer from context.
      expect(alt.toLowerCase()).not.toMatch(/^(a )?screenshot/)
    }
  })

  it('lets the images shrink, since they sit in a fluid column', () => {
    const { container } = render(<Landing onLogIn={noop} onSignUp={noop} />)
    for (const img of container.querySelectorAll('img')) {
      // Intrinsic width/height so the browser reserves the box and the page does not jump as they decode.
      expect(img.getAttribute('width'), 'width prevents layout shift').not.toBeNull()
      expect(img.getAttribute('height'), 'height prevents layout shift').not.toBeNull()
    }
  })

  it('has no axe violations in light theme', async () => {
    document.documentElement.setAttribute('data-theme', 'light')
    const { container } = render(<Landing onLogIn={noop} onSignUp={noop} />)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations in dark theme', async () => {
    document.documentElement.setAttribute('data-theme', 'dark')
    const { container } = render(<Landing onLogIn={noop} onSignUp={noop} />)
    // Note: `color-contrast` is disabled in the axe helper because jsdom has no layout engine, so neither of
    // these sweeps can see the contrast risk on the dark hero band. Structure only - the contrast is handled
    // by pinning the on-band CTA to --head-fg/--head-bg rather than the theme-flipping --fg/--bg pair.
    expect(await axe(container)).toHaveNoViolations()
  })

  /**
   * The page is for car owners, not engineers.
   *
   * The first cut of this page was written in the project's own voice and shipped saying "MCP",
   * "self-hosted", "derived" and "a class of bug the schema forecloses" - none of which tells someone who
   * wants to know what their car costs whether any of it is about their car. This is that requirement as a
   * test, because it is the only thing that will stop the house voice creeping back the next time this file
   * is edited.
   */
  it.each([
    [/\bMCP\b/i, 'the protocol name means nothing to a car owner - say "AI assistant"'],
    [/self-hosted/i, 'a deployment detail, not a reason to sign up'],
    [/\bderived\b/i, 'say the figures are worked out fresh, not that they are "derived"'],
    [/\bschema\b/i, 'nobody signing up for a car app knows or cares what a schema is'],
    [/\bdomain service\b/i, 'internal architecture'],
    [/\bregression test/i, 'internal practice'],
    // Added with the invitation copy: the three words the door is built out of on our side, none of which
    // describe anything the reader has to do.
    [/\ballowlist\b/i, 'the mechanism behind the invitation, not the invitation'],
    [/\bprovision/i, 'what the server does when someone is let in; nobody signing up says it'],
    [/\btenant\b/i, 'an Auth0 word, and to a car owner a word about renting a flat'],
  ])('says nothing matching %s', (pattern, why) => {
    // Both doors, because the invitation copy is where the house voice got in last time and it renders on
    // only one of them.
    for (const inviteOnly of [false, true]) {
      const { unmount } = render(<Landing onLogIn={noop} onSignUp={noop} inviteOnly={inviteOnly} />)
      expect(screen.getByRole('main').textContent ?? '', why).not.toMatch(pattern)
      unmount()
    }
  })

  it('links out to the author and the source', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)

    const site = screen.getByRole('link', { name: /usualexpat\.com/i })
    expect(site).toHaveAttribute('href', 'https://usualexpat.com')

    const repo = screen.getByRole('link', { name: /github/i })
    expect(repo.getAttribute('href')).toMatch(/^https:\/\/github\.com\//)

    // No target="_blank": an unexpected new window is an accessibility annoyance, and the visitor can decide.
    for (const link of [site, repo]) expect(link).not.toHaveAttribute('target')
  })

  it('keeps the proof section honest about whose car it is', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)
    // The figures on the screenshots are one real car's. Saying so is the difference between a demo and a claim.
    const main = screen.getByRole('main')
    expect(within(main).getByText(/76,632 miles/)).toBeInTheDocument()
  })

  it('does not promise the assistant is one click', () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)
    // Connecting one currently means a key and a config file on your machine. Saying so on the page is the
    // difference between a feature and a disappointment.
    expect(screen.getByText(/takes a bit of setting up/i)).toBeInTheDocument()
  })

  it('names the build in the footer', async () => {
    render(<Landing onLogIn={noop} onSignUp={noop} />)

    // From GET /api/meta, which needs no account - which is what makes this possible on a page nobody has
    // signed in to. One text node, so getByText('cambelt.app') above still finds only the hero eyebrow.
    expect(await screen.findByText('cambelt.app v0.0.0-test')).toBeInTheDocument()
  })

  it('says nothing about the build until it knows one', async () => {
    // 404 rather than a hang, because the query client deliberately does not retry one - so this settles
    // immediately instead of after two backoffs.
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 404 })))
    render(<Landing onLogIn={noop} onSignUp={noop} />)

    // The failure this pins is `cambelt.app v` or `cambelt.app vundefined` - the shape a version line rots
    // into the moment someone renders it unconditionally.
    await waitFor(() => expect(screen.queryByText(/cambelt\.app v/)).not.toBeInTheDocument())
  })
})
