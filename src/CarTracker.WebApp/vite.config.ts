/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
// Explicit extension: this file is compiled under tsconfig.node.json, which is module: nodenext.
import { themeCsp } from './plugins/theme-csp.ts'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), themeCsp()],
  server: {
    // Aspire's AddViteApp injects PORT; fall back to Vite's default for standalone runs.
    port: process.env['PORT'] ? Number(process.env['PORT']) : 5173,
    host: true,

    // Required when reached through the gateway: YARP forwards with the destination's authority, and Vite
    // rejects hosts it does not recognise.
    allowedHosts: true,

    // Deliberately no `server.ws` config: leaving it undefined makes the HMR client connect back to the
    // origin it was served from — the gateway. Setting clientPort would point it at Vite's own port and
    // break HMR behind the proxy.
    //
    // Deliberately no `proxy`: unlike BookmarkFeeder, this app is only ever reached through the gateway,
    // which owns /api, /scalar and /openapi. A Vite proxy would be a second routing authority that exists
    // only in development.
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
  },
})
