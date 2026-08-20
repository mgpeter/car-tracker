import { Btn } from '../../components/Btn'
import { Sheet } from '../../components/Sheet'
import { AppLink } from '../../lib/link'
import { ADD_QUERY, QUICK_ADD_ACTIONS } from './QuickAdd'

/**
 * Quick add, for the phone.
 *
 * **This closes a gap rather than adding a convenience.** The desktop quick-add band hides itself below 900px,
 * and its stylesheet says why: "The bottom bar's + is the mobile quick-add, so the band is duplication." That
 * was true of every screen except the one you land on - the dashboard's centre slot held an inert warning
 * tell-tale, so on a phone the dashboard offered no way to add anything at all.
 *
 * It renders `QUICK_ADD_ACTIONS`, the same list the band renders, for the reason two hand-written copies of
 * "the things you can log" always end the same way: one of them quietly loses wash for six months.
 *
 * The links carry `?add=1` and the target screen opens its own sheet on arrival, exactly as the band's do.
 * Navigating out of a sheet is fine - the route change unmounts it, so there is nothing to close.
 */
export function QuickAddSheet({
  open,
  onClose,
  reg,
  onAddFuel,
}: {
  open: boolean
  onClose: () => void
  reg: string
  onAddFuel: () => void
}) {
  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Quick add"
      subtitle="Everything you can log for this car."
    >
      <div style={{ gridColumn: '1 / -1', display: 'grid', gap: 10, gridTemplateColumns: 'minmax(0, 1fr)' }}>
        {/* Fuel first and solid, the same weight it has on the desktop band, because it is the weekly one.
            It is a button rather than a link: the dashboard mounts this sheet itself. */}
        <Btn
          onClick={() => {
            onClose()
            onAddFuel()
          }}
        >
          + Fuel
        </Btn>

        {QUICK_ADD_ACTIONS.map((action) => (
          <AppLink
            key={action.screen}
            className="btn ghost"
            to={action.screen}
            reg={reg}
            query={ADD_QUERY}
          >
            {action.label}
          </AppLink>
        ))}
      </div>
    </Sheet>
  )
}
