# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-09-public-landing-page/spec.md

## Technical Requirements

### Where it goes, and what must not move

`AuthGate` (`src/auth/AuthGate.tsx`) has three branches. Only the third changes:

| Branch | Condition | Today | After |
|---|---|---|---|
| Loading | `isLoading \|\| (isAuthenticated && !tokenReady)` | `<Splash>Checking your session…</Splash>` | unchanged |
| Authenticated | `isAuthenticated` | `<>{children}</>` | unchanged |
| Signed out | otherwise | inline-styled splash, 2 buttons | `<LandingPage />` |

The gate stays **above `RouterProvider`** (`main.tsx:39-41`). That is the security property: nothing renders
until Auth0 confirms a session, so no screen can flash another user's data while a redirect settles. The
consequence to state plainly in review: **the landing page has no route and no URL.** A future `/about` means
moving the gate inside the router, which is a change to the security boundary and needs its own decision.

Deep links keep working: `onRedirectCallback` (`main.tsx:31`) preserves `window.location.pathname`, so
someone who followed `/BT53AKJ/fuel`, signed in from the landing page, still arrives at the fuel log.

`IconSprite` is mounted at `main.tsx:38`, **above** the gate, so icons are already available to the signed-out
page — no extra wiring.

### Real classes, not inline styles

The current splash is built from `style={{…}}` objects. That is not a CSP violation — React writes through
CSSOM, which `style-src` does not govern, so `theme-csp.ts:57`'s claim is only loosely wrong. The reason to
use classes anyway is `tokens.test.ts`, which mechanically forbids colour literals and raw palette names in
component CSS and **cannot see inline styles at all**. A marketing page is precisely where an off-palette hex
gets typed, so the new styles belong in `components.css` where the guard reaches them.

### The hero

Follow `.g-hero` (`components.css:1216-1248`) — the closest precedent, being the one band that has no plate
and no vehicle:

- Full-bleed `background: var(--head-bg)` with `color: var(--head-fg)`. `--head-bg` is dark in **both**
  themes, so the band does not invert.
- `<Contours variant="hero" />` for the signature texture (`aria-hidden`, `--sand` strokes, opacity .14).
- An `.eyebrow` caption (`components.css:928`, mono 11px, `letter-spacing: .26em`, `--sand`).
- An `h1` in `var(--disp)`, uppercase, `clamp(30px, 5vw, 46px)`, with a `.thin` child as the deck — Oswald is
  `font-display: block` deliberately (`fonts.css:20-22`) because it carries the identity above the fold.
- The band sits **outside** `<Wrap>` and puts a `<Wrap>` inside itself — the pattern `GaragePage.tsx:38` uses.

### The CTA contrast trap

`.btn` is `background: var(--fg); color: var(--bg)` (`components.css:771-792`). On the dark `--head-bg` band
in **light** theme that is dark-green on dark-green — near-invisible. The app has never put a `.btn` on a hero
band, so there is no existing precedent to copy.

Resolve it explicitly, one of two ways, and check both themes either way:

1. An on-dark modifier that pins the button to `--head-fg`/`--head-bg` rather than the theme-flipping pair; or
2. CTAs placed below the band on `--bg`, where the default `.btn` already works.

This is the single most likely thing to look correct on a dark-theme developer machine and be unreadable for
a light-theme visitor.

### Screenshots

`docs/images/` is **not served**: Vite's `publicDir` is `src/CarTracker.WebApp/public/` (which holds only
`favicon.svg`), and `.dockerignore:20` excludes `docs` from the build context entirely. The failure mode if
this is got wrong is nasty — an unresolved `/images/x.png` falls through `UseStaticFiles`, misses the YARP
routes, hits `MapFallbackToFile` and returns **`index.html` with a 200 and `text/html`**. A broken image with
a success status.

So: copy into `src/assets/screens/` and **import** them, so Vite fingerprints the URL. This is the house rule
`fonts.css:8-12` states after a real incident — `public/` gives a stable URL, and a stable URL for a file
whose content changes is a cache trap. Screenshots are exactly that case; they get re-captured.

Processing, with **Python + Pillow 12.3** (verified present — note `convert` on `PATH` is Windows' filesystem
tool, not ImageMagick):

| Source | Now | Treatment |
|---|---|---|
| `garage-desktop.png` | 2880×1800, 301 KB | The only natural landscape (16:10). Downscale to ~1400px wide, WebP. The primary shot. |
| `dashboard-desktop.png` | 2848×4480, 870 KB | A 4.5:1 full-page strip. **Crop to the top viewport** or use deliberately as a tall motif — it cannot be dropped in as a hero image. |
| `fuel-desktop.png` | 2848×2936, 475 KB | ~1:1. Optional second shot; crop if used. |
| `service-desktop.png` | 2848×1886, 348 KB | 3:2. Optional. |

**Budget: ≤120 KB each after conversion**, and report the total bundle delta — these are the app's first
bundled raster assets, and `IconSprite` is inline SVG precisely to avoid a fetch.

Every image needs a real `alt` describing what the screen shows, not "screenshot".

### Responsive

House breakpoints are **900 / 680 / 560** — 900 switches the nav, 560 is where the whole app goes single
column. Use those, not new ones.

Any wrapping flex row this adds (the CTA pair, a feature grid) gets its selector added to
`src/styles/overflow.test.ts`'s `DATA_LENGTH_ROWS` or `MUST_SHRINK` lists. The repo has been bitten twice —
`components.css:523-533` (the 900px nav) and `8b938af` (the filter chips) — and the guard exists so there is
not a third. For any card grid, use `minmax(min(330px, 100%), 1fr)`: a bare `330px` floor overflowed every
360px phone, which `components.css:1251` records.

### Tests

- A new exported `LandingPage` **fails `coverage.test.ts:90-123`** unless a test naming it runs `axe(`. Sweep
  it in both themes, since the contrast risk is theme-dependent.
- Signed-out rendering needs a **whole-file** `vi.mock('@auth0/auth0-react')` with `vi.hoisted` mutable state.
  The global mock (`test/setup.ts:8-22`) returns a fresh signed-in object per call, and its `vi.fn()`s are new
  spies each render, so it can be neither tweaked nor asserted against. `AuthGate.test.tsx:8-22` is the
  template.
- `AuthGate.test.tsx`'s existing assertions on the splash copy and on the exact `loginWithRedirect` arguments
  need **updating, not deleting** — the argument assertions are the only thing proving `screen_hint` is sent,
  which is the difference between the sign-up button working and silently showing a login form.

### Copy sources

Reuse, do not reinvent: `README.md:3-16` (the problem narrative — the workbook's five wrong figures),
`docs/product/mission.md:53-57` (the two differentiators), `mission-lite.md:3` (the one-sentence version), and
`mission.md:63-81` (a grouped feature list).

**Do not carry forward `GaragePage.tsx:30`'s "Single-user, self-hosted"** — stale since Auth0, and it is fixed
as part of this work.

## External Dependencies (Conditional)

**None at runtime.** Pillow is used once, offline, to prepare the images; it is not added to any manifest and
ships in nothing. No new npm package, no schema, no endpoint, no OpenAPI or generated-types diff.
