import { Btn } from '../components/Btn'
import { Contours } from '../components/Contours'
import { Wrap } from '../components/layout'
import { Footer } from '../shell/AppShell'
import dashboardShot from '../assets/screens/dashboard.webp'
import garageShot from '../assets/screens/garage.webp'

/**
 * What a signed-out visitor sees.
 *
 * **Written for car owners, not for engineers.** The first cut of this page was in the project's own voice and
 * said "MCP", "self-hosted", "derived" and "a class of bug the schema forecloses" - none of which tells
 * someone who wants to know what their car costs whether any of it concerns them. `LandingPage.test.tsx`
 * carries a jargon guard so that voice cannot creep back.
 *
 * **Access is by invitation**, and the page says so beside both sign-up buttons. An uninvited address gets as
 * far as an Auth0 login and is then refused, so a page that promised an open door would be spending someone's
 * time to tell them no - say it before the click, not after it.
 *
 * Presentational on purpose: it takes two callbacks and an optional error rather than reaching for `useAuth0`
 * itself, so the auth knowledge stays in one place (`AuthGate`) and this page can be tested without mocking a
 * session. It renders ABOVE the router - `AuthGate` wraps `RouterProvider` so nothing can flash another
 * user's data before a redirect settles - which is also why the page has no URL of its own.
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
          <div className="eyebrow">cambelt.app</div>
          <h1>
            Know what your car costs
            <span className="thin">
              And what it needs next - every figure worked out fresh, every time you open it
            </span>
          </h1>

          <p className="lp-lede">
            Log a fill-up in twenty seconds at the pump. See what the car really costs you per mile. Find out
            the MOT is coming before the reminder letter does.
          </p>

          {error !== undefined && (
            <p className="lp-error" role="alert">
              Sign-in failed: {error}
            </p>
          )}

          {/* Sign-up leads, because someone who has just been invited has no account to log into yet. `Btn`
              takes no className, so the on-dark treatment is scoped from the band in CSS (`.lp-hero .btn`) -
              the default is --fg on --bg, which against this band is near-invisible in light theme. The
              second CTA at the foot of the page sits on --bg and needs no override. */}
          <div className="lp-cta">
            <Btn variant="solid" onClick={onSignUp}>
              Sign up
            </Btn>
            <Btn variant="ghost" onClick={onLogIn}>
              Log in
            </Btn>
          </div>
          <p className="lp-cta-note">
            Access is by invitation at the moment. If you have been invited, sign up with the address the
            invitation went to. It is free, and your garage is private - each account only sees its own cars.
          </p>
        </Wrap>
      </header>

      <Wrap>
        <section className="lp-sec">
          <h2>Why a spreadsheet stops being enough</h2>
          <p>
            This started because a spreadsheet said the MOT was due in three weeks. It had already been done -
            the certificate had been on the kitchen table for a month. The same sheet had the car down for
            about twice the fuel it had actually bought, and a fuel economy figure averaged over a trip that
            never happened.
          </p>
          <p>
            None of that was a typo. It is what happens when a number gets typed into a box once and nothing
            ever works it out again. So this app never keeps a number it can work out for itself. Your
            mileage, cost per mile, what you have spent this year, days until the MOT, when each check is next
            due - all of it is calculated the moment you open the page, from what you actually logged.
          </p>
          <p className="lp-note">
            Correct a fill-up from last March and every figure that depended on it moves with it. You do not
            have to go and find them.
          </p>
        </section>

        <section className="lp-sec">
          <h2>What it looks like</h2>
          <p className="lp-sub">
            Real screens from one real car - a 2003 Land Rover Freelander, bought at 76,632 miles and tracked
            ever since.
          </p>

          <figure className="lp-shot">
            <img
              src={dashboardShot}
              width={1400}
              height={875}
              alt="The dashboard for one car: its registration plate and odometer, a panel saying nothing is overdue, and a list of renewals showing MOT, insurance and road tax with the number of days left on each."
              loading="lazy"
              decoding="async"
            />
            <figcaption>
              Everything the car wants from you, on one screen. The MOT countdown comes from the pass you
              logged, not a date you typed in.
            </figcaption>
          </figure>

          <figure className="lp-shot">
            <img
              src={garageShot}
              width={1400}
              height={758}
              alt="The home screen: a card for one car showing its mileage, running cost per mile, days until the MOT and average fuel economy, next to a tile for adding another car."
              loading="lazy"
              decoding="async"
            />
            <figcaption>
              One card per car. Add a second and it gets its own logs, checks and budget from the start.
            </figcaption>
          </figure>
        </section>

        <section className="lp-sec">
          <h2>What you get</h2>
          <div className="lp-points">
            <div>
              <h3>Nothing to keep up to date</h3>
              <p>
                There is no summary to refresh and no totals to add up again. Log the fill, the service, the
                oil check - the numbers follow on their own, and they are right because they were worked out
                a second ago.
              </p>
            </div>
            <div>
              <h3>It tells you when something looks wrong</h3>
              <p>
                A mileage reading lower than the one before it, a fuel figure that cannot be right, a check
                that has quietly slipped past its date. Flagged for you to look at, never silently swallowed
                and never corrected behind your back.
              </p>
            </div>
            <div>
              <h3>Built around one awkward old car</h3>
              <p>
                Which means it handles the unglamorous parts: tyre tread and pressures, wash and underside
                rinses, the running list of things you are keeping an eye on, and receipts filed against the
                job they belong to.
              </p>
            </div>
            <div>
              <h3>Your data is yours</h3>
              <p>
                Each account only ever sees its own cars. Nothing is sold, nothing is shared, and you can read
                exactly what the app does with what you log - the source is public.
              </p>
            </div>
          </div>
        </section>

        <section className="lp-sec last">
          <h2>Works with an AI assistant</h2>
          <p>
            If you use an assistant like Claude, you can connect it to your garage and simply ask - "what
            needs doing on the Freelander?", or "log 47 litres at 80,900 miles". It reads the same live
            figures the app shows you, so the two can never disagree, and anything it records appears in the
            app straight away.
          </p>
          <p className="lp-note">
            Worth being straight about this one: connecting an assistant takes a bit of setting up today - a
            key from your settings, and a configuration file on your computer. Making it a single button is on
            the list. Everything else here needs nothing but a browser.
          </p>

          <div className="lp-cta lp-cta-end">
            <Btn variant="solid" onClick={onSignUp}>
              Sign up
            </Btn>
            <Btn variant="ghost" onClick={onLogIn}>
              Log in
            </Btn>
          </div>
          {/* Said again down here for the same reason the buttons are: someone who has read this far should
              not have to scroll back up to find out the door is shut. */}
          <p className="lp-cta-note">
            Still by invitation - sign up with the address your invitation went to, or log in if you already
            have an account.
          </p>
        </section>
      </Wrap>

      <Footer>
        Made by <a href="https://usualexpat.com">usualexpat.com</a>. The source is on{' '}
        <a href="https://github.com/mgpeter/car-tracker">GitHub</a> - read exactly what it does with what you
        log.
      </Footer>
    </main>
  )
}
