/**
 * Client-side settings, persisted to localStorage.
 *
 * The API key lives here (DEC-009). localStorage is readable by any script running on this origin, so an XSS
 * would leak the key — that is a real property of this choice, not an oversight. It is acceptable because the
 * app is single-user and self-hosted, the key guards one person's car data, and the alternative (an
 * HttpOnly cookie) needs a login flow that README §6 explicitly does not want yet. Revisit if the app ever
 * grows a second user or leaves the LAN.
 */

const STORAGE_KEY = 'cartracker.settings'

export interface Settings {
  /** The X-Api-Key value. Empty until the owner pastes it in. */
  apiKey: string
}

const DEFAULTS: Settings = {
  apiKey: '',
}

type Listener = (settings: Settings) => void

const listeners = new Set<Listener>()

function read(): Settings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw === null) {
      return DEFAULTS
    }

    // Anything could be in localStorage — a hand-edit, an older shape, another app's key collision.
    // Merging over defaults means a malformed field degrades to its default rather than crashing the app.
    return { ...DEFAULTS, ...(JSON.parse(raw) as Partial<Settings>) }
  } catch {
    return DEFAULTS
  }
}

let current: Settings = read()

export function getSettings(): Settings {
  return current
}

export function updateSettings(patch: Partial<Settings>): Settings {
  current = { ...current, ...patch }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(current))
  listeners.forEach((listener) => listener(current))
  return current
}

export function subscribeToSettings(listener: Listener): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}
