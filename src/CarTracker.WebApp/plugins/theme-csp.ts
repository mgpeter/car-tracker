import { createHash } from 'node:crypto'
import type { Plugin } from 'vite'

/**
 * The pre-paint theme script.
 *
 * It runs before first paint, in <head>, synchronously — that is the whole point. The design applies the
 * theme in componentDidMount, which is after first paint: hard-reload a dark-mode machine and you see a flash
 * of sand before it corrects. React cannot fix this from inside; only markup ahead of the body can.
 *
 * It only handles an EXPLICIT choice. `system` is the absence of the attribute, resolved by CSS
 * (`@media (prefers-color-scheme: dark) :root:not([data-theme='light'])`), so this script never asks the OS
 * anything and stays this small.
 *
 * Exported as a string because it is the single source of truth for both the injected markup and the CSP
 * hash. If the two ever disagree by one byte, the browser silently refuses to run it and the flash returns.
 */
export const THEME_SCRIPT = `try{var t=localStorage.getItem('ct-theme');if(t==='dark'||t==='light')document.documentElement.setAttribute('data-theme',t)}catch(e){}`

export function themeScriptHash(script: string = THEME_SCRIPT): string {
  return `sha256-${createHash('sha256').update(script, 'utf8').digest('base64')}`
}

/**
 * Injects the pre-paint theme script.
 *
 * **It used to inject a Content-Security-Policy meta tag too, and that moved to the gateway.** A meta tag is
 * fixed the moment the bundle is built, so it could only ever name the Auth0 tenant the *build* knew about -
 * which meant a deployment against any other tenant had its token request refused by its own policy, silently.
 * The policy is now a response header from `CarTracker.Gateway/SpaHosting.cs`, read from the same
 * configuration that produces `/config.js`, so the origin it permits and the origin the SPA calls come from
 * one place at serve time.
 *
 * The gateway hashes this script out of the `index.html` it is about to serve, which is why the marker
 * attribute below is load-bearing: it is how the hash finds its subject. `THEME_SCRIPT` and
 * `themeScriptHash()` stay exported because the tests still pin the script's shape, and because the hash
 * function is the definition the C# side is checked against.
 *
 * Injected in dev as well as in build - the no-flash behaviour is not a production nicety.
 */
export function themeCsp(): Plugin {
  return {
    name: 'cartracker:theme-csp',

    // Head-prepended so the theme is settled before the stylesheet lands. Returning the tag array alone
    // leaves the HTML itself untouched, which is all this plugin needs now.
    transformIndexHtml: () => [
      {
        tag: 'script',
        attrs: { 'data-theme-preload': '' },
        children: THEME_SCRIPT,
        injectTo: 'head-prepend' as const,
      },
    ],
  }
}
