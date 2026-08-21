import { AUTH0_DEFAULTS } from './authDefaults'

/**
 * Which Auth0 application this deployment signs people in with.
 *
 * None of it is secret: the domain is the tenant's public discovery origin, the client id is a public SPA
 * identifier, and the audience is a public API identifier. The `audience` is the linchpin - requesting a token
 * *for it* is what makes Auth0 issue a verifiable JWT access token rather than an opaque one, and it must
 * match the API's `Auth0:Audience`.
 *
 * **Three sources, in falling order of specificity, and the first one is why this app can be self-hosted.**
 *
 * 1. `window.__CAMBELT_CONFIG__`, set by `/config.js`, which the gateway serves from its own environment. This
 *    is a *runtime* value, so one published image can serve any deployment against any Auth0 tenant.
 * 2. `import.meta.env`, substituted by Vite at build time. Useful for `npm run dev` with a local `.env`, and
 *    for anyone who would rather bake an image than configure one.
 * 3. The compiled-in defaults, which are this project's own application - so an unset deployment still works
 *    rather than half-working.
 *
 * It used to be (2) and (3) only, and that made the app un-self-hostable in a way that was easy to miss: Vite
 * turns `import.meta.env` into literals during `vite build`, so anyone with a different Auth0 tenant had to
 * fork the repo and build their own gateway image. The API half never had this problem - it reads
 * `Auth0:Authority` and `Auth0:Audience` from configuration like any other server setting.
 *
 * Read synchronously, at module scope, exactly as before. `/config.js` is a render-blocking script in the
 * document head, so the global is already there when the app mounts - which is the whole reason it is a script
 * rather than a JSON document someone has to await.
 */
const env = import.meta.env as Record<string, string | undefined>

/** What `/config.js` sets. Optional at every level: dev has no gateway, and tests have no document. */
interface RuntimeConfig {
  domain?: string
  clientId?: string
  audience?: string
}

const runtime: RuntimeConfig =
  (typeof window === 'undefined'
    ? undefined
    : (window as unknown as { __CAMBELT_CONFIG__?: RuntimeConfig }).__CAMBELT_CONFIG__) ?? {}

/** Empty strings are treated as absent, so an unset container variable falls through rather than blanking. */
const pick = (...candidates: (string | undefined)[]): string =>
  candidates.find((value) => value !== undefined && value !== '') ?? ''

export const auth0Config = {
  domain: pick(runtime.domain, env.VITE_AUTH0_DOMAIN, AUTH0_DEFAULTS.domain),
  clientId: pick(runtime.clientId, env.VITE_AUTH0_CLIENT_ID, AUTH0_DEFAULTS.clientId),
  audience: pick(runtime.audience, env.VITE_AUTH0_AUDIENCE, AUTH0_DEFAULTS.audience),
} as const
