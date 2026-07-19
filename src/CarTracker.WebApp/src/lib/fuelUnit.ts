import { useSyncExternalStore } from 'react'

/**
 * Fuel-economy display unit — a client preference, like the theme.
 *
 * Display-only by construction: every `FuelEntryMetrics` already carries both `mpg` and `litresPer100Km`,
 * computed server-side, so `28.7 MPG ≡ 9.8 L/100 km` already holds. The toggle only picks which derived value
 * to render; it recomputes nothing and touches no stored data. Persisted in localStorage on the `theme.ts`
 * pattern (safe read → MPG fallback, no-op store on exception, subscribe).
 */
export type FuelUnit = 'mpg' | 'l100'

export const FUEL_UNIT_STORAGE_KEY = 'ct-fuel-unit'

export const UNIT_LABEL: Record<FuelUnit, string> = { mpg: 'MPG', l100: 'L/100km' }

/**
 * MPG × L/100 km, a constant (mirrors `Units.MpgTimesLitresPer100Km`): 4.54609 × 100 ÷ 1.609344 ≈ 282.4809.
 * Because the server computes both per fill from the same miles and litres, converting a summary aggregate this
 * way lands exactly on the per-entry value — the two never disagree.
 */
export const MPG_TIMES_L100 = (4.54609 * 100) / 1.609344

export function isFuelUnit(value: unknown): value is FuelUnit {
  return value === 'mpg' || value === 'l100'
}

function read(): FuelUnit {
  try {
    const raw = localStorage.getItem(FUEL_UNIT_STORAGE_KEY)
    return isFuelUnit(raw) ? raw : 'mpg'
  } catch {
    return 'mpg'
  }
}

let current: FuelUnit = read()
const listeners = new Set<() => void>()

export function getFuelUnit(): FuelUnit {
  return current
}

export function setFuelUnit(unit: FuelUnit): void {
  current = unit
  try {
    localStorage.setItem(FUEL_UNIT_STORAGE_KEY, unit)
  } catch {
    // The choice still applies for this session; it just will not survive a reload.
  }
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** Reactive read: components re-render when the unit changes. */
export function useFuelUnit(): FuelUnit {
  return useSyncExternalStore(subscribe, getFuelUnit, getFuelUnit)
}

/** Test-only: reset the module singleton so a toggle in one test does not leak into the next. */
export function __resetFuelUnit(): void {
  current = 'mpg'
}

/** Convert an MPG aggregate (average/best/worst) to the active unit's value. L/100 km is the inverse scale. */
export function economy(mpg: number | null, unit: FuelUnit): number | null {
  return mpg === null ? null : unit === 'mpg' ? mpg : MPG_TIMES_L100 / mpg
}

/** The per-entry value the server already computed for the active unit — no conversion, no drift. */
export function entryEconomy(entry: { mpg: number | null; litresPer100Km: number | null }, unit: FuelUnit): number | null {
  return unit === 'mpg' ? entry.mpg : entry.litresPer100Km
}

/** A unit-value (already in the active unit) formatted to one decimal, or an em dash when absent. */
export function fmtEconomy(value: number | null): string {
  return value === null ? '—' : value.toFixed(1)
}

/** Lower is better in L/100 km, higher is better in MPG — the good/bad axis inverts with the unit. */
export function lowerIsBetter(unit: FuelUnit): boolean {
  return unit === 'l100'
}
