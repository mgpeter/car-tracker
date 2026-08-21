import type { Plugin } from 'vite'
import { AUTH0_DEFAULTS } from '../src/lib/authDefaults.js'

/**
 * Serves `/config.js` in development, standing in for the gateway.
 *
 * In production the gateway serves that path from its own environment, which is how one published image can
 * be pointed at any Auth0 tenant. There is no gateway in `npm run dev`, so without this the document's
 * `<script src="/config.js">` would 404 on every page load - harmless, because `authConfig.ts` falls through
 * to the build-time env and then to the defaults, but a console error that is there every single time is one
 * people learn to scroll past, and the next real one goes with it.
 *
 * It serves the same values `authConfig.ts` would have fallen back to, which keeps dev honest about the
 * mechanism: the config genuinely arrives over HTTP here too, rather than dev quietly taking a different path
 * through the code than production does.
 *
 * `VITE_AUTH0_*` from a local `.env` still wins for anyone pointing a dev session at their own tenant, because
 * this only fills in what that would have filled in anyway.
 */
export function devConfig(): Plugin {
  return {
    name: 'cartracker:dev-config',
    apply: 'serve',

    configureServer(server) {
      server.middlewares.use('/config.js', (_request, response) => {
        const env = server.config.env as Record<string, string | undefined>

        const config = {
          domain: env['VITE_AUTH0_DOMAIN'] ?? AUTH0_DEFAULTS.domain,
          clientId: env['VITE_AUTH0_CLIENT_ID'] ?? AUTH0_DEFAULTS.clientId,
          audience: env['VITE_AUTH0_AUDIENCE'] ?? AUTH0_DEFAULTS.audience,
        }

        response.setHeader('Content-Type', 'application/javascript; charset=utf-8')
        response.setHeader('Cache-Control', 'no-store')
        response.end(`window.__CAMBELT_CONFIG__=${JSON.stringify(config)};`)
      })
    },
  }
}
