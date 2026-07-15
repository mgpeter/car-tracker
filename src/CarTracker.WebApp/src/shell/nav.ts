/**
 * The one nav table. Every rendering derives from this — top nav, More panel, bottom nav, More sheet.
 *
 * The design has THREE competing versions of this list and they disagree:
 *   A. the desktop More panel — Records / Watch & plan / Reference, with Garage absent entirely (reachable
 *      only via the brand link, on a multi-vehicle app);
 *   B. dashboard.dc.html's mobile sheet — Daily / Records / Watch & plan / Reference, Garage first;
 *   C. the other 15 screens' mobile sheets — a flat 17-link grid, no headings.
 *
 * C is B's exact order with the headings deleted, which is strong evidence B is the intended design and C the
 * un-migrated older one. **Owner's call (2026-07-15): B is canonical.** Desktop keeps its six top-level links;
 * the More panel adopts B's groups.
 *
 * `label` / `nav` / `bottom` are not drift — they are three REGISTERS of one screen. "Fuel log" names it in a
 * list, "Fuel" fits a nav bar, "Home" is what the dashboard is called from a phone's bottom bar. Each
 * rendering picks its column.
 */

export type ScreenId =
  | 'garage'
  | 'dashboard'
  | 'fuel'
  | 'checks'
  | 'expenses'
  | 'mileage'
  | 'wash'
  | 'service'
  | 'tasks'
  | 'tyres'
  | 'documents'
  | 'issues'
  | 'budget'
  | 'equipment'
  | 'data-integrity'
  | 'vehicle-info'
  | 'settings'

export type NavGroup = 'daily' | 'records' | 'watch' | 'reference'

export const GROUP_LABELS: Record<NavGroup, string> = {
  daily: 'Daily',
  records: 'Records',
  watch: 'Watch & plan',
  reference: 'Reference',
}

/** Rendering order for the groups. */
export const GROUP_ORDER: readonly NavGroup[] = ['daily', 'records', 'watch', 'reference']

export interface ScreenDef {
  /** In a list or a sheet — the full name. */
  label: string
  /** In the desktop top nav, where space is tight. */
  nav?: string
  /** In the bottom nav's fixed slots. */
  bottom?: string
  group: NavGroup
  /** Present in the desktop top bar rather than behind More. */
  topLevel: boolean
  /**
   * False only for the garage.
   *
   * This is why the garage's nav is structurally different rather than accidentally so: it is the screen you
   * are on *before* choosing a vehicle, so it cannot render vehicle-scoped links. There is no vehicle to
   * scope them to.
   */
  scoped: boolean
}

/**
 * `Record<ScreenId, ScreenDef>` on purpose: add a screen to the union and this fails to compile until it is
 * filed under a group. A screen cannot be reachable-but-unfiled, which is how Garage went missing from the
 * design's desktop menu.
 */
export const SCREENS: Record<ScreenId, ScreenDef> = {
  garage: { label: 'Garage', group: 'daily', topLevel: false, scoped: false },
  dashboard: { label: 'Dashboard', nav: 'Dashboard', bottom: 'Home', group: 'daily', topLevel: true, scoped: true },
  fuel: { label: 'Fuel log', nav: 'Fuel', bottom: 'Fuel', group: 'daily', topLevel: true, scoped: true },
  checks: { label: 'Regular checks', nav: 'Checks', bottom: 'Checks', group: 'daily', topLevel: true, scoped: true },
  expenses: { label: 'Expenses', nav: 'Expenses', group: 'daily', topLevel: true, scoped: true },
  mileage: { label: 'Mileage readings', group: 'daily', topLevel: false, scoped: true },
  wash: { label: 'Wash log', group: 'daily', topLevel: false, scoped: true },
  service: { label: 'Service history', nav: 'Service', group: 'records', topLevel: true, scoped: true },
  tasks: { label: 'Tasks', nav: 'Tasks', group: 'records', topLevel: true, scoped: true },
  tyres: { label: 'Tyre log', group: 'records', topLevel: false, scoped: true },
  documents: { label: 'Documents', group: 'records', topLevel: false, scoped: true },
  issues: { label: 'Issues watchlist', group: 'watch', topLevel: false, scoped: true },
  budget: { label: 'Budget', group: 'watch', topLevel: false, scoped: true },
  equipment: { label: 'Equipment', group: 'watch', topLevel: false, scoped: true },
  // The three screens whose bottom-nav centre slot is a link, not a FAB — they have no primary write action.
  'data-integrity': { label: 'Data integrity', bottom: 'Flags', group: 'watch', topLevel: false, scoped: true },
  'vehicle-info': { label: 'Vehicle info', bottom: 'Ref', group: 'reference', topLevel: false, scoped: true },
  settings: { label: 'Settings', bottom: 'Set', group: 'reference', topLevel: false, scoped: true },
}

export const SCREEN_IDS = Object.keys(SCREENS) as ScreenId[]

/** The six that sit directly in the desktop top bar. */
export const TOP_LEVEL: readonly ScreenId[] = SCREEN_IDS.filter((id) => SCREENS[id].topLevel)

/**
 * Grouped, for the More panel and the More sheet.
 *
 * `excludeTopLevel` is the difference between the two: the desktop panel holds what the top bar does not
 * already show, while the mobile sheet is the *only* menu there is, so it lists everything.
 */
export function groupedScreens(options: { excludeTopLevel: boolean }): Array<{ group: NavGroup; ids: ScreenId[] }> {
  return GROUP_ORDER.map((group) => ({
    group,
    ids: SCREEN_IDS.filter(
      (id) => SCREENS[id].group === group && !(options.excludeTopLevel && SCREENS[id].topLevel),
    ),
  })).filter((g) => g.ids.length > 0)
}
