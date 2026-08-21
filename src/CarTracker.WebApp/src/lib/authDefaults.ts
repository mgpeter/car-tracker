/**
 * The Auth0 application this app falls back to, as plain constants.
 *
 * **It is a separate file from `authConfig.ts` because two very different readers need it, and only one of
 * them is a browser.** `authConfig.ts` reads `import.meta.env` and can therefore only ever run inside the
 * bundle; the build-time CSP plugin (`plugins/theme-csp.ts`) runs in Node, during `vite build`, and must put
 * the same Auth0 origin into `connect-src`. Anything with `import.meta.env` in it is unimportable from there.
 *
 * **The two disagreeing is a silent production-only login failure**, which is why this file exists at all.
 * The CSP used to carry its own hardcoded copy of the tenant domain, decoupled from `VITE_AUTH0_DOMAIN`. Point
 * the SPA at a different tenant and the token XHR would go to the new one while `connect-src` still named the
 * old: the browser refuses the request with a console line, the app never signs in, and nothing else looks
 * wrong. The CSP is build-only, so dev and the whole test suite would show a working login.
 *
 * None of these are secrets. The domain is the tenant's public discovery origin, the client id is a public SPA
 * identifier, and the audience is a public API identifier.
 */
export const AUTH0_DEFAULTS = {
  domain: 'usualexpat.uk.auth0.com',
  clientId: 'AYVXSt9aa5rz4kHFYs3KZ5HqYfBNkPKp',

  /**
   * The API (resource server) tokens are minted for - and **a security boundary, not a label**.
   *
   * A bearer token is validated on signature, issuer, audience and expiry, and nothing checks which client
   * asked for it. So two deployments sharing an audience share their tokens: one minted against a box on a
   * home LAN is cryptographically valid on the public site. That is why cambelt.app has its own
   * `cambelt.api` rather than reusing this.
   */
  audience: 'cartracker.api',
} as const
