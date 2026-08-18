# Spec Tasks

## Tasks

- [x] 1. Prepare the images before any markup depends on them
  - [x] 1.1 Downscale and convert with Python + Pillow into `src/assets/screens/`: `garage-desktop` to
        ~1400px wide WebP (the only natural landscape), and a **top-viewport crop** of `dashboard-desktop`,
        which is 2848×4480 and cannot be used whole. The dashboard crop (top 1780px of 4480) lands on the
        plate, odometer drum, quick-add and the renewals table - a good hero shot rather than a mid-panel slice
  - [x] 1.2 Verify each output is **≤120 KB** and record the actual sizes - `dashboard.webp` 1400×875
        **51.3 KB**, `garage.webp` 1400×758 **29.5 KB**, total **80.8 KB**. Well inside budget
  - [x] 1.3 Confirm the originals in `docs/images/` are left untouched - `git status` clean for that path

  > **The garage shot needed a second crop, and this is worth recording.** At the full 16:10 frame its footer
  > legibly read *"Single-user, self-hosted"* - the stale line task 5.2 exists to fix. A screenshot asserting
  > single-user, on a page inviting strangers to sign up, would contradict itself in the reader's field of
  > view. Cropped to 1400×758 to drop the footer band. Note this makes the two images different aspect ratios
  > (16:10 and ~1.85:1), so the layout must not assume a uniform shape.

- [x] 2. The landing page component
  - [x] 2.1 Write tests: signed out renders the product name, the pitch and both CTAs; **Sign up calls
        `loginWithRedirect` with `screen_hint: 'signup'`** and Log in with no arguments; an Auth0 `error`
        still surfaces with `role="alert"`; axe passes **in both themes**
  - [x] 2.2 Build `LandingPage` from real classes in `components.css` - never `style={{}}`, because
        `tokens.test.ts`'s colour-literal guard cannot see inline styles and this is exactly the page where an
        off-palette hex gets typed
  - [x] 2.3 Hero on the `.g-hero` pattern: full-bleed `--head-bg`, `<Contours variant="hero" />`, `.eyebrow`
        caption, `var(--disp)` uppercase `h1` with a `.thin` deck, `<Wrap>` inside the band
  - [x] 2.4 **Resolve the CTA contrast trap**: `.btn` is `--fg` on `--bg` and goes near-invisible on the dark
        band in light theme. Either an on-dark modifier or CTAs below the band - and verify in both themes,
        because this is the failure that looks fine on a dark-theme machine
  - [x] 2.5 Copy from `README.md:3-16`, `mission.md:53-57` and `mission-lite.md:3`. Import the images with
        real `alt` text describing the screen, not "screenshot"
  - [x] 2.6 Verify all tests pass

- [x] 3. Wire it into the gate
  - [x] 3.1 Write tests: the **loading** branch still reads "Checking your session…" and the
        **authenticated** branch still renders children - the two things that must not regress
  - [x] 3.2 Replace `AuthGate`'s signed-out `Splash` return with `<LandingPage />`; leave the other two
        branches and the token-provider effect alone. Delete the now-unused private `Splash` only if the
        loading branch no longer needs it
  - [x] 3.3 Update `AuthGate.test.tsx`'s existing assertions rather than deleting them - the
        `loginWithRedirect` argument checks are the only proof `screen_hint` is actually sent
  - [x] 3.4 Verify all tests pass

- [x] 4. Responsive and accessibility
  - [x] 4.1 Use the house breakpoints **900 / 680 / 560**; no new ones
  - [x] 4.2 Add any new wrapping flex row to `src/styles/overflow.test.ts`'s guard lists. Two incidents so far
        (`components.css:523-533`, `8b938af`) and the guard exists so there is not a third
  - [x] 4.3 Any card grid uses `minmax(min(330px, 100%), 1fr)` - a bare `330px` floor overflowed every 360px
        phone, per `components.css:1251`
  - [x] 4.4 Axe sweep in both themes; heading order sane (one `h1`); images carry meaningful `alt`
  - [x] 4.5 Verify all tests pass

- [x] 5. The paperwork, including the release gates
  - [x] 5.1 Annotate `docs/design-brief.md:347` - it forbids exactly this page, and was written before Auth0
  - [x] 5.2 Fix `GaragePage.tsx:30`'s stale "Single-user, self-hosted" footer line
  - [x] 5.3 Add a roadmap entry, **and the three public-release gates**: per-user reference tables
        (`Garage`/`WashLocation` are global, so one user can rename another's), HTTPS (README §6 calls it
        mandatory and the stack serves plain HTTP with a bearer-carrying MCP endpoint), and DEC-016's
        first-user-claims-all-unowned-vehicles. **The page is safe to ship; opening sign-up to strangers is
        not.**
  - [x] 5.4 Update CLAUDE.md

- [x] 6. Prove it
  - [x] 6.1 `npm run build`, then confirm the built HTML references `/assets/…-<hash>.webp`. The failure mode
        is a 200 returning `index.html`, so a broken image reports success - check the reference, not the
        status
  - [x] 6.2 Report the bundle delta from the images
  - [x] 6.3 Full suite, typecheck, build clean; codegen gate expected to show **no contract diff at all**
  - [x] 6.4 Manual, signed out: the page renders in both themes, **Sign up reaches Auth0's registration form**
        and Log in reaches the login form. Signed in: the garage is unchanged and no landing page appears
  - [x] 6.5 At 360px: hero, CTA row and any grid wrap rather than widen the page

  > **Both were done on the deployed build; the spec is closed - confirmed by the owner 2026-08-17.**

  > ⚠️ **6.4 and 6.5 needed the deployed build**, which is why they trailed the rest by a week. Everything
  > automatable was done at ship - 498 tests, typecheck, build, fingerprinted images verified present *and
  > referenced* in `dist`, no contract diff. What was left is everything a test in this project cannot see:
  >
  > - **The CTA contrast on the dark band.** `color-contrast` is disabled in `test/axe.ts` because jsdom has
  >   no layout engine, so neither theme sweep can reach a verdict on it. `.lp-hero .btn` pins to
  >   `--head-fg`/`--head-bg` by design, but design is not proof.
  > - **`screen_hint` actually landing on Auth0's registration form.** The test proves the argument is sent;
  >   only a real redirect proves Auth0 honours it for this tenant's configuration.
  > - **360px.** Third time asking; the guard asserts the CSS declarations exist, not that the row wraps.
