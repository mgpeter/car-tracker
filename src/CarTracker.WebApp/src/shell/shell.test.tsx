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
    expect(SCREEN_IDS).toHaveLength(17)
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

  it('substitutes a link where a screen has no write action', () => {
    render(
      <QueryClientProvider client={createQueryClient()}>
        <ThemeProvider>
          <IconSprite />
          <div id="root">
            <AppShell scope={VEHICLE} current="settings" center={{ kind: 'link', screen: 'settings' }}>
              <p>body</p>
            </AppShell>
          </div>
        </ThemeProvider>
      </QueryClientProvider>,
    )
    const bnav = screen.getByRole('navigation', { name: 'Primary mobile' })
    // Settings, vehicle-info and data-integrity have no primary write. The design holds the grid with an
    // inline style="width:68px"; the width lives in CSS now.
    expect(within(bnav).getByRole('link', { name: 'Set' })).toHaveClass('bnav-link')
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
})
