import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure } from '../api/queries'
import { IntegrityPill } from '../components/Pill'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'

interface VehicleDetail {
  registration: string
  name: string
  variant: string | null
  year: number
  colour: string | null
  bodyStyle: string | null
  vin: string | null
  engineCode: string | null
  engineSizeCc: number | null
  fuelType: string
  transmission: string | null
  drivetrain: string | null
  purchaseDate: string
  purchasePrice: number | null
  purchaseMileage: number
  seller: string | null
  defaultGarage: string | null
  ulezCompliant: boolean | null
  vedAnnualCost: number | null
  fluids: Record<string, string | number | null>
  tyres: Record<string, string | number | null>
  insurance: Record<string, string | number | null>
  breakdown: Record<string, string | number | null>
  notes: string | null
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** A row, or nothing. An empty spec row is worse than an absent one — it implies the manual said nothing. */
function Row({ label, value, note }: { label: string; value: ReactNode; note?: ReactNode }) {
  if (value === null || value === undefined || value === '') return null
  return (
    <div className="setrow num">
      <span className="sk">{label}</span>
      <span className="sv">
        {value}
        {note !== undefined && <i>{note}</i>}
      </span>
    </div>
  )
}

/**
 * Vehicle info — the reference card.
 *
 * **The one screen that is honestly stored, and that is not a compromise.** An oil spec is not a measurement;
 * it is what the manual says goes in. Nothing here derives from a log because no log produces it, so nothing
 * here can drift out of step with one — which is the exact property the rest of the app has to work for.
 *
 * The policy dates appear here as the inputs they are. Their countdowns live on the dashboard, derived, and are
 * deliberately not repeated: two places showing "243 days" is two places to disagree.
 */
export function VehicleInfoPage() {
  const reg = useVehicleReg()

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'detail'] as const,
    queryFn: async () => {
      const result = await apiRequest<VehicleDetail>(`/api/vehicles/${encodeURIComponent(reg)}`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const f = data?.fluids ?? {}
  const t = data?.tyres ?? {}
  const ins = data?.insurance ?? {}
  const bd = data?.breakdown ?? {}

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="vehicle-info"
      center={null}
      footer={
        <>
          Everything on this screen is <b>stored</b>, and that is correct: a torque figure or an oil grade is
          what the manual says, not something measured. The countdowns these policies drive are computed on the{' '}
          <b>dashboard</b> and are not repeated here — two places showing the same number is two places to
          disagree.
        </>
      }
    >
      <PageHead
        eyebrow="Vehicle info · the reference card"
        title="Vehicle"
        plate={data?.registration ?? reg}
        pmeta={
          data === undefined ? undefined : (
            <>
              <b>{data.name}</b>
              {data.variant !== null && (
                <>
                  <br />
                  {data.variant}
                </>
              )}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The vehicle could not be loaded</h2>
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
                title="Identity"
                rule={<>stored facts</>}
                link={
                  <AppLink className="sec-link" to="settings" reg={reg}>
                    Edit in settings →
                  </AppLink>
                }
              />
              <Panel>
                <Row label="Registration" value={data.registration} />
                <Row label="Make & model" value={data.name} note={data.variant} />
                <Row label="Year" value={data.year > 0 ? data.year : null} />
                <Row label="Colour" value={data.colour} />
                <Row label="Body style" value={data.bodyStyle} />
                <Row label="VIN" value={data.vin} />
                <Row
                  label="Engine"
                  value={data.engineCode}
                  note={data.engineSizeCc !== null ? `${data.engineSizeCc} cc · ${data.fuelType}` : data.fuelType}
                />
                <Row label="Transmission" value={data.transmission} />
                <Row label="Drivetrain" value={data.drivetrain} />
                <Row label="ULEZ" value={data.ulezCompliant === null ? null : data.ulezCompliant ? 'Compliant' : 'Not compliant'} />
              </Panel>
            </Wrap>
          </Section>

          <Section>
            <Wrap>
              <SectionHead title="Purchase" rule={<>where the odometer started</>} />
              <Panel>
                <Row label="Bought" value={shortDate(data.purchaseDate)} note={data.seller} />
                <Row label="Price" value={data.purchasePrice !== null ? money(data.purchasePrice) : null} />
                <Row
                  label="Odometer at purchase"
                  value={`${data.purchaseMileage.toLocaleString('en-GB')} mi`}
                  // Not trivia: MilesSincePurchase and cost-per-mile both rest on it, and it is the founding
                  // MileageReading rather than a number typed twice.
                  note="miles-since-purchase and cost-per-mile derive from this"
                />
                <Row label="Default garage" value={data.defaultGarage} />
              </Panel>
            </Wrap>
          </Section>

          <Section>
            <Wrap>
              <SectionHead title="Fluids & parts" rule={<>what the manual says goes in</>} />
              <Panel>
                <Row
                  label="Engine oil"
                  value={f['oilSpec'] as string}
                  note={f['oilCapacityLitres'] !== null ? `${f['oilCapacityLitres']} L` : undefined}
                />
                <Row
                  label="Coolant"
                  value={f['coolantSpec'] as string}
                  // The K-series head gasket is why this field is worth a screen: OAT only, red/pink, never
                  // mixed with IAT. Getting it wrong is how the frailty becomes a failure.
                  note={f['coolantCapacityLitres'] !== null ? `${f['coolantCapacityLitres']} L · OAT only, never mixed with IAT` : 'OAT only, never mixed with IAT'}
                />
                <Row
                  label="Fuel tank"
                  value={f['fuelTankCapacityLitres'] !== null && f['fuelTankCapacityLitres'] !== undefined ? `${f['fuelTankCapacityLitres']} L` : null}
                  note="the dashboard's full-tank range derives from this"
                />
                <Row label="Brake fluid" value={f['brakeFluidSpec'] as string} />
                <Row label="Transmission oil" value={f['transmissionOilSpec'] as string} />
                <Row label="Spark plugs" value={f['sparkPlugPart'] as string} />
                <Row label="Oil filter" value={f['oilFilterPart'] as string} />
                <Row label="Air filter" value={f['airFilterPart'] as string} />
                <Row label="Fuel filter" value={f['fuelFilterPart'] as string} />
                <Row label="Cabin filter" value={f['cabinFilterPart'] as string} />
              </Panel>
            </Wrap>
          </Section>

          <Section>
            <Wrap>
              <SectionHead
                title="Tyres"
                rule={<>the target, not the last reading</>}
                link={
                  <AppLink className="sec-link" to="tyres" reg={reg}>
                    Tyre log →
                  </AppLink>
                }
              />
              <Panel>
                <Row label="Size" value={t['tyreSize'] as string} />
                <Row
                  label="Pressure · front"
                  value={t['pressureFrontPsi'] !== null ? `${t['pressureFrontPsi']} psi` : null}
                  note={t['pressureFrontLadenPsi'] !== null ? `${t['pressureFrontLadenPsi']} psi laden` : undefined}
                />
                <Row
                  label="Pressure · rear"
                  value={t['pressureRearPsi'] !== null ? `${t['pressureRearPsi']} psi` : null}
                  note={t['pressureRearLadenPsi'] !== null ? `${t['pressureRearLadenPsi']} psi laden` : undefined}
                />
                <Row
                  label="Minimum tread"
                  value={t['minTreadMm'] !== null ? `${t['minTreadMm']} mm` : null}
                  note="MOT limit is 1.6 mm"
                />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Policies"
                rule={<>the inputs; the countdowns are on the dashboard</>}
                link={
                  <AppLink className="sec-link" to="dashboard" reg={reg}>
                    Renewals →
                  </AppLink>
                }
              />
              <Panel>
                <div className="derived num">
                  <span className="lockico">
                    <IntegrityPill>Inputs only</IntegrityPill>
                  </span>
                  <span>
                    These dates are stored. <b>Their countdowns are not</b> — the dashboard computes days
                    remaining at render, which is why the old spreadsheet's stored MOT countdown could go stale
                    and this cannot.
                  </span>
                </div>
                <Row label="Insurer" value={ins['insurer'] as string} note={ins['policyNumber'] as string} />
                <Row label="Cover" value={ins['coverType'] as string} note={ins['premium'] !== null ? `${money(Number(ins['premium']))}/yr` : undefined} />
                <Row
                  label="Excess"
                  value={ins['excessCompulsory'] !== null ? money(Number(ins['excessCompulsory'])) : null}
                  note={ins['excessVoluntary'] !== null ? `+ ${money(Number(ins['excessVoluntary']))} voluntary` : undefined}
                />
                <Row label="No-claims" value={ins['ncbYears'] !== null ? `${ins['ncbYears']} years` : null} />
                <Row label="Road tax" value={data.vedAnnualCost !== null ? `${money(data.vedAnnualCost)}/yr` : null} />
                <Row label="Breakdown" value={bd['provider'] as string} note={bd['policyNumber'] as string} />
                <Row label="Notes" value={data.notes} />
              </Panel>
            </Wrap>
          </Section>
        </>
      )}
    </AppShell>
  )
}
