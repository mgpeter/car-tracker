import { afterEach, describe, expect, it, vi } from 'vitest'
import { restoreAfterSignIn, returnToFrom, safeReturnTo } from './returnTo'

/**
 * Where a visitor goes after signing in. The value rides through Auth0 as `appState` and comes back in the
 * browser, so it is treated as untrusted on the way back: only a same-origin path to a gated app route is
 * restored, and anything else lands on the garage.
 */
describe('safeReturnTo', () => {
  it.each([
    '/file?vehicle=BT53%20AKJ&type=PartsReceipt&title=Coolant%20%26%20thermostat&date=2026-11-14&expenseEntryId=412',
    '/file?vehicle=bt53akj&type=CarPhoto',
    '/file?vehicle=bt53akj&type=Receipt&title=1%2F2%20tank',
    '/bt53akj/photos',
    '/bt53akj/documents?file=1&type=MOT',
    '/bt53akj/assistant',
    '/account',
    '/admin',
    '/',
  ])('restores the app path %s', (path) => {
    expect(safeReturnTo(path)).toBe(path)
  })

  it.each([
    ['an absolute URL', 'https://evil.example/file'],
    ['a scheme-relative host', '//evil.example'],
    ['a scheme-relative host with a path', '//evil.example/file?vehicle=x'],
    ['a backslash host', '/\\evil.example'],
    ['two backslashes', '\\\\evil.example'],
    ['a backslash after the slash, encoded', '/%5Cevil.example'],
    ['an encoded second slash', '/%2Fevil.example'],
    ['an encoded double slash', '/%2F%2Fevil.example'],
    ['a javascript URL', 'javascript:alert(1)'],
    ['a javascript URL with a leading slash trick', '/javascript:alert(1)'],
    ['a data URL', 'data:text/html,<script>alert(1)</script>'],
    ['a tab inside the host', '/\t/evil.example'],
    ['a newline inside the host', '/\n/evil.example'],
    ['leading whitespace', ' //evil.example'],
    ['a path with no leading slash', 'evil.example/file'],
    ['a dot segment out of the app', '/file/../../evil'],
    ['a route the app does not have', '/wp-admin'],
    ['a public route', '/privacy'],
    ['an unknown screen under a vehicle', '/bt53akj/settings'],
    ['an empty string', ''],
  ])('sends %s to the garage', (_, value) => {
    expect(safeReturnTo(value)).toBe('/')
  })

  it.each([undefined, null, 42, { returnTo: '/file' }, ['/file']])('sends a non-string (%s) to the garage', (value) => {
    expect(safeReturnTo(value)).toBe('/')
  })

  it('drops a fragment', () => {
    expect(safeReturnTo('/account#danger')).toBe('/account')
  })
})

describe('returnToFrom', () => {
  it('keeps the path and the query of a gated route', () => {
    expect(returnToFrom({ pathname: '/file', search: '?vehicle=bt53akj&type=MOT' })).toBe('/file?vehicle=bt53akj&type=MOT')
  })

  it('has nothing to return to from the garage', () => {
    expect(returnToFrom({ pathname: '/', search: '' })).toBeUndefined()
  })

  it('has nothing to return to from a route that would not be restored', () => {
    expect(returnToFrom({ pathname: '/privacy', search: '' })).toBeUndefined()
  })
})

describe('restoreAfterSignIn', () => {
  afterEach(() => window.history.replaceState({}, '', '/'))

  it('navigates to the saved path, replacing the callback URL', () => {
    const navigate = vi.fn()

    restoreAfterSignIn({ returnTo: '/file?vehicle=bt53akj&type=MOT' }, navigate)

    expect(navigate).toHaveBeenCalledWith('/file?vehicle=bt53akj&type=MOT')
  })

  it.each(['//evil.example', '/\\evil.example', 'https://evil.example', 'javascript:alert(1)'])(
    'sends %s to the garage',
    (returnTo) => {
      const navigate = vi.fn()

      restoreAfterSignIn({ returnTo }, navigate)

      expect(navigate).toHaveBeenCalledWith('/')
    },
  )

  it('strips the code and state from the callback URL when nothing was saved', () => {
    window.history.replaceState({}, '', '/?code=abc&state=xyz')
    const navigate = vi.fn()

    restoreAfterSignIn(undefined, navigate)

    expect(navigate).toHaveBeenCalledWith('/')
  })
})
