import type { VehicleSummary } from '../../api/client'
import { Pill } from '../../components/Pill'
import { Panel, Section, SectionHead, Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'
import { countdownText, renewalPresentation, type RenewalUrgency } from '../../lib/renewal'
import type { ScreenId } from '../../shell/nav'

type Renewal = VehicleSummary['renewals']['mot']

const date = (iso: string | null) =>
  iso === null ? null : new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

interface RowProps {
  renewal: Renewal
  reg: string
  to: ScreenId
  note: string
  /** The right-hand sub-line under the date, where there is one. */
  sub?: string | null
}

function Row({ renewal, reg, to, note, sub }: RowProps) {
  const { label, tone } = renewalPresentation(renewal.urgency as RenewalUrgency | null, renewal.daysRemaining)

  return (
    <div className={`rrow tone-${tone}`}>
      <div className="stripe" />
      <div className="cell">
        <div className="rlabel">
          <AppLink to={to} reg={reg}>
            {renewal.name}
          </AppLink>
        </div>
        <div className="rnote">{renewal.source ?? note}</div>
      </div>
      <div className="cell">
        <div className="rdate num">{date(renewal.expiryDate) ?? 'not recorded'}</div>
        {sub !== null && sub !== undefined && <div className="rsub">{sub}</div>}
      </div>
      <div className="cell">
        <div className="rcount num">{countdownText(renewal.daysRemaining)}</div>
      </div>
      <div className="cell">
        <Pill tone={tone}>{label}</Pill>
      </div>
    </div>
  )
}

/**
 * Renewals and due dates.
 *
 * **The legend is rewritten, not ported.** The design's is `<i>red</i> under 30 days · <i>amber</i> under 60`
 * — the page's one colour-only status statement, and it does not even survive its own stylesheet: `.rule i`
 * paints those two words in `--accent`, so "red" renders orange. It names the paint instead of the rule, which
 * leaves a greyscale reader, a colourblind reader and a screen-reader user with a legend that explains nothing.
 * The thresholds are the information; the colours were only ever how they were shown.
 */
export function RenewalsPanel({ summary }: { summary: VehicleSummary }) {
  const { mot, insurance, roadTax, nextServiceDate, nextServiceMiles } = summary.renewals
  const reg = summary.registration

  return (
    <Section>
      <Wrap>
        <SectionHead
          title="Renewals & due dates"
          rule={<>Due under 30 days · due soon under 60</>}
          link={
            <AppLink className="sec-link" to="settings" reg={reg}>
              Statutory &amp; policies →
            </AppLink>
          }
        />
        <Panel className="renewals">
          <Row
            renewal={mot}
            reg={reg}
            to="service"
            note="no MOT record yet — add the pass and this derives itself"
          />
          <Row renewal={insurance} reg={reg} to="settings" note="no policy recorded" />
          <Row renewal={roadTax} reg={reg} to="settings" note="VED · verify at gov.uk/check-vehicle-tax" />
          <Row
            renewal={nextServiceDate}
            reg={reg}
            to="service"
            note="whichever comes first · from the latest service record"
            // The design shows "or 87,500 mi" and a miles countdown as separate hardcoded strings. Only the
            // miles figure exists in the domain, and only sometimes.
            sub={nextServiceMiles !== null ? `or in ${nextServiceMiles.toLocaleString('en-GB')} mi` : null}
          />
        </Panel>
      </Wrap>
    </Section>
  )
}
