import { Btn } from '../components/Btn'
import { Contours } from '../components/Contours'
import { Wrap } from '../components/layout'
import dashboardShot from '../assets/screens/dashboard.webp'
import garageShot from '../assets/screens/garage.webp'

/**
 * What a signed-out visitor sees.
 *
 * Presentational on purpose: it takes two callbacks and an optional error rather than reaching for `useAuth0`
 * itself, so the auth knowledge stays in one place (`AuthGate`) and this page can be tested without mocking a
 * session. It renders ABOVE the router — `AuthGate` wraps `RouterProvider` so nothing can flash another
 * user's data before a redirect settles — which is also why the page has no URL of its own. A future
 * `/about` means moving the gate inside the router, and that is a change to the security boundary.
 *
 * The copy is assembled from what the project already says well (`README.md`, `docs/product/mission.md`)
 * rather than invented, and every claim on it is one the app can currently keep.
 */
export function LandingPage({
  onLogIn,
  onSignUp,
  error,
}: {
  onLogIn: () => void
  onSignUp: () => void
  error?: string
}) {
  return (
    <main className="lp">
      <header className="lp-hero">
        <Contours variant="hero" />
        <Wrap className="lp-hero-in">
          <div className="eyebrow">Car Tracker · self-hosted</div>
          <h1>
            Every figure computed live
            <span className="thin">
              Maintenance, running costs and what needs doing next — for the cars you actually own
            </span>
          </h1>

          <p className="lp-lede">
            A self-hosted tracker that computes every number from the underlying logs on read, and exposes the
            same data to an AI assistant over MCP. Nothing derived is stored, so no figure can go stale in a
            column.
          </p>

          {error !== undefined && (
            <p className="lp-error" role="alert">
              Sign-in failed: {error}
            </p>
          )}

          {/* The primary invitation is the new account, not the login form: a stranger has no credentials to
              type. `Btn` takes no className, so the on-dark treatment is scoped from the band in CSS
              (`.lp-hero .btn`) — the default is --fg on --bg, which against this band is near-invisible in
              light theme. The second CTA at the foot of the page sits on --bg and needs no override. */}
          <div className="lp-cta">
            <Btn variant="solid" onClick={onSignUp}>
              Sign up
            </Btn>
            <Btn variant="ghost" onClick={onLogIn}>
              Log in
            </Btn>
          </div>
          <p className="lp-cta-note">Free, and your data stays on your own server.</p>
        </Wrap>
      </header>

      <Wrap>
        <section className="lp-sec">
          <h2>The problem is structural, not clerical</h2>
          <p>
            This replaces a 13-sheet spreadsheet. That workbook's dashboard stores its computed figures, and
            five of them are provably wrong: it double-counts every fill, so "total litres pumped" reads
            1,112.94 against a real 556.47. It shows an MOT expiring in 23 days that was superseded by a pass
            logged three weeks earlier. It averages fuel economy over an interval that never happened.
          </p>
          <p>
            None of those are typos. They are what happens when a computed number gets a column to sit in and
            nobody recomputes it. So nothing derived is stored here — current mileage, per-fill MPG, spend
            rollups, cost-per-mile, days-to-renewal, check status, budget variance are all computed on read by
            one service, and the web app and the assistant both call it. A figure cannot disagree with itself
            across surfaces, because there is only one of it.
          </p>
          <p className="lp-note">
            The workbook's five bad figures are kept as regression tests.
          </p>
        </section>

        <section className="lp-sec">
          <h2>What it looks like</h2>
          <p className="lp-sub">
            Real screens, on one real car — BT53 AKJ, a 2003 Land Rover Freelander 1 bought at 76,632 miles.
            Every number on them is derived from its logs at the moment the page rendered.
          </p>

          <figure className="lp-shot">
            <img
              src={dashboardShot}
              width={1400}
              height={875}
              alt="The per-vehicle dashboard: registration plate, odometer, an attention panel reporting nothing overdue, and a renewals table showing MOT, insurance and road tax with day counts."
              loading="lazy"
              decoding="async"
            />
            <figcaption>
              The dashboard. Renewals count down from the logged MOT pass, not a date anyone typed.
            </figcaption>
          </figure>

          <figure className="lp-shot">
            <img
              src={garageShot}
              width={1400}
              height={758}
              alt="The garage home screen: a vehicle card showing odometer, running cost per mile, days to MOT and average fuel economy, beside an add-a-vehicle tile."
              loading="lazy"
              decoding="async"
            />
            <figcaption>The garage. Each car is its own scope — logs, checks, budget and dashboard.</figcaption>
          </figure>
        </section>

        <section className="lp-sec last">
          <h2>Two things that make it different</h2>
          <div className="lp-points">
            <div>
              <h3>The assistant reads the live domain</h3>
              <p>
                The MCP server is hosted in the same application and calls the same service the web UI does.
                So "what needs my attention?" and the dashboard cannot disagree, and a fill logged by voice is
                in the browser on refresh — audited, and attributed to the assistant that wrote it.
              </p>
            </div>
            <div>
              <h3>Derived-never-stored is enforced by the design</h3>
              <p>
                Unlike the spreadsheet it replaces, and unlike trackers that cache totals for speed, no derived
                figure has a column to go stale in. The five defects above are not bugs to fix once but a class
                of bug the schema forecloses.
              </p>
            </div>
          </div>

          <div className="lp-cta lp-cta-end">
            <Btn variant="solid" onClick={onSignUp}>
              Sign up
            </Btn>
            <Btn variant="ghost" onClick={onLogIn}>
              Log in
            </Btn>
          </div>
        </section>
      </Wrap>
    </main>
  )
}
