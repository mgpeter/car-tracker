import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { hrefFor } from '../lib/link'
import { __resetScrollLock } from '../lib/useScrollLock'
import { ThemeProvider } from '../theme/ThemeProvider'
import { axe } from '../test/axe'
import { AppShell } from './AppShell'
import { groupedScreens, SCREEN_IDS, SCREENS, TOP_LEVEL, type ScreenId } from './nav'
import type { ShellScope } from './scope'

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })),
  )
})

const VEHICLE: ShellScope = { kind: 'vehicle', reg: 'BT53 AKJ' }

function renderShell(scope: ShellScope = VEHICLE, current: ScreenId = 'dashboard') {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <ThemeProvider>
        <IconSprite />
        <div id="root">
          <AppShell
            scope={scope}
            current={current}
            center={{ kind: 'action', icon: 'plus', label: 'Quick add', onClick: () => {} }}
            footer={<>Every figure is computed on read.</>}
          >
            <p>page body</p>
          </AppShell>
        </div>
      </ThemeProvider>
    </QueryClientProvider>,
  )
}

describe('the nav table', () => {
  it('files every screen under a group', () => {
    // Record<ScreenId, ScreenDef> means this cannot fail at runtime — it fails at compile time. The test
    // documents the guarantee and catches an id that exists but is unreachable.
    // Sixteen since the settings screen was absorbed into vehicle-info. The account screen did not replace
    // it in this table: like the assistant, it is reached from one control in the bar rather than from a
    // menu, so it has a route and a `CurrentScreen` and deliberately no entry here.
    expect(SCREEN_IDS).toHaveLength(16)
    expect(SCREEN_IDS).not.toContain('settings')
    for (const id of SCREEN_IDS) expect(SCREENS[id].group).toBeDefined()
  })

  it('reaches every screen from the mobile sheet', () => {
    // The design's desktop menu omits Garage entirely — reachable only via the brand link, on a
    // multi-vehicle app. Nothing may be unreachable from the only menu a phone has.
    const reachable = groupedScreens({ excludeTopLevel: false }).flatMap((g) => g.ids)
    expect(new Set(reachable)).toEqual(new Set(SCREEN_IDS))
  })

  it('never repeats a top-level screen inside the More panel', () => {
    const inPanel = groupedScreens({ excludeTopLevel: true }).flatMap((g) => g.ids)
    for (const id of TOP_LEVEL) expect(inPanel).not.toContain(id)
  })

  it('gives the garage the only unscoped URL', () => {
    expect(hrefFor('garage')).toBe('/')
    expect(hrefFor('fuel', 'BT53 AKJ')).toBe('/bt53akj/fuel')
    // The registration belongs in the URL (DEC-007). The design has no routing at all — its links are flat
    // filenames and the reg appears only as page content.
    expect(() => hrefFor('fuel')).toThrow(/vehicle-scoped/)
  })

  it('keeps the account screen out of every menu', () => {
    // `hrefFor` returns `/` for ANY screen whose `scoped` is false - it never reads the id - so a screen
    // added to this table as unscoped would silently resolve to the garage. That is the reason the account
    // screen is not in the table at all, and this is the test that says so out loud.
    expect(SCREEN_IDS).not.toContain('account')
    const reachable = groupedScreens({ excludeTopLevel: false }).flatMap((g) => g.ids)
    expect(reachable).not.toContain('account')
  })
})

