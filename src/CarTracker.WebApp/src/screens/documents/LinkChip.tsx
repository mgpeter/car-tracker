import { LINK_LABEL, type DocumentItem } from '../../api/documents'
import { Absent, Sub } from '../../components/DataTable'
import { AppLink } from '../../lib/link'
import type { ScreenId } from '../../shell/nav'

/** The screen each kind of record lives on. */
export const LINK_SCREEN = {
  ServiceRecord: 'service',
  Expense: 'expenses',
  Issue: 'issues',
} as const satisfies Record<keyof typeof LINK_LABEL, ScreenId>

/**
 * The `→ service record` chip, from whichever link is set, pointing at the screen that record lives on.
 * Absent is a real state and says so.
 */
export function LinkChip({ document, reg }: { document: Pick<DocumentItem, 'linkedTo'>; reg: string }) {
  if (document.linkedTo === null) return <Absent>not attached</Absent>

  const { kind, label } = document.linkedTo
  return (
    <AppLink to={LINK_SCREEN[kind]} reg={reg} className="dlink">
      → {LINK_LABEL[kind]}
      <Sub>{label}</Sub>
    </AppLink>
  )
}
