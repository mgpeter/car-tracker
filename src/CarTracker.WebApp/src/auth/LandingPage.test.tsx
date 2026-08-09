import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { axe } from '../test/axe'
import { LandingPage } from './LandingPage'

// No Auth0 mock anywhere in this file, deliberately: LandingPage takes two callbacks and an optional error,
// so the page can be tested as the presentational thing it is. AuthGate keeps the auth knowledge, and
// AuthGate.test keeps the assertions on what loginWithRedirect is actually called with.
const noop = () => {}

afterEach(() => {
  document.documentElement.removeAttribute('data-theme')
})

describe('LandingPage', () => {
  it('names the product and says what it does', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)

    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()
    // The claim the whole product rests on, said in words a car owner can check against their own experience.
    expect(screen.getByText(/worked out fresh/i)).toBeInTheDocument()
  })

  it('has exactly one h1', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    // A marketing page is where heading order quietly rots; the rest must be h2 and below.
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  })

  it('offers both ways in, at the top and again at the foot', async () => {
    const onLogIn = vi.fn()
    const onSignUp = vi.fn()
    const user = userEvent.setup()
    render(<LandingPage onLogIn={onLogIn} onSignUp={onSignUp} />)

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

  it('surfaces an Auth0 failure without losing the page', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} error="Something went wrong" />)

    expect(screen.getByRole('alert')).toHaveTextContent(/something went wrong/i)
    // The pitch and the buttons survive the error — a failed redirect must not strand someone on a bare message.
    expect(screen.getAllByRole('button', { name: /sign up/i }).length).toBeGreaterThan(0)
  })

  it('renders no alert when there is no error', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('describes each screenshot rather than calling it a screenshot', () => {
    const { container } = render(<LandingPage onLogIn={noop} onSignUp={noop} />)

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
    const { container } = render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    for (const img of container.querySelectorAll('img')) {
      // Intrinsic width/height so the browser reserves the box and the page does not jump as they decode.
      expect(img.getAttribute('width'), 'width prevents layout shift').not.toBeNull()
      expect(img.getAttribute('height'), 'height prevents layout shift').not.toBeNull()
    }
  })

  it('has no axe violations in light theme', async () => {
    document.documentElement.setAttribute('data-theme', 'light')
    const { container } = render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations in dark theme', async () => {
    document.documentElement.setAttribute('data-theme', 'dark')
    const { container } = render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    // Note: `color-contrast` is disabled in the axe helper because jsdom has no layout engine, so neither of
    // these sweeps can see the contrast risk on the dark hero band. Structure only — the contrast is handled
    // by pinning the on-band CTA to --head-fg/--head-bg rather than the theme-flipping --fg/--bg pair.
    expect(await axe(container)).toHaveNoViolations()
  })

  /**
   * The page is for car owners, not engineers.
   *
   * The first cut of this page was written in the project's own voice and shipped saying "MCP",
   * "self-hosted", "derived" and "a class of bug the schema forecloses" — none of which tells someone who
   * wants to know what their car costs whether any of it is about their car. This is that requirement as a
   * test, because it is the only thing that will stop the house voice creeping back the next time this file
   * is edited.
   */
  it.each([
    [/\bMCP\b/i, 'the protocol name means nothing to a car owner — say "AI assistant"'],
    [/self-hosted/i, 'a deployment detail, not a reason to sign up'],
    [/\bderived\b/i, 'say the figures are worked out fresh, not that they are "derived"'],
    [/\bschema\b/i, 'nobody signing up for a car app knows or cares what a schema is'],
    [/\bdomain service\b/i, 'internal architecture'],
    [/\bregression test/i, 'internal practice'],
  ])('says nothing matching %s', (pattern, why) => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    expect(screen.getByRole('main').textContent ?? '', why).not.toMatch(pattern)
  })

  it('links out to the author and the source', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)

    const site = screen.getByRole('link', { name: /usualexpat\.com/i })
    expect(site).toHaveAttribute('href', 'https://usualexpat.com')

    const repo = screen.getByRole('link', { name: /github/i })
    expect(repo.getAttribute('href')).toMatch(/^https:\/\/github\.com\//)

    // No target="_blank": an unexpected new window is an accessibility annoyance, and the visitor can decide.
    for (const link of [site, repo]) expect(link).not.toHaveAttribute('target')
  })

  it('keeps the proof section honest about whose car it is', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    // The figures on the screenshots are one real car's. Saying so is the difference between a demo and a claim.
    const main = screen.getByRole('main')
    expect(within(main).getByText(/76,632 miles/)).toBeInTheDocument()
  })

  it('does not promise the assistant is one click', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    // Connecting one currently means a key and a config file on your machine. Saying so on the page is the
    // difference between a feature and a disappointment.
    expect(screen.getByText(/takes a bit of setting up/i)).toBeInTheDocument()
  })
})