describe('TopNav', () => {
  it('shows the six top-level links and hides the rest behind More', () => {
    renderShell()
    const nav = screen.getByRole('navigation', { name: 'Primary' })
    for (const id of TOP_LEVEL) {
      expect(within(nav).getByRole('link', { name: SCREENS[id].nav ?? SCREENS[id].label })).toBeInTheDocument()
    }
    expect(within(nav).getByText('More')).toBeInTheDocument()
  })

  // Scoped to the top nav: both navs are always in the DOM and CSS hides one at 900px, so an unscoped query
  // matches "Fuel" twice. jsdom has no layout, so it cannot tell them apart — the scoping is the test's job.
  it('marks the current screen for assistive tech, not just visually', () => {
    renderShell(VEHICLE, 'fuel')
    const nav = within(screen.getByRole('navigation', { name: 'Primary' }))
    // The design marks current with an inline border and NO aria-current in the wash and fuel-log sheets, so
    // a screen-reader user is told nothing about where they are. One prop, one behaviour, everywhere.
    expect(nav.getByRole('link', { name: 'Fuel' })).toHaveAttribute('aria-current', 'page')
    expect(nav.getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')
  })

  it('groups the More panel and reaches Garage from it', async () => {
    const user = userEvent.setup()
    renderShell()
    const nav = within(screen.getByRole('navigation', { name: 'Primary' }))

    // The summary is "More ▾" — the caret is an aria-hidden SVG now, so the accessible name is just "More".
    await user.click(nav.getByText('More'))

    for (const label of ['Daily', 'Records', 'Watch & plan', 'Reference']) {
      expect(nav.getByText(label)).toBeInTheDocument()
    }
    // The gap in the design's desktop menu, closed: Garage was reachable only via the brand link.
    expect(nav.getByRole('link', { name: 'Garage' })).toHaveAttribute('href', '/')
  })

  it('names the theme button by state AND action', () => {
    renderShell()
    // The design's visible "Theme · System" is ambiguous read aloud: is System where you are, or where you
    // are going? The visible text stays; the accessible name says both.
    expect(screen.getByRole('button', { name: /Theme: System\. Change to Light/ })).toBeInTheDocument()
  })

  it('cycles system -> light -> dark', async () => {
    const user = userEvent.setup()
    renderShell()
    await user.click(screen.getByRole('button', { name: /Theme: System/ }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    await user.click(screen.getByRole('button', { name: /Theme: Light/ }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })
})

describe('the garage outlier is structural', () => {
  it('renders no vehicle-scoped links and no bottom nav', () => {
    renderShell({ kind: 'garage' }, 'garage')
    // Not an inconsistency in the design — there is no vehicle to point them at.
    expect(screen.queryByRole('navigation', { name: 'Primary mobile' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Fuel' })).not.toBeInTheDocument()
  })

  it('offers a shortcut back to a vehicle when there is one', () => {
    renderShell({ kind: 'garage', shortcut: { reg: 'BT53 AKJ' } }, 'garage')
    expect(screen.getByRole('link', { name: 'BT53 AKJ · Dashboard' })).toHaveAttribute('href', '/bt53akj/dashboard')
  })
})

describe('BottomNav', () => {
  it('carries the page action in the centre slot with an accessible name', () => {
    renderShell()
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })
    // The FAB's glyph is decorative; the button carries the name. The design already does this correctly on
    // all 13 of its FABs and the port must not regress it.
    expect(within(bnav).getByRole('button', { name: 'Quick add' })).toBeInTheDocument()
  })

  /**
   * The centre badge.
   *
   * It replaced a `status` variant that rendered a warning triangle as a plain `<span>` with `cursor: default`
   * - the one control a thumb reaches for, inert, on exactly the two screens where something was wrong. The
   * badge keeps the alarm and gives the position back its action, so what is worth pinning is that the count
   * reaches the *accessible name* rather than only the pixels: a number nobody can hear is decoration.
   */
  const renderCentre = (badge?: { count: number; tone: 'due' | 'soon' | 'ok' | 'info' }) =>
    render(
      <QueryClientProvider client={createQueryClient()}>
        <ThemeProvider>
          <IconSprite />
          <div id="root">
            <AppShell
              scope={VEHICLE}
              current="checks"
              center={{
                kind: 'action',
                icon: 'plus',
                label: 'Log checks',
                onClick: () => {},
                ...(badge !== undefined && { badge }),
              }}
            >
              <p>body</p>
            </AppShell>
          </div>
        </ThemeProvider>
      </QueryClientProvider>,
    )

  it('folds the attention count into the accessible name of the centre action', () => {
    renderCentre({ count: 3, tone: 'due' })
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })

    expect(within(bnav).getByRole('button', { name: 'Log checks, 3 needing attention' })).toBeInTheDocument()
    // Visible too, and toned - but announced once, through the name above, not twice.
    expect(bnav.querySelector('.bplus-badge')).toHaveTextContent('3')
    expect(bnav.querySelector('.bplus-badge')).toHaveClass('tone-due')
  })

  it('shows no badge when there is nothing to say', () => {
    renderCentre()
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })

    expect(within(bnav).getByRole('button', { name: 'Log checks' })).toBeInTheDocument()
    expect(bnav.querySelector('.bplus-badge')).toBeNull()
  })

  it('shows no badge for a count of zero', () => {
    // A badge reading 0 is noise, and one that appears at zero trains people to ignore the position.
    renderCentre({ count: 0, tone: 'ok' })
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })

    expect(bnav.querySelector('.bplus-badge')).toBeNull()
    expect(within(bnav).getByRole('button', { name: 'Log checks' })).toBeInTheDocument()
  })

  it('substitutes a link where a screen has no write action', () => {
    render(
      <QueryClientProvider client={createQueryClient()}>
        <ThemeProvider>
          <IconSprite />
          <div id="root">
            <AppShell scope={VEHICLE} current="vehicle-info" center={{ kind: 'link', screen: 'vehicle-info' }}>
              <p>body</p>
            </AppShell>
          </div>
        </ThemeProvider>
      </QueryClientProvider>,
    )
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })
    // Vehicle-info and data-integrity have no *single* primary write. The design holds the grid with an
    // inline style="width:68px"; the width lives in CSS now. (This was the settings screen until it was
    // absorbed; vehicle-info inherited the slot along with its eight editors, no one of which is the action
    // the screen is for.)
    expect(within(bnav).getByRole('link', { name: 'Ref' })).toHaveClass('bnav-link')
  })

  it('renders no bottom bar on the account screen', () => {
    render(
      <QueryClientProvider client={createQueryClient()}>
        <ThemeProvider>
          <IconSprite />
          <div id="root">
            <AppShell scope={{ kind: 'account' }} current="account" center={null}>
              <p>body</p>
            </AppShell>
          </div>
        </ThemeProvider>
      </QueryClientProvider>,
    )
    // Three of its five slots are vehicle-scoped links, and the account screen is about no car at all - the
    // same conclusion the garage reaches, and for the same reason.
    expect(screen.queryByRole('navigation', { name: 'Primary mobile' })).not.toBeInTheDocument()
  })

  it('opens the All screens sheet', async () => {
    const user = userEvent.setup()
    renderShell()
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })
    await user.click(within(bnav).getByRole('button', { name: 'More' }))

    const sheet = screen.getByRole('dialog', { name: 'All screens' })
    // One sheet, replacing three stylings of the same list across the design.
    expect(within(sheet).getByText('Daily')).toBeInTheDocument()
    expect(within(sheet).getByRole('link', { name: /Mileage readings/ })).toBeInTheDocument()
  })
})

