import type { VehicleSummary } from '../../api/client'
import { IntegrityPill } from '../../components/Pill'
import { Panel, Section, SectionHead, Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'

/**
 * The dashboard's data-integrity panel.
 *
 * **It renders nothing at all when there are no flags** — and returning `null` is the whole design, not a
 * shortcut. A panel headed "Data integrity" sitting empty implies a question was asked and answered clean,
 * which is a different and stronger claim than "there is nothing to say". The clean state is the norm; a
 * section that appears only when it has content keeps the dashboard about what needs attention.
 *
 * M1d left this out for having no data source: `VehicleSummary` carried no integrity figure. It now carries
 * `Integrity { openCount, highestSeverity }` — a headline, computed from the open flags — so the panel can
 * lead with the worst severity and link to the queue without loading every flag's detail here.
 */
export function IntegrityPanel({ summary }: { summary: VehicleSummary }) {
  const { integrity } = summary
  const reg = summary.registration

  if (integrity.openCount === 0) return null

  const n = integrity.openCount
  const severity = integrity.highestSeverity ?? 'Warning'

  return (
    <Section>
      <Wrap>
        <SectionHead
          title="Data integrity"
          rule={
            <>
              {n} open flag{n === 1 ? '' : 's'} · worst is {severity.toLowerCase()}
            </>
          }
          link={
            <AppLink className="sec-link" to="data-integrity" reg={reg}>
              Open the queue →
            </AppLink>
          }
        />
        <Panel className="attn attn-info">
          <div>
            <div className="attn-k">
              <IntegrityPill>{severity}</IntegrityPill>
            </div>
            <h3>
              {n === 1 ? 'A figure on this car is in question' : `${n} figures on this car are in question`}
            </h3>
            <p>
              A detector flagged {n === 1 ? 'a value' : 'some values'} it does not believe — a reading that
              goes backwards, an MPG the car cannot do, or a fill whose cost does not match its litres. Nothing
              was changed and nothing was rejected: every figure here is computed as though the flags were not
              raised, and the queue is where you decide what each one means.
            </p>
          </div>
          <div className="attn-act">
            <AppLink className="btn" to="data-integrity" reg={reg}>
              Review {n === 1 ? 'the flag' : `${n} flags`}
            </AppLink>
          </div>
        </Panel>
      </Wrap>
    </Section>
  )
}
