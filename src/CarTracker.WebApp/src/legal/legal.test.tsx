import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { CLIENT_STORAGE } from '../lib/clientStorage'
import { axe } from '../test/axe'
import { CookiesPage } from './CookiesPage'
import { LegalLoading, LegalPage } from './LegalPage'
import { LegalRoute } from './LegalRoute'
import { PrivacyPage } from './PrivacyPage'
import { TermsPage } from './TermsPage'

/**
 * The three public documents (DEC-024).
 *
 * Two guards here matter more than the rest. **The processor disclosure** must follow this deployment's own
 * capability flags, so an install with no model credential does not tell its users that their photographs go
 * to a model API - which would be false, and which nobody would ever notice was false. And **the controller
 * must come from configuration**, never from a literal, because a committed policy naming somebody else as
 * the controller is the failure the whole `Legal:` polarity exists to prevent.
 */

const PUBLICATION = {
  controllerName: 'Usual Expat Ltd',
  controllerContact: 'privacy@example.test',
  controllerAddress: '1 Test Street, Testville',
  jurisdiction: 'United Kingdom',
  hostingSummary: 'Runs on a virtual machine in the UK South Azure region; backups are held in the same region.',
}

interface MetaOverrides {
  legal?: unknown
  chatConfigured?: boolean
  vehicleLookupConfigured?: boolean
}