describe('AppShell', () => {
  it('renders the page body and footer', () => {
    renderShell()
    expect(screen.getByText('page body')).toBeInTheDocument()
    expect(screen.getByText('Every figure is computed on read.')).toBeInTheDocument()
  })

  it('names the build under the footer prose', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ version: '0.0.0-test' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
      ),
    )
    renderShell()

    expect(await screen.findByText('cambelt.app v0.0.0-test')).toBeInTheDocument()
    // Its own paragraph. Appending it to the prose would break the exact-text assertion above, and every
    // screen test that matches its footer the same way.
    expect(screen.getByText('Every figure is computed on read.')).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = renderShell()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the More sheet open', async () => {
    const user = userEvent.setup()
    renderShell()
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })
    await user.click(within(bnav).getByRole('button', { name: 'More' }))
    expect(await axe(document.body)).toHaveNoViolations()
  })

  describe('the assistant', () => {
    /**
     * Configured and entitled, with a registration that differs from its own URL slug - which is every
     * registration.
     *
     * **Two responses, because the assistant needs two different facts to be true.** `/api/meta` says this
     * deployment holds a model credential; `/api/meta/authenticated` says this account's plan includes the
     * assistant. The authenticated path is matched first: it is a prefix of the other, and testing them the
     * other way round would silently answer both with the deployment's response.
     */
    function mockMeta({ chatConfigured = true, chatEnabled = true } = {}) {
      vi.stubGlobal(
        'fetch',
        vi.fn(async (url: string | URL) => {
          const href = String(url)
          const body = href.includes('/api/meta/authenticated')
            ? { authenticated: true, plan: chatEnabled ? 'Pro' : 'Free', allowances: ALLOWANCES(chatEnabled) }
            : href.includes('/api/meta')
              ? { chatConfigured }
              : href.includes('/summary')
                ? { registration: 'BT53 AKJ', name: 'Land Rover Freelander' }
                : {}
          return new Response(JSON.stringify(body), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          })
        }),
      )
    }

    const ALLOWANCES = (chatEnabled: boolean) => ({
      chatEnabled,
      dailyChatTokens: chatEnabled ? 1_000_000 : 0,
      maxDocuments: chatEnabled ? 2000 : 100,
      dailyVehicleLookups: chatEnabled ? 50 : 3,
    })

    it('is reachable from the bar, and names the car as it is written on the car', async () => {
      // The shell knows the vehicle as its URL slug. No British plate reads "BT53AKJ", and the guard that
      // catches this on screens (`plate={reg}`) does not look at the shell.
      mockMeta()
      renderShell({ kind: 'vehicle', reg: 'bt53akj' })

      await userEvent.click(await screen.findByRole('button', { name: 'Open the assistant' }))

      const panel = await screen.findByRole('region', { name: 'Assistant' })
      expect(await within(panel).findByText('BT53 AKJ')).toBeInTheDocument()
    })

    it('has no axe violations with the ChatDock open over a screen', async () => {
      mockMeta()
      const { container } = renderShell({ kind: 'vehicle', reg: 'bt53akj' })

      await userEvent.click(await screen.findByRole('button', { name: 'Open the assistant' }))
      await screen.findByRole('region', { name: 'Assistant' })

      expect(await axe(container)).toHaveNoViolations()
    })

    it('is also reachable from the sheet, which is the way in on a phone', async () => {
      // Below 900px the bar sheds its links and `<BottomNav>` takes over; the sheet is the labelled route in.
      mockMeta()
      renderShell({ kind: 'vehicle', reg: 'bt53akj' })

      await userEvent.click(screen.getByRole('button', { name: 'More' }))

      const link = await screen.findByRole('link', { name: /ask about this car/i })
      expect(link).toHaveAttribute('href', '/bt53akj/assistant')
    })

    it('offers nothing when the deployment has no model credential', async () => {
      // Strictly `=== true`: an in-flight meta hides the control rather than offering one that 503s.
      mockMeta({ chatConfigured: false })

      renderShell()

      await screen.findByRole('navigation', { name: 'Primary' })
      expect(screen.queryByRole('button', { name: 'Open the assistant' })).not.toBeInTheDocument()
    })

    it('offers nothing when the account is on a plan without the assistant', async () => {
      // The other half, and it is a genuinely different fault: the deployment is configured and every other
      // account can use the chat. Rendering the button here would give a free account a control answering 403.
      mockMeta({ chatEnabled: false })

      renderShell({ kind: 'vehicle', reg: 'bt53akj' })

      await screen.findByRole('navigation', { name: 'Primary' })
      expect(screen.queryByRole('button', { name: 'Open the assistant' })).not.toBeInTheDocument()

      await userEvent.click(screen.getByRole('button', { name: 'More' }))
      expect(screen.queryByRole('link', { name: /ask about this car/i })).not.toBeInTheDocument()
    })

    it('offers nothing while the plan is still in flight', async () => {
      // A never-resolving fetch, which is what the first paint actually looks like. `undefined` must read as
      // "no" here: the alternative is a button that appears, is pressed, and 403s a fifth of a second later.
      vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))

      renderShell({ kind: 'vehicle', reg: 'bt53akj' })

      await screen.findByRole('navigation', { name: 'Primary' })
      expect(screen.queryByRole('button', { name: 'Open the assistant' })).not.toBeInTheDocument()
    })
  })
})
