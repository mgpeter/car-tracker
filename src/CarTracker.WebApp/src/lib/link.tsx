import { createContext, use, type ReactNode } from 'react'
import { SCREENS, type ScreenId } from '../shell/nav'

/**
 * The URL for a screen.
 *
 * Emits **task 5's shape already** — `/` for the garage, `/:reg/…` for everything else (DEC-007: every entity
 * is vehicle-scoped, and the registration belongs in the URL so a dashboard can be linked to and bookmarked).
 * The design has no routing at all: its links are flat filenames like `fuel-log.dc.html`, and the
 * registration never appears in a URL — only as page content.
 *
 * Getting the shape right now means task 5 swaps the *renderer*, not the URLs.
 */
export function hrefFor(screen: ScreenId, reg?: string): string {
  if (!SCREENS[screen].scoped) return '/'
  if (reg === undefined) {
    throw new Error(`${screen} is vehicle-scoped and needs a registration`)
  }
  return `/${reg.toLowerCase().replace(/\s+/g, '')}/${screen}`
}

type LinkRenderer = (props: {
  href: string
  children: ReactNode
  className?: string
  'aria-current'?: 'page'
  /** For a link whose content is not its name — a whole vehicle card, say. See VehicleCard. */
  'aria-label'?: string
}) => ReactNode

const defaultRenderer: LinkRenderer = ({ href, children, className, ...rest }) => (
  <a href={href} className={className} {...rest}>
    {children}
  </a>
)

const LinkContext = createContext<LinkRenderer>(defaultRenderer)

/**
 * Lets task 5 supply React Router's `<Link>` without task 4 importing the router.
 *
 * `react-router-dom` is already a dependency, so this is not about the package — it is about coupling. If the
 * shell imported `<Link>`, every component test would need a `<MemoryRouter>` around it, and task 4's
 * components would depend on a routing decision that has not been made yet. The default renderer is a plain
 * `<a>`, so tests need no provider at all and the app still navigates (with a full page load) until task 5.
 */
export function LinkProvider({ render, children }: { render: LinkRenderer; children: ReactNode }) {
  return <LinkContext value={render}>{children}</LinkContext>
}

export function useLinkRenderer(): LinkRenderer {
  return use(LinkContext)
}

interface AppLinkProps {
  to: ScreenId
  reg?: string
  className?: string
  /** Only where the link's content is not its name. */
  'aria-label'?: string
  /** Set by the shell, which is told the current screen. Task 5 derives it from the route instead. */
  current?: boolean
  children: ReactNode
}

/**
 * A link to a screen.
 *
 * `aria-current="page"` comes from `current`, and it is not optional decoration: the design marks the current
 * item **visually only** in the wash and fuel-log More sheets — an inline `border-color:var(--accent)` with no
 * `aria-current` — so a screen-reader user is told nothing about where they are. The dashboard's sheet gets it
 * right. Deriving it from one prop means every rendering marks it the same way, and the inconsistency cannot
 * come back.
 */
export function AppLink({ to, reg, className, current = false, children, ...rest }: AppLinkProps) {
  const render = useLinkRenderer()
  const label = rest['aria-label']
  return render({
    href: hrefFor(to, reg),
    ...(className !== undefined && { className }),
    ...(current && { 'aria-current': 'page' as const }),
    ...(label !== undefined && { 'aria-label': label }),
    children,
  })
}
