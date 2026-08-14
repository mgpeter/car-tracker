import { useVehicleSummary } from '../api/queries'
import { ChatPanel } from './ChatPanel'

/**
 * The docked panel, with the car named the way it is written on the car.
 *
 * The shell knows the vehicle as the URL slug — `bt53akj` — and that is the right thing for routing and the
 * wrong thing to print: no British plate reads BT53AKJ. Screens reach the real registration through
 * `usePlate()`, which needs the route context; the shell renders above some routes that have none, so this
 * resolves it from the summary by slug instead and keeps `AppShell` free of the question.
 *
 * The slug is the fallback for the moment before the summary lands, not an answer — the same rule `usePlate`
 * follows.
 */
export function ChatDock({ reg, onClose }: { reg: string | null; onClose: () => void }) {
  const { data } = useVehicleSummary(reg ?? '')

  return <ChatPanel vehicle={reg === null ? null : (data?.registration ?? reg)} variant="dock" onClose={onClose} />
}