function mockMeta({ legal = PUBLICATION, chatConfigured = false, vehicleLookupConfigured = false }: MetaOverrides = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      async () =>
        new Response(
          JSON.stringify({
            applicationName: 'CarTracker',
            version: '0.28.0',
            environment: 'Test',
            serverTimeUtc: '2026-08-23T12:00:00Z',
            chatConfigured,
            vehicleLookupConfigured,
            legal,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
    ),
  )
}

function renderPage(page: React.ReactElement) {
  return render(<QueryClientProvider client={createQueryClient()}>{page}</QueryClientProvider>)
}

/** The rendered text of a document, once `meta` has answered. */
async function textOf(page: React.ReactElement): Promise<string> {
  renderPage(page)
  await screen.findByRole('heading', { level: 1 })
  // Waits for the controller name, which only appears once the anonymous meta call resolves. `findAll`, not
  // `find`: the privacy page names the controller twice, in the contact line and again where it says who to
  // complain to, and `findByText` throws on more than one match.
  await screen.findAllByText(new RegExp(PUBLICATION.controllerName, 'i'))
  return screen.getByRole('main').textContent ?? ''
}

const PAGES = [
  { name: 'Privacy', element: <PrivacyPage />, heading: /privacy/i },
  { name: 'Cookies', element: <CookiesPage />, heading: /cookies/i },
  { name: 'Terms', element: <TermsPage />, heading: /terms/i },
]

beforeEach(() => mockMeta())
afterEach(() => vi.unstubAllGlobals())

describe('the public legal documents', () => {
  for (const page of PAGES) {
    it(`${page.name} renders one h1 naming the document`, async () => {
      renderPage(page.element)
      const headings = await screen.findAllByRole('heading', { level: 1 })
      expect(headings).toHaveLength(1)
      expect(headings[0]!.textContent ?? '').toMatch(page.heading)
    })

    it(`${page.name} names the configured controller`, async () => {
      const text = await textOf(page.element)
      expect(text).toContain(PUBLICATION.controllerName)
      expect(text).toContain(PUBLICATION.controllerContact)
    })

    it(`${page.name} has no axe violations`, async () => {
      const { container } = renderPage(page.element)
      await screen.findByRole('heading', { level: 1 })
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})

describe('the controller is never hardcoded', () => {
  it('appears in no source file, so the self-hoster property cannot regress into a literal', async () => {
    // The one guard here that reads source rather than output. A page that happened to render the right
    // controller in a test while carrying a fallback literal would pass every assertion above.
    const dir = join(process.cwd(), 'src/legal')
    for (const file of ['PrivacyPage.tsx', 'CookiesPage.tsx', 'TermsPage.tsx', 'LegalPage.tsx']) {
      const text = await readFile(join(dir, file), 'utf8')
      expect(text.toLowerCase(), `${file} names a controller in its own source`).not.toContain('usualexpat.com/privacy')
      expect(text, `${file} carries an email literal`).not.toMatch(/[\w.]+@[\w.]+\.\w+/)
    }
  })

  it('renders nothing rather than a heading above blanks when a value is absent', async () => {
    mockMeta({ legal: { ...PUBLICATION, controllerAddress: null, hostingSummary: null } })
    const text = await textOf(<PrivacyPage />)

    expect(text).not.toContain('1 Test Street')
    expect(text.toLowerCase()).not.toContain('azure')
    // And the heading that would have introduced the hosting sentence goes with it.
    expect(screen.queryByRole('heading', { name: /where your data is held/i })).not.toBeInTheDocument()
  })
})

describe('the processors disclosed follow this deployment, not the prose', () => {
  it('names neither Anthropic nor DVLA when this deployment has no credential for either', async () => {
    const text = await textOf(<PrivacyPage />)

    // The property that matters most on a NAS. Telling somebody's household that their photographs go to a
    // model API, on an install holding no model credential, is a false statement in a privacy policy.
    expect(text).not.toContain('Anthropic')
    expect(text).not.toContain('DVLA')
    expect(text).not.toContain('DVSA')
  })

  it('names Anthropic only when the assistant is configured', async () => {
    mockMeta({ chatConfigured: true })
    const text = await textOf(<PrivacyPage />)

    expect(text).toContain('Anthropic')
    expect(text).not.toContain('DVLA')
  })

  it('names DVLA and DVSA only when the lookup is configured', async () => {
    mockMeta({ vehicleLookupConfigured: true })
    const text = await textOf(<PrivacyPage />)

    expect(text).toContain('DVLA')
    expect(text).not.toContain('Anthropic')
  })

  it('always names the identity provider, because there is no deployment without one', async () => {
    const text = await textOf(<PrivacyPage />)
    expect(text).toContain('Auth0')
  })
})

describe('the cookie notice', () => {
  it('leads with the fact that no cookies are set, which is the unusual part', async () => {
    const text = await textOf(<CookiesPage />)
    expect(text).toMatch(/no cookies/i)
  })

  it('lists every key the registry declares, so the page cannot drift from what is stored', async () => {
    const text = await textOf(<CookiesPage />)
    // The whole reason lib/clientStorage.ts exists. A hand-written list here would go stale the first time
    // somebody adds a key, and the staleness would be a false statement in a published document.
    for (const entry of CLIENT_STORAGE) {
      expect(text, `the notice omits ${entry.key}`).toContain(entry.label)
      expect(text, `the notice omits the purpose of ${entry.key}`).toContain(entry.purpose)
    }
  })

  it('says why there is no consent banner rather than leaving it to be inferred', async () => {
    const text = await textOf(<CookiesPage />)
    expect(text.toLowerCase()).toContain('banner')
  })
})

describe('the terms', () => {
  it('says the app is not a source of statutory truth', async () => {
    const text = await textOf(<TermsPage />)
    // The clause worth having: every figure is worked out from what the owner logged, and a reminder is a
    // convenience rather than a legal notice. Nothing here excuses missing an MOT.
    expect(text.toLowerCase()).toMatch(/mot/)
    expect(text.toLowerCase()).toMatch(/reminder/)
  })

  it('reads the governing law from configuration', async () => {
    mockMeta({ legal: { ...PUBLICATION, jurisdiction: 'Ireland' } })
    renderPage(<TermsPage />)
    expect(await screen.findByText(/Ireland/)).toBeInTheDocument()
  })
})

describe('written for car owners, not for engineers', () => {
  /**
   * `LandingPage.test.tsx`'s guard, applied to the other three pages written for the same reader. It earned
   * its place there by going red on all five terms present in the first cut of that page, and these documents
   * are exactly where the house voice would return: a privacy policy is the most tempting place in any
   * codebase to describe the implementation instead of the effect.
   */
  it.each([
    [/\bMCP\b/i, 'the protocol name means nothing to a car owner'],
    [/\bderived\b/i, 'say the figures are worked out fresh'],
    [/\bschema\b/i, 'internal structure'],
    [/\bquery filter\b/i, 'internal mechanism'],
    [/\bEF Core\b/i, 'internal library'],
    [/\bendpoint\b/i, 'say what it does, not what it is called'],
    [/\bentity\b/i, 'internal vocabulary'],
    [/\bmirror(ed)?\b/i, 'an internal word for a row this app writes for you'],
  ])('says nothing matching %s', async (pattern, why) => {
    for (const page of PAGES) {
      const { unmount } = renderPage(page.element)
      await screen.findByRole('heading', { level: 1 })
      expect(screen.getByRole('main').textContent ?? '', `${page.name}: ${why}`).not.toMatch(pattern)
      unmount()
    }
  })
})

describe('the shared shell', () => {
  it('renders outside AppShell, so a signed-out visitor gets no menu pointing at screens they cannot reach', () => {
    renderPage(<LegalPage title="Privacy" current="privacy" version="2026-08-23">{<p>body</p>}</LegalPage>)
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('sweeps the loading splash too, which is a state a visitor on a slow connection really sees', async () => {
    // Not exempt: it renders on its own for as long as `meta` takes, and a heading with no landmark around it
    // is exactly the kind of thing that passes review by never being looked at.
    const { container } = render(<LegalLoading title="Privacy" />)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('keeps the shared footer, because the other two documents are linked from it', () => {
    renderPage(<LegalPage title="Privacy" current="privacy" version="2026-08-23">{<p>body</p>}</LegalPage>)
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
  })
})

describe('a deployment that publishes nothing', () => {
  /**
   * The self-hoster property, end to end. A NAS install has no `Legal:` configuration, so `meta.legal` is
   * null, and the right answer is not a document with nobody's name on it - it is no document at all.
   */
  function renderRoute() {
    return render(
      <QueryClientProvider client={createQueryClient()}>
        <MemoryRouter initialEntries={['/privacy']}>
          <Routes>
            <Route
              path="/privacy"
              element={
                <LegalRoute title="Privacy">
                  <PrivacyPage />
                </LegalRoute>
              }
            />
            <Route path="/" element={<p>the landing page</p>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )
  }

  it('sends a visitor to the landing page rather than rendering an unowned policy', async () => {
    mockMeta({ legal: null })
    renderRoute()

    expect(await screen.findByText('the landing page')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /privacy/i })).not.toBeInTheDocument()
  })

  it('waits for the answer rather than treating an in-flight meta as unpublished', () => {
    // The distinction that decides whether a slow network bounces somebody off a page that was about to
    // render, which would be invisible on a fast connection and infuriating on a train. While `meta` is in
    // flight the visitor stays on the document, on its splash - they are NOT sent to the landing page.
    //
    // The other half - that it renders once the answer arrives - is what every other test in this file
    // exercises, so it is not re-asserted here through a hand-held promise.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))
    renderRoute()

    expect(screen.queryByText('the landing page')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /privacy/i })).toBeInTheDocument()
  })
})
