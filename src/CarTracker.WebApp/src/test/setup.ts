import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, expect, vi } from 'vitest'
import { toHaveNoViolations } from './axe'

expect.extend({ toHaveNoViolations })

// A signed-in Auth0 by default, for every test. Components that read the session (the shell's user menu) render
// as authenticated; nothing here wires the token into `client.ts`, so API mocks are untouched. A test that
// needs the signed-out or loading state overrides this per-file with its own vi.mock.
vi.mock('@auth0/auth0-react', () => ({
  Auth0Provider: ({ children }: { children: unknown }) => children,
  useAuth0: () => ({
    isAuthenticated: true,
    isLoading: false,
    error: undefined,
    user: { email: 'you@example.test', name: 'Test Owner' },
    loginWithRedirect: vi.fn(),
    logout: vi.fn(),
    getAccessTokenSilently: vi.fn(async () => 'test-access-token'),
  }),
}))

// jsdom implements no layout, so it defines no `scrollIntoView` at all — calling one is a TypeError rather
// than a no-op. The integrity queue's "Fix this" brings the flagged row into view on arrival, and that is a
// claim about pixels no unit test can check; stubbing it here keeps the behaviour testable without the
// component apologising for the environment with an optional call.
Element.prototype.scrollIntoView ??= () => {}

afterEach(() => {
  cleanup()
})
