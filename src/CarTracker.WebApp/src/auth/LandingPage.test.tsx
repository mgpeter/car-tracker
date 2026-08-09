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
    // The claim the whole product rests on, and the one a visitor has to understand before signing up.
    expect(screen.getByText(/computed live/i)).toBeInTheDocument()
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

  it('keeps the proof section honest about whose car it is', () => {
    render(<LandingPage onLogIn={noop} onSignUp={noop} />)
    // The figures on the screenshots are one real car's. Saying so is the difference between a demo and a claim.
    const main = screen.getByRole('main')
    expect(within(main).getByText(/BT53 AKJ/)).toBeInTheDocument()
  })
})
