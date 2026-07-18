/**
 * Whether the owner has dismissed the dashboard's all-clear panel, remembered per vehicle.
 *
 * Guarded exactly like `lib/theme.ts`: localStorage throws in private-mode Safari and when storage is disabled,
 * and a piece of reassurance that will not survive a reload is not worth surfacing an error for. A missing key
 * means "not dismissed", which is the correct default.
 */
const key = (reg: string) => `ct-attn-dismissed:${reg.toUpperCase().replace(/\s+/g, '')}`

export function isAllClearDismissed(reg: string): boolean {
  try {
    return localStorage.getItem(key(reg)) === '1'
  } catch {
    return false
  }
}

export function setAllClearDismissed(reg: string, dismissed: boolean): void {
  try {
    if (dismissed) localStorage.setItem(key(reg), '1')
    else localStorage.removeItem(key(reg))
  } catch {
    // As in theme.ts: the choice holds for this session, it just will not persist.
  }
}
