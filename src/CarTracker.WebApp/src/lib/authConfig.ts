/**
 * Auth0 configuration for the interactive login (README §6).
 *
 * None of these are secrets — the domain is the tenant's public discovery origin, the client id is a public SPA
 * identifier, and the audience is a public API identifier. They default to this project's tenant so the app
 * runs with no `.env`; a deployment against a different tenant overrides them with `VITE_AUTH0_*` (see
 * `.env.example`).
 *
 * The `audience` is the linchpin: requesting a token *for it* is what makes Auth0 issue a verifiable JWT access
 * token (not an opaque one), which the API validates. It must match the API's `Auth0:Audience`.
 */
const env = import.meta.env as Record<string, string | undefined>

export const auth0Config = {
  domain: env.VITE_AUTH0_DOMAIN ?? 'usualexpat.uk.auth0.com',
  clientId: env.VITE_AUTH0_CLIENT_ID ?? 'AYVXSt9aa5rz4kHFYs3KZ5HqYfBNkPKp',
  audience: env.VITE_AUTH0_AUDIENCE ?? 'cartracker.api',
} as const
