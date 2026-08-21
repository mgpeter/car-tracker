import { AUTH0_DEFAULTS } from './authDefaults'

/**
 * Auth0 configuration for the interactive login (README §6).
 *
 * None of these are secrets — the domain is the tenant's public discovery origin, the client id is a public SPA
 * identifier, and the audience is a public API identifier. They default to this project's tenant so the app
 * runs with no `.env`; a deployment against a different tenant overrides them with `VITE_AUTH0_*` (see
 * `.env.example`), which `deploy/Dockerfile.gateway` passes as build arguments.
 *
 * The `audience` is the linchpin: requesting a token *for it* is what makes Auth0 issue a verifiable JWT access
 * token (not an opaque one), which the API validates. It must match the API's `Auth0:Audience`.
 */
const env = import.meta.env as Record<string, string | undefined>

export const auth0Config = {
  // The fallbacks live in `authDefaults.ts` rather than here, because the build-time CSP plugin needs the
  // same domain and cannot import this file - it reads `import.meta.env`, which exists only in the bundle.
  // A second copy of the domain over there is what used to make a tenant change a silent login failure.
  domain: env.VITE_AUTH0_DOMAIN ?? AUTH0_DEFAULTS.domain,
  clientId: env.VITE_AUTH0_CLIENT_ID ?? AUTH0_DEFAULTS.clientId,
  audience: env.VITE_AUTH0_AUDIENCE ?? AUTH0_DEFAULTS.audience,
} as const
