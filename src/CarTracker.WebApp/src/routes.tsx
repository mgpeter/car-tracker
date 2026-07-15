import { createContext, use, type ReactNode } from 'react'
import { createBrowserRouter, Link, Outlet, useParams } from 'react-router-dom'
import { Wrap } from './components/layout'
import { GalleryPage } from './gallery/Gallery'
import { LinkProvider } from './lib/link'
import { DashboardPage } from './screens/DashboardPage'
import { FuelLogPage } from './screens/FuelLogPage'
import { GaragePage } from './screens/GaragePage'
import { SettingsPage } from './screens/SettingsPage'
import { SCREEN_IDS, type ScreenId } from './shell/nav'

/**
 * The current vehicle, read from the route.
 *
 * There is **no global current-vehicle store**, deliberately. A store is a second source of truth for
 * something the URL already says, and the two go out of step the moment someone opens a second tab or follows
 * a link — you end up looking at BT53's dashboard with another car's figures. The URL is the state; this
 * just reads it.
 */
const VehicleContext = createContext<string | null>(null)

export function useVehicleReg(): string {
  const reg = use(VehicleContext)
  if (reg === null) throw new Error('useVehicleReg outside a vehicle-scoped route')
  return reg
}

/** Exported so a vehicle-scoped screen can be tested without standing up the whole router. */
export function VehicleProvider({ children }: { children: ReactNode }) {
  const { reg } = useParams<{ reg: string }>()
  if (reg === undefined) throw new Error('vehicle route without a :reg param')
  return <VehicleContext value={reg}>{children}</VehicleContext>
}

/**
 * Hands the shell React Router's `<Link>`.
 *
 * Task 4 built the whole shell against a `LinkProvider` whose default renderer is a plain `<a>`, and had it
 * emit this URL shape (`/`, `/:reg/fuel`) from the start — so this is the swap it was designed for: a
 * renderer, not a rewrite. It also means every component test still runs with no router at all.
 */
function RouterLinks({ children }: { children: ReactNode }) {
  return (
    <LinkProvider
      render={({ href, children: inner, ...rest }) => (
        <Link to={href} {...rest}>
          {inner}
        </Link>
      )}
    >
      {children}
    </LinkProvider>
  )
}

function Root() {
  return (
    <RouterLinks>
      <Outlet />
    </RouterLinks>
  )
}

/** Until a screen lands (M1), the route exists and says so honestly rather than 404ing. */
function NotBuiltYet({ screen }: { screen: ScreenId }) {
  return (
    <Wrap>
      <section>
        <h1 style={{ fontFamily: 'var(--disp)', textTransform: 'uppercase' }}>{screen}</h1>
        <p style={{ color: 'var(--muted)' }}>
          This screen is not built yet. The route and the data layer are — see the component gallery for the
          vocabulary it will be built from.
        </p>
        <Link className="mark" to="/gallery">
          Component gallery
        </Link>
      </section>
    </Wrap>
  )
}

export const router = createBrowserRouter([
  {
    path: '/',
    element: <Root />,
    children: [
      // The garage: the only unscoped screen, because it is where you are before choosing a vehicle.
      { index: true, element: <GaragePage /> },
      { path: 'gallery', element: <GalleryPage /> },
      {
        path: ':reg',
        element: (
          <VehicleProvider>
            <Outlet />
          </VehicleProvider>
        ),
        children: [
          // Every vehicle-scoped screen, from the one nav table — so a screen cannot exist in the menu and
          // 404 on click, or be routable and unreachable.
          { path: 'dashboard', element: <DashboardPage /> },
          { path: 'fuel', element: <FuelLogPage /> },
          { path: 'settings', element: <SettingsPage /> },
          ...SCREEN_IDS.filter((id) => id !== 'garage' && id !== 'settings' && id !== 'dashboard' && id !== 'fuel').map((id) => ({
            path: id,
            element: <NotBuiltYet screen={id} />,
          })),
        ],
      },
    ],
  },
])
