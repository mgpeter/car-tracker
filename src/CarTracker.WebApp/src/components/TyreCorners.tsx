import { Pill } from './Pill'

export interface CornerReading {
  psiFrontLeft: number | null
  psiFrontRight: number | null
  psiRearLeft: number | null
  psiRearRight: number | null
  psiSpare: number | null
  treadFrontLeft: number | null
  treadFrontRight: number | null
  treadRearLeft: number | null
  treadRearRight: number | null
}

/** The MOT limit — below this a tyre is illegal, not merely worn. */
export const LEGAL_TREAD = 1.6
/** Approaching the limit: worth flagging before the test finds it. */
export const WARN_TREAD = 3.0

const CORNERS = [
  { psi: 'psiFrontLeft', tread: 'treadFrontLeft', label: 'Front left', pos: 'fl' },
  { psi: 'psiFrontRight', tread: 'treadFrontRight', label: 'Front right', pos: 'fr' },
  { psi: 'psiRearLeft', tread: 'treadRearLeft', label: 'Rear left', pos: 'rl' },
  { psi: 'psiRearRight', tread: 'treadRearRight', label: 'Rear right', pos: 'rr' },
] as const

const treadStatus = (tread: number | null) =>
  tread === null ? null : tread <= LEGAL_TREAD ? 'overdue' : tread <= WARN_TREAD ? 'soon' : 'ok'

/**
 * The four corners, laid out as four corners.
 *
 * "Which corner" is spatial, so pressure at the front-left is easier to place on a car outline than in the
 * second column of a wide row. Four `border-radius` divs around a body silhouette, not an SVG — the shape is
 * boxes, which CSS expresses natively. Rendered *alongside* the readings table, not replacing it: four numbers
 * are still honestly a table too.
 *
 * The model is asymmetric on purpose — five pressures, four treads — so the spare takes a pressure but has no
 * tread target, and its card says so ("no tread target") rather than leaving a blank to interpret. A corner
 * whose tread nears the MOT limit warns on the due axis, so a legal-borderline tyre is visible before the test.
 */
export function TyreCorners({ reading }: { reading: CornerReading }) {
  return (
    <div className="tyre-diagram" role="group" aria-label="Latest tyre reading by corner">
      <div className="tyre-body" aria-hidden="true" />
      {CORNERS.map((c) => {
        const psi = reading[c.psi]
        const tread = reading[c.tread]
        const status = treadStatus(tread)
        return (
          <div className={`tyre-corner tyre-${c.pos}${status && status !== 'ok' ? ` warn-${status}` : ''}`} key={c.pos}>
            <span className="tc-label">{c.label}</span>
            <span className="tc-psi num">{psi === null ? '—' : `${psi % 1 === 0 ? psi : psi.toFixed(1)} psi`}</span>
            <span className="tc-tread num">{tread === null ? 'no tread' : `${tread.toFixed(1)} mm`}</span>
            {status === 'overdue' && <Pill tone="due">Below MOT limit</Pill>}
            {status === 'soon' && <Pill tone="soon">Approaching {LEGAL_TREAD} mm</Pill>}
          </div>
        )
      })}

      {/* The fifth pressure, no tread target — a full-width card that says so rather than leaving a gap. */}
      <div className="tyre-spare">
        <span className="tc-label">Spare</span>
        <span className="tc-psi num">{reading.psiSpare === null ? 'never logged' : `${reading.psiSpare} psi`}</span>
        <span className="tc-tread num">no tread target</span>
      </div>
    </div>
  )
}
