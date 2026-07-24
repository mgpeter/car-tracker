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

afterEach(() => {
  cleanup()
})
