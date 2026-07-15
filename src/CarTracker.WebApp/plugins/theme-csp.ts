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
 * Injects the pre-paint script and a CSP that allows exactly it.
 *
 * The CSP is applied on build only. Vite's dev server injects its own inline scripts for HMR, so a strict
 * policy in dev would either break the dev loop or force 'unsafe-inline' — and a policy with 'unsafe-inline'
 * is not a policy, it just looks like one in a screenshot. The script itself is injected in both, so dev and
 * prod share the no-flash behaviour.
 *
 * `font-src 'self'` is the DEC-010 guarantee made enforceable: with it, a CDN-loaded face does not silently
 * degrade to a system fallback, it fails loudly in the console.
 */
export function themeCsp(): Plugin {
  let isBuild = false

  return {
    name: 'cartracker:theme-csp',

    configResolved(config) {
      isBuild = config.command === 'build'
    },

    transformIndexHtml(html) {
      const tags: Array<{ tag: string; attrs?: Record<string, string>; children?: string; injectTo: 'head-prepend' }> = []

      // Order is load-bearing, and both tags are head-prepended in array order. A <meta> CSP governs only
      // what is parsed AFTER it, so the policy must precede the script it hashes. With the script first the
      // hash is decorative: the script runs because nothing is policing it yet, and a wrong hash would fail
      // silently in the direction that looks like success.
      if (isBuild) {
        const policy = [
          "default-src 'self'",
          // The hash covers the pre-paint script above. Vite's own bundle is an external 'self' module.
          `script-src 'self' '${themeScriptHash()}'`,
          // Tailwind emits a stylesheet; the components carry no inline style attributes that need hashing.
          // 'unsafe-inline' for styles is a real weakening, so it stays out until something demands it.
          "style-src 'self'",
          // DEC-010.
          "font-src 'self'",
          "img-src 'self' data:",
          // The gateway is the single origin (DEC-009), so the API is same-origin. If this ever needs
          // widening, something has bypassed the gateway and that is the bug — see CLAUDE.md on CORS.
          "connect-src 'self'",
          "object-src 'none'",
          "base-uri 'none'",
          "frame-ancestors 'none'",
          "form-action 'self'",
        ].join('; ')

        tags.push({
          tag: 'meta',
          attrs: { 'http-equiv': 'Content-Security-Policy', content: policy },
          injectTo: 'head-prepend',
        })
      }

      tags.push({
        tag: 'script',
        attrs: { 'data-theme-preload': '' },
        children: THEME_SCRIPT,
        injectTo: 'head-prepend',
      })

      return { html, tags }
    },
  }
}
