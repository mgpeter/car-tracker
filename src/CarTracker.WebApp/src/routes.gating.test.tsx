import { isValidElement, type ReactElement } from 'react'
import type { RouteObject } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AuthGate } from './auth/AuthGate'
import { routeConfig } from './routes'

/**
 * The login wall used to be structural. `main.tsx` rendered `<AuthGate><RouterProvider /></AuthGate>`, so no
 * route element was ever constructed for a signed-out visitor and a new screen was gated because *everything*
 * was. Publishing the legal documents at real URLs moved the gate inside the router, and that trades the
 * structural guarantee for a positional one: a route nested under the gated layout is gated, and a route added
 * as its sibling is public, silently, with nothing failing and no error anywhere.
 *
 * This file is what makes that trade safe. It walks the route table as data and asserts that every addressable
 * path is either inside the gated branch or named in {@link PUBLIC_PATHS} - so opening a fourth public route is
 * a deliberate edit to a list with this comment on it, rather than a nesting mistake nobody sees until a
 * stranger reads somebody's garage.
 *
 * **This test is the security boundary now** (DEC-024). Deleting it, or loosening it into something that
 * passes when the walk finds nothing, re-opens exactly what that decision closed.
 *
 * **It was checked by mis-nesting a real screen before it was kept**, the way `AdminReadServiceTests` was
 * checked against an un-widened query: `dashboard` moved out of the gated branch, this file went red naming it,
 * and it was moved back.
 */

/**
 * Every path a signed-out visitor may reach. Adding to this list opens a page to the whole internet; there is
 * no other way to do it, which is the point.
 */
const PUBLIC_PATHS = ['/privacy', '/cookies', '/terms']

/** A route node as it is reachable by URL: what path matches it, and whether the gate stands above it. */
interface FlatRoute {
  path: string
  gated: boolean
}

/**
 * The gate is identified by the component itself rather than by a marker property, so it cannot be spoofed by
 * a route that merely claims to be gated - and so that renaming the file breaks this test rather than
 * silently passing it.
 */
function isGate(element: ReactElement | undefined): boolean {
  return isValidElement(element) && element.type === AuthGate
}

function join(parent: string, segment: string | undefined): string {
  if (segment === undefined) return parent
  if (segment.startsWith('/')) return segment
  return `${parent === '/' ? '' : parent}/${segment}`
}

/**
 * Flattens the route table to the pages a visitor can land on.
 *
 * **Leaves and index routes only.** A route with children is a layout: `Root` renders the `LinkProvider`
 * around an `<Outlet />`, `:reg` renders the vehicle context around one, and the two branch nodes and the gate
 * render nothing of their own. Their paths are prefixes rather than destinations, and counting them was the
 * first version of this test - which then demanded that `Root`, whose entire job is to sit above the gate,
 * be gated.
 *
 * The limit that follows, stated because it is the way this guard could be wrong: a layout route whose element
 * renders real content of its own, rather than only an outlet, would not be checked. Every layout here renders
 * an outlet and nothing else, and one that did otherwise would be a screen wearing a layout's shape.
 */
function flatten(routes: RouteObject[], parentPath = '', gated = false): FlatRoute[] {
  return routes.flatMap((route) => {
    const nowGated = gated || isGate(route.element as ReactElement | undefined)
    const path = route.index === true ? parentPath || '/' : join(parentPath, route.path)
    if (route.children) return flatten(route.children, path, nowGated)
    return [{ path: path || '/', gated: nowGated }]
  })
}

describe('the route table', () => {
  const flat = flatten(routeConfig)

  it('is not empty, so a broken walk cannot pass by finding nothing', () => {
    // Without this the whole file is vacuously green the day `routeConfig` stops being exported in the shape
    // `flatten` expects - which is exactly when it is most needed.
    expect(flat.length).toBeGreaterThan(15)
  })

  it('counts a page once, so a duplicate path cannot hide an ungated twin behind a gated one', () => {
    // `find` returns the first match, so two nodes answering to one path would let a green assertion sit on
    // top of a public duplicate. There are none, and this is what keeps it that way.
    expect(new Set(flat.map((r) => r.path)).size).toBe(flat.length)
  })

  it('gates every path that is not explicitly public', () => {
    const ungated = flat.filter((r) => !r.gated).map((r) => r.path)
    // Compared as sets rather than asserting `every`, so a failure names the paths that escaped instead of
    // reporting that a boolean was false.
    expect([...new Set(ungated)].sort()).toEqual([...PUBLIC_PATHS].sort())
  })

  it('reaches the three legal documents and the garage, so the branches are wired the way the names imply', () => {
    const paths = flat.map((r) => r.path)
    for (const path of PUBLIC_PATHS) expect(paths).toContain(path)
    expect(flat.find((r) => r.path === '/')).toEqual({ path: '/', gated: true })
    expect(flat.find((r) => r.path === '/:reg/fuel')).toEqual({ path: '/:reg/fuel', gated: true })
    expect(flat.find((r) => r.path === '/account')).toEqual({ path: '/account', gated: true })
    expect(flat.find((r) => r.path === '/admin')).toEqual({ path: '/admin', gated: true })
  })
})
