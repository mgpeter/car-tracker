import { createContext, use, type ReactNode } from 'react'
import { createBrowserRouter, Link, Outlet, useParams, type RouteObject } from 'react-router-dom'
import { AuthGate } from './auth/AuthGate'
import { Wrap } from './components/layout'
import { GalleryPage } from './gallery/Gallery'
import { CookiesPage } from './legal/CookiesPage'
import { LegalRoute } from './legal/LegalRoute'
import { PrivacyPage } from './legal/PrivacyPage'
import { TermsPage } from './legal/TermsPage'
import { LinkProvider } from './lib/link'
import { DashboardPage } from './screens/DashboardPage'
import { BudgetPage } from './screens/BudgetPage'
import { ChecksPage } from './screens/ChecksPage'
import { DocumentsPage } from './screens/DocumentsPage'
import { EquipmentPage } from './screens/EquipmentPage'
import { IssuesPage } from './screens/IssuesPage'
import { TasksPage } from './screens/TasksPage'
import { TyresPage } from './screens/TyresPage'
import { VehicleInfoPage } from './screens/VehicleInfoPage'
import { WashPage } from './screens/WashPage'
import { DataIntegrityPage } from './screens/DataIntegrityPage'
import { ServiceHistoryPage } from './screens/ServiceHistoryPage'
import { ExpensesPage } from './screens/ExpensesPage'
import { FuelLogPage } from './screens/FuelLogPage'
import { MileagePage } from './screens/MileagePage'
import { GaragePage } from './screens/GaragePage'
import { AssistantPage } from './screens/AssistantPage'
import { AccountPage } from './screens/AccountPage'
import { AdminPage } from './screens/AdminPage'
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
 * The screens that exist. Every other id in the nav table renders <NotBuiltYet>, so a screen cannot be in the
 * menu and 404 on click — and this list shrinking to nothing is the goal.
 */
const BUILT: ScreenId[] = [
  'garage', 'dashboard', 'fuel', 'expenses', 'mileage', 'checks', 'service', 'data-integrity',
  'tasks', 'issues', 'tyres', 'wash', 'budget', 'equipment', 'vehicle-info', 'documents',
]

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

/**
 * The route table, exported as data so `routes.gating.test.tsx` can walk it.
 *
 * **The login wall is one of these nodes now, and that is a change to the security boundary.** It used to sit
 * above the router entirely (`<AuthGate><RouterProvider /></AuthGate>`), so no route element was ever
 * constructed for a signed-out visitor and a new screen was gated because everything was. The three legal
 * documents have to be readable without a session and have to have real URLs - a footer link, an Auth0 tenant
 * setting and an address a stranger can be sent all need one - so the gate moved inside.
 *
 * The consequence is positional and is the thing to hold on to: **a route nested under the gated layout is
 * gated; a route added as its sibling is public.** Silently, with nothing failing. That is what
 * `routes.gating.test.tsx` exists to catch, and it lists the public paths explicitly so opening a fourth is a
 * deliberate edit rather than a nesting mistake. If you are adding a screen, it belongs in the gated branch.
 *
 * `LandingPage` still has no URL of its own, exactly as before: it is what the gate renders for a signed-out
 * visitor at any gated path, so `/` is the landing page and `/privacy` is the policy.
 *
 * DEC-024 records why the documents are routes here rather than static pages, and what the trade cost.
 */
export const routeConfig: RouteObject[] = [
  {
    path: '/',
    element: <Root />,
    children: [
      // ── Public. Readable with no session, and the ONLY branch that is. ──────────────────────────────────
      // Pathless, so these keep their top-level URLs; the grouping is what makes the boundary visible in one
      // place rather than spread across three sibling entries.
      {
        children: [
          // LegalRoute sends the visitor to `/` when this deployment publishes nothing, waiting for `meta`
          // to answer first so a slow network is not mistaken for an unpublished deployment.
          {
            path: 'privacy',
            element: (
              <LegalRoute title="Privacy">
                <PrivacyPage />
              </LegalRoute>
            ),
          },
          {
            path: 'cookies',
            element: (
              <LegalRoute title="Cookies">
                <CookiesPage />
              </LegalRoute>
            ),
          },
          {
            path: 'terms',
            element: (
              <LegalRoute title="Terms">
                <TermsPage />
              </LegalRoute>
            ),
          },
        ],
      },
      // ── Gated. Everything else. A route belongs HERE unless it is a public legal document. ──────────────
      {
        element: (
          <AuthGate>
            <Outlet />
          </AuthGate>
        ),
        children: [
          // The garage: the only unscoped screen in the nav table, because it is where you are before choosing a
          // vehicle.
          { index: true, element: <GaragePage /> },
          { path: 'gallery', element: <GalleryPage /> },
          // Not in SCREEN_IDS, and a sibling of :reg rather than a child of it - the account is about the person,
          // so scoping it to a car would be wrong twice: once in the URL and once in the meaning. React Router
          // ranks a static segment above a dynamic one, so this wins over :reg; the cost is that a vehicle
          // registered "ACCOUNT" would be unreachable, which no UK plate format can be.
          { path: 'account', element: <AccountPage /> },
          // The operator surface, a sibling of :reg for the same reason and with the same caveat as `account`
          // above. The permission is enforced by the API on every call it makes; this route is reachable by
          // typing the URL, and lands on a screen whose three panels each report that they could not read.
          { path: 'admin', element: <AdminPage /> },
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
              { path: 'expenses', element: <ExpensesPage /> },
              { path: 'mileage', element: <MileagePage /> },
              { path: 'checks', element: <ChecksPage /> },
              { path: 'service', element: <ServiceHistoryPage /> },
              { path: 'data-integrity', element: <DataIntegrityPage /> },
              { path: 'tasks', element: <TasksPage /> },
              { path: 'issues', element: <IssuesPage /> },
              { path: 'tyres', element: <TyresPage /> },
              { path: 'wash', element: <WashPage /> },
              { path: 'budget', element: <BudgetPage /> },
              { path: 'equipment', element: <EquipmentPage /> },
              { path: 'documents', element: <DocumentsPage /> },
              { path: 'vehicle-info', element: <VehicleInfoPage /> },
              // Not in SCREEN_IDS: the assistant is a docked panel above 900px and a screen below it, reached from
              // the bar rather than from a menu, so it has a route and deliberately no nav entry.
              { path: 'assistant', element: <AssistantPage /> },
              ...SCREEN_IDS.filter((id) => !BUILT.includes(id)).map((id) => ({
                path: id,
                element: <NotBuiltYet screen={id} />,
              })),
            ],
          },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routeConfig)
