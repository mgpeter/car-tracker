import { useState } from 'react'
import type { GarageItem } from '../api/client'
import { useGarage } from '../api/queries'
import { Contours } from '../components/Contours'
import { FChip } from '../components/Filters'
import { Icon } from '../components/Icon'
import { SectionHead, Wrap } from '../components/layout'
import { VehicleCard } from '../components/VehicleCard'
import { AppShell } from '../shell/AppShell'
import { AddVehicleSheet } from './AddVehicleSheet'

/**
 * The garage — the home screen, and the only unscoped one.
 *
 * `ShellScope` is 'garage' here, which is what makes the nav render two links instead of six and no bottom
 * bar: there is no vehicle to scope them to yet. That is a consequence of where you are, not a special case.
 */
export function GaragePage() {
  const [adding, setAdding] = useState(false)
  const [showArchived, setShowArchived] = useState(false)
  const { data, isPending, isError, error } = useGarage()

  // The API returns every vehicle whatever its status, deliberately: VehicleMetricsLoader says a Sold car's
  // history still answers questions, and that "hiding Sold or SORN is presentation, and the garage surfaces
  // do it". This is the garage surface, and until now it did not.
  const active = data?.filter((v) => v.status === 'Active') ?? []
  const archived = data?.filter((v) => v.status !== 'Active') ?? []
  const shown = showArchived ? [...active, ...archived] : active

  // Not persisted, unlike the theme and the fuel unit. Those are how you read every number forever; this is
  // something you do once to go and look at an old car, and a remembered flag would mean a car you sold is
  // back in your garage in March because you looked at it in January.

  // The shortcut back to a car, so the top nav is not a dead end on the way in. An **active** one by
  // preference: the shortcut is "get me back to my car", and a sold one is not that.
  const shortcut = active[0] ?? data?.[0]

  return (
    <AppShell
      scope={shortcut ? { kind: 'garage', shortcut: { reg: shortcut.registration } } : { kind: 'garage' }}
      current="garage"
      footer={
        <>
          Your garage is yours: each account sees only its own vehicles. A vehicle is a
          scope: its logs, check definitions, budgets and reference data live together, and <b>every derived
          figure is computed at render</b>. Adding a car starts a fresh, empty scope — nothing is shared
          between vehicles except your settings.
        </>
      }
    >
      <header className="g-hero">
        <Contours variant="hero" />
        <Wrap className="g-hero-in">
          <div className="eyebrow">Cambelt</div>
          <h1>
            The Garage
            <span className="thin">
              {isPending
                ? 'loading…'
                : // The headline stays the total. "Tracked" is true of a sold car - that is DEC-007's whole
                  // position - and a hero number that moved when you pressed a filter would be reporting the
                  // filter rather than the garage. The archived count sits beside it so the cards on screen
                  // reconcile with the number above them.
                  `${data?.length ?? 0} ${data?.length === 1 ? 'vehicle' : 'vehicles'} tracked${
                    archived.length > 0 ? ` · ${archived.length} sold or SORN` : ''
                  } · every figure computed live from its logs`}
            </span>
          </h1>
        </Wrap>
      </header>

      <Wrap>
        <section className="last">
          <SectionHead
            title="Vehicles"
            rule={<>pick a car — all screens scope to it</>}
            // No chip at all when nothing is archived: a control with nothing behind it is worse than no
            // control. The count is on the label because it is the entire reason to press it.
            link={
              archived.length === 0 ? undefined : (
                <FChip active={showArchived} onClick={() => setShowArchived(!showArchived)}>
                  {showArchived ? 'Hide archived' : `Show archived (${archived.length})`}
                </FChip>
              )
            }
          />

          {isError && <GarageError message={error instanceof Error ? error.message : 'Unknown error'} />}

          {/* Pending is not empty. Rendering the add-car prompt while the request is in flight would tell
              someone with a car that they have none. */}
          {isPending && <p style={{ color: 'var(--muted)' }}>Loading the garage…</p>}

          {/* Never hide the last thing. A garage of only archived cars would otherwise render the add-car
              prompt to someone with three of them - the same lie the isPending note above guards against. */}
          {data !== undefined && shown.length === 0 && archived.length > 0 && (
            <p style={{ color: 'var(--muted)' }}>
              Every vehicle in your garage is marked Sold or SORN. Show them to open one, or add a car.
            </p>
          )}

          {data && (
            <div className="cars">
              {shown.map((item: GarageItem) => (
                <VehicleCard key={item.vehicleId} item={item} />
              ))}

              <button className="addcar" type="button" onClick={() => setAdding(true)}>
                <span className="plus">
                  <Icon name="plus" />
                </span>
                <span className="t">Add a vehicle</span>
                <span className="s">
                  Each car gets its own logs, checks, budget and dashboard.
                </span>
              </button>
            </div>
          )}
        </section>
      </Wrap>

      <AddVehicleSheet open={adding} onClose={() => setAdding(false)} />
    </AppShell>
  )
}

/**
 * The two failures need two answers.
 *
 * A dead server and a rejected session look identical in a generic "something went wrong", and the reader can
 * only act on one. Since the web app authenticates with the signed-in Auth0 session (not a pasted key), an
 * `Unauthorized` here means the session is not accepted — the fix is to sign out and in again, from the user
 * menu, not to paste anything.
 */
function GarageError({ message }: { message: string }) {
  if (message === 'Unauthorized') {
    return (
      <div className="panel" style={{ padding: '18px', borderColor: 'var(--due)' }}>
        <p style={{ margin: 0 }}>
          Your session is not authorized to read the garage. Sign out and back in from the account menu; if it
          persists, the app may not be registered with your identity provider yet.
        </p>
      </div>
    )
  }

  return (
    <div className="panel" style={{ padding: '18px', borderColor: 'var(--due)' }}>
      <p style={{ margin: 0 }}>Could not reach the API — {message}</p>
    </div>
  )
}
