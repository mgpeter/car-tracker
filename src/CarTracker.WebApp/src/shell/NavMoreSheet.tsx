import { useMeta } from '../api/queries'
import { Sheet } from '../components/Sheet'
import { AppLink, useLinkRenderer } from '../lib/link'
import { GROUP_LABELS, groupedScreens, SCREENS, type CurrentScreen } from './nav'
import type { ShellScope } from './scope'

interface NavMoreSheetProps {
  open: boolean
  onClose: () => void
  scope: ShellScope
  current: CurrentScreen
}

/**
 * The mobile "All screens" sheet.
 *
 * One component, replacing **three** stylings of the same list: `.ms-group`/`.ms-list` (dashboard only, and
 * grouped), `<a class="mark">` with a repeated inline style (14 screens, flat), and `<a class="btn">` with a
 * ~110-character inline style per link (fuel-log, flat). The flat variants are the grouped one with its
 * headings deleted, so this takes the grouped version — the owner's call, and the evidence agrees.
 *
 * `excludeTopLevel: false`, unlike the desktop More panel: on this viewport the sheet is the ONLY menu, so it
 * has to list everything the bottom bar's three slots do not.
 *
 * `.ms-group`/`.ms-list` are lifted out of `dashboard.dc.html` — they were shell all along, defined in one
 * screen's local `<style>` because that screen happened to be the one that used them.
 */
export function NavMoreSheet({ open, onClose, scope, current }: NavMoreSheetProps) {
  const groups = groupedScreens({ excludeTopLevel: false })
  const render = useLinkRenderer()

  // The assistant's entry point on a phone, where the top bar — and with it the dock button — is not rendered.
  // Strictly `=== true`, so an in-flight meta offers nothing rather than a link that 503s.
  const chat = useMeta().data?.chatConfigured === true && scope.kind === 'vehicle'

  return (
    <Sheet open={open} onClose={onClose} title="All screens">
      <div className="ms-pad">
        {chat && (
          <div>
            <div className="ms-group">Assistant</div>
            <div className="ms-list">
              {render({
                href: `/${scope.reg.toLowerCase().replace(/\s+/g, '')}/assistant`,
                ...(current === 'assistant' && { 'aria-current': 'page' as const }),
                children: 'Ask about this car',
              })}
            </div>
          </div>
        )}

        {groups.map(({ group, ids }) => (
          <div key={group}>
            <div className="ms-group">{GROUP_LABELS[group]}</div>
            <div className="ms-list">
              {ids.map((id) => {
                const scoped = SCREENS[id].scoped
                // A vehicle-scoped link is unreachable from the garage — there is no vehicle to scope it to.
                if (scoped && scope.kind === 'garage') return null
                return (
                  <AppLink
                    key={id}
                    to={id}
                    {...(scoped && scope.kind === 'vehicle' && { reg: scope.reg })}
                    current={current === id}
                  >
                    {SCREENS[id].label}
                  </AppLink>
                )
              })}
            </div>
          </div>
        ))}
      </div>
    </Sheet>
  )
}
