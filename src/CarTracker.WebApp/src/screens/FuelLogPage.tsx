import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../api/client'
import { apiRequest } from '../api/client'
import { ApiFailure, useVehicleSummary } from '../api/queries'
import { Mark } from '../components/Btn'
import { Kv } from '../components/Kv'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { AddFillSheet } from './fuel/AddFillSheet'
import { FuelTable } from './fuel/FuelTable'

type Fuel = VehicleSummary['fuel']

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

/**
 * The fuel log.
 *
 * Reads `GET /api/vehicles/{reg}/fuel`, which returns the same `FuelEconomySummary` the dashboard's panel
 * renders — computed once, through `IDerivedMetricsService`. A raw `FuelEntries` query would hand back rows
 * with no MPG and invite this screen to work it out again, which is how two surfaces start disagreeing about
 * a number.
 */
type FuelEntry = Fuel['entries'][number]

export function FuelLogPage() {
  const reg = useVehicleReg()
  const [editing, setEditing] = useState<FuelEntry | 'new' | null>(null)

  const { data: summary } = useVehicleSummary(reg)
  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'fuel'] as const,
    queryFn: async () => {
      const result = await apiRequest<Fuel>(`/api/vehicles/${encodeURIComponent(reg)}/fuel`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const last = data?.entries.at(-1)
  const measured = data?.entries.filter((e) => e.mpg !== null && e.isPlausible) ?? []
  const best = measured.find((e) => e.mpg === data?.bestMpg)
  const worst = measured.find((e) => e.mpg === data?.worstMpg)
  const prices = data?.entries.map((e) => e.pricePerLitre) ?? []

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="fuel"
      center={{ kind: 'action', icon: 'plus', label: 'Add fill', onClick: () => setEditing('new') }}
      footer={
        <>
          MPG is computed per fill from litres and the distance since the previous fill — never stored, and
          never withheld on a partial tank. A figure outside 10–70 MPG is kept and marked, because the entry is
          real even when the figure is not; it is excluded from the averages above, not deleted.
        </>
      }
    >
      <PageHead
        eyebrow={`Fuel log · computed live${data !== undefined && data.lastFillDate !== null ? ` · last fill ${shortDate(data.lastFillDate)}` : ''}`}
        title="Fuel"
        plate={summary?.registration ?? reg}
        pmeta={
          <>
            {summary?.mileage.currentMileage !== null && summary?.mileage.currentMileage !== undefined && (
              <>
                Odometer <b>{summary.mileage.currentMileage.toLocaleString('en-GB')} mi</b>
                <br />
              </>
            )}
            {/* The design's line here is "MPG is computed per fill, full-to-full only — partial fills are
                logged but their MPG withheld". That rule was removed by the fuel-basis spec: litres is the
                sole basis, and a partial fill's litres are exactly as known as any other's. */}
            MPG is computed per fill from litres alone —<br />
            fill level is recorded, and changes nothing
          </>
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The fuel log could not be loaded</h2>
              <p className="panel-empty">{error instanceof Error ? error.message : 'The request failed.'}</p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <p className="panel-empty">Loading…</p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <>
          <Section>
            <Wrap>
              <SectionHead
                title="Fleet stats"
                rule={
                  data.fillCount === 0 ? (
                    <>no fills yet</>
                  ) : (
                    <>
                      {data.fillCount} fill{data.fillCount === 1 ? '' : 's'}
                      {/* The count that separates a fill from a measurable interval. On BT53's real history
                          these differ by one, and that one is DEC-012's phantom. */}
                      {data.measuredIntervalCount !== data.fillCount && (
                        <> · {data.measuredIntervalCount} measurable</>
                      )}
                      {/* Kept off the averages but on the log. The design has no concept of this at all: its
                          only non-figure is the "Estimate" pill it puts on the interval that never happened. */}
                      {data.implausibleCount > 0 && <> · {data.implausibleCount} implausible</>}
                    </>
                  )
                }
                link={
                  <AppLink className="sec-link" to="dashboard" reg={reg}>
                    Dashboard →
                  </AppLink>
                }
              />
              <Panel className="stats num">
                <Kv
                  label="Average"
                  value={data.averageMpg === null ? '—' : `${data.averageMpg.toFixed(1)}`}
                  note={
                    data.averageMpg === null
                      ? 'needs two fills'
                      : `MPG · ${data.measuredIntervalCount} interval${data.measuredIntervalCount === 1 ? '' : 's'}`
                  }
                />
                <Kv
                  label="Best"
                  value={data.bestMpg === null ? '—' : data.bestMpg.toFixed(1)}
                  note={best !== undefined ? `${dayMonth(best.entryDate)} · ${best.milesSinceLast?.toLocaleString('en-GB')} mi` : 'no interval'}
                />
                <Kv
                  label="Worst"
                  value={data.worstMpg === null ? '—' : data.worstMpg.toFixed(1)}
                  note={worst !== undefined ? `${dayMonth(worst.entryDate)} · ${worst.milesSinceLast?.toLocaleString('en-GB')} mi` : 'no interval'}
                />
                <Kv
                  label="Total litres"
                  value={data.totalLitres.toFixed(2)}
                  note={data.totalCost > 0 ? `${money(data.totalCost)} pumped` : 'nothing pumped'}
                />
                <Kv
                  label="Avg price/L"
                  value={data.averagePricePerLitre === null ? '—' : `£${data.averagePricePerLitre.toFixed(3)}`}
                  // Volume-weighted (DEC-011). The workbook's plain mean of the price column gives 1.5949
                  // against this 1.5973 — not a defect, a different question answered correctly, which is why
                  // it sits outside the count of five.
                  note={
                    prices.length > 0
                      ? `${Math.min(...prices).toFixed(3)} – ${Math.max(...prices).toFixed(3)} · volume-weighted`
                      : 'no fills'
                  }
                />
                <Kv
                  label="Last fill"
                  value={data.lastFillDate === null ? '—' : dayMonth(data.lastFillDate)}
                  note={last?.station ?? (data.lastFillDate === null ? 'no fills' : 'station not recorded')}
                />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Fills"
                rule={<>each fill mirrors into expenses automatically</>}
                link={<Mark onClick={() => setEditing('new')}>Add fill</Mark>}
              />
              {data.entries.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No fills logged yet. The first one records an odometer reading and mirrors into expenses;
                    MPG starts at the second, because the first has nothing to measure from.
                  </p>
                </Panel>
              ) : (
                <FuelTable
                  entries={data.entries}
                  bestMpg={data.bestMpg}
                  worstMpg={data.worstMpg}
                  onEdit={setEditing}
                />
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddFillSheet
        editing={editing}
        onClose={() => setEditing(null)}
        reg={reg}
        // The previous FILL's mileage, and nothing else. Falling back to the current odometer reading looks
        // helpful and is wrong: MPG measures fuel burned between two fills, so a reading that is not a fill
        // has no litres attached and cannot bound an interval. With that fallback the first fill previewed
        // 66.4 MPG — measured from the purchase reading — while the server correctly returned no MPG at all,
        // because there was no previous fill. A preview that contradicts the server is worse than no preview.
        lastMileage={last?.mileage ?? null}
        averageMpg={data?.averageMpg ?? null}
        today={summary?.asOfDate ?? new Date().toISOString().slice(0, 10)}
      />
    </AppShell>
  )
}
