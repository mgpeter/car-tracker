import { useState } from 'react'
import type { GarageItem } from '../api/client'
import { useGarage } from '../api/queries'
import { Contours } from '../components/Contours'
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
  const { data, isPending, isError, error } = useGarage()

  // The shortcut back to a car, so the top nav is not a dead end on the way in. The default vehicle if there
  // is one, else the first — the same order the API returns.
  const shortcut = data?.[0]

  return (
    <AppShell
      scope={shortcut ? { kind: 'garage', shortcut: { reg: shortcut.registration } } : { kind: 'garage' }}
      current="garage"
      footer={
        <>
          Single-user, self-hosted. A vehicle is a scope: its logs, check definitions, budgets and reference
          data live together, and <b>every derived figure is computed at render</b>. Adding a car starts a
          fresh, empty scope — nothing is shared between vehicles except your settings.
        </>
      }
    >
      <header className="g-hero">
        <Contours variant="hero" />
        <Wrap className="g-hero-in">
          <div className="eyebrow">Car Tracker · self-hosted</div>
          <h1>
            The Garage
            <span className="thin">
              {isPending
                ? 'loading…'
                : `${data?.length ?? 0} ${data?.length === 1 ? 'vehicle' : 'vehicles'} tracked · every figure computed live from its logs`}
            </span>
          </h1>
        </Wrap>
      </header>

      <Wrap>
        <section className="last">
          <SectionHead title="Vehicles" rule={<>pick a car — all screens scope to it</>} />

          {isError && <GarageError message={error instanceof Error ? error.message : 'Unknown error'} />}

          {/* Pending is not empty. Rendering the add-car prompt while the request is in flight would tell
              someone with a car that they have none. */}
          {isPending && <p style={{ color: 'var(--muted)' }}>Loading the garage…</p>}

          {data && (
            <div className="cars">
              {data.map((item: GarageItem) => (
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
 * The three failures need three answers.
 *
 * `apiFetch` keeps them apart deliberately — a missing API key and a dead server look identical in a generic
 * "something went wrong", and only one of them is fixable by the person reading it.
 */
function GarageError({ message }: { message: string }) {
  const unauthorized = message === 'Unauthorized'

  return (
    <div className="panel" style={{ padding: '18px', borderColor: 'var(--due)' }}>
      <p style={{ margin: 0 }}>
        {unauthorized
          ? 'The API rejected the key. Check it in Settings.'
          : `Could not reach the API — ${message}`}
      </p>
    </div>
  )
}
