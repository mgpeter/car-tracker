import { useReminders } from '../api/queries'
import { AppLink } from '../lib/link'

/**
 * The shell's reminder count.
 *
 * A read, derived from the same summary the dashboard uses (via `/reminders`), so this number and the
 * dashboard's "needs attention" panel are the one figure computed one way — they cannot drift (DEC-002, §4).
 * It rides in the shell so the count is glanceable from any screen, where the dashboard's own panel is not.
 *
 * On the **due axis** (rust), never the blue integrity axis and never the orange structural accent — a reminder
 * is a due-status thing. It disappears at zero rather than assert an all-clear the dashboard already makes; a
 * badge is for what needs doing.
 */
export function ReminderBadge({ reg }: { reg: string }) {
  const { data } = useReminders(reg)
  const count = data?.firingCount ?? 0

  if (count === 0) return null

  return (
    <AppLink
      to="dashboard"
      reg={reg}
      className="rem-badge"
      aria-label={`${count} reminder${count === 1 ? ' needs' : 's need'} attention — open the dashboard`}
    >
      <span className="pill due">
        {count} due
      </span>
    </AppLink>
  )
}
