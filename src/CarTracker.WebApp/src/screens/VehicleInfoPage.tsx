import { useState } from 'react'
import { useVehicleDetail, useVehicleSummary } from '../api/queries'
import { Mark } from '../components/Btn'
import { IntegrityPill } from '../components/Pill'
import { DerivedRow, SettingRow } from '../components/SettingRow'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { Icon } from '../components/Icon'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { CheckDefinitionsPanel } from './vehicle/CheckDefinitionsPanel'
import { VehicleLifecyclePanel } from './vehicle/VehicleLifecyclePanel'
import { VehicleEditSheet, type EditorId } from './vehicle/VehicleEditSheet'

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 })

const shortDate = (iso: string | null | undefined) =>
  iso === null || iso === undefined
    ? null
    : new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/**
 * Vehicle - everything this car has stored, and where it is changed.
 *
 * **The one screen that is honestly stored, and that is not a compromise.** An oil spec is not a measurement;
 * it is what the manual says goes in. Nothing here derives from a log because no log produces it, so nothing
 * here can drift out of step with one - which is the exact property the rest of the app has to work for. The
 * two exceptions are labelled as such: the MOT expiry, which comes from the latest pass record, and the
 * renewal countdowns, which are not repeated here at all.
 *
 * It used to be two screens. A read-only reference card lived here and a Settings screen edited the same
 * fields elsewhere, so the fuel tank, the insurer and the tyre pressures each appeared twice, in two files,
 * with links pointing back and forth. Merging them is why the sections are ordered by **read urgency** rather
 * than by importance: fluids and tyres first, because this is the screen you open at a tyre bay in bad light,
 * and the configuring reader arrives with intent and will scroll.
 */
export function VehicleInfoPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<EditorId | null>(null)

  const { data, isPending, isError, error, refetch } = useVehicleDetail(reg)
  const { data: summary } = useVehicleSummary(reg)

  /** Every edit control names what it edits: eight buttons reading only "Edit" is eight identical announcements. */
  const edit = (id: EditorId, label: string, verb: 'Edit' | 'Set' | 'Add' | 'Seed' = 'Edit') => (
    <Mark aria-label={`${verb} ${label}`} onClick={() => setEditing(id)}>
      {verb}
    </Mark>
  )

  const f = data?.fluids
  const t = data?.tyres
  const ins = data?.insurance
  const bd = data?.breakdown
  const mot = summary?.renewals.mot
  const roadTax = summary?.renewals.roadTax
  const insurance = summary?.renewals.insurance

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="vehicle-info"
      center={{ kind: 'link', screen: 'vehicle-info' }}
      footer={
        <>
          Everything on this screen is <b>stored</b>, and that is correct: a torque figure or an oil grade is
          what the manual says, not something measured. The countdowns these policies drive are computed on the{' '}
          <b>dashboard</b> and are not repeated here — two places showing the same number is two places to
          disagree. Every field merges on save, so <b>a blank leaves the stored value</b> rather than clearing it.
        </>
      }
    >
      <PageHead
        eyebrow="Vehicle · the reference card, and where its stored values are set"
        title="Vehicle"
        plate={plate}
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
          {/* First, because this is the bad-light section: the numbers you came to read standing next to the
              car. The coolant warning stays attached to the row it constrains rather than becoming a banner
              above ten rows - a caveat two rows away from its field is a caveat you can act against. */}
          <Section>
            <Wrap>
              <SectionHead
                title="Fluids & parts"
                rule={<>what the manual says goes in</>}
                link={edit('fluids', 'the fluid and parts specs')}
              />
              <Panel>
                <SettingRow
                  label="Engine oil"
                  value={f?.oilSpec}
                  note={f?.oilCapacityLitres != null ? `${f.oilCapacityLitres} L` : undefined}
                />
                <SettingRow
                  label="Coolant"
                  value={f?.coolantSpec}
                  // The K-series head gasket is why this field is worth a screen: OAT only, red/pink, never
                  // mixed with IAT. Getting it wrong is how the frailty becomes a failure.
                  note={
                    f?.coolantCapacityLitres != null
                      ? `${f.coolantCapacityLitres} L · OAT only, never mixed with IAT`
                      : 'OAT only, never mixed with IAT'
                  }
                />
                <SettingRow
                  label="Fuel tank"
                  value={f?.fuelTankCapacityLitres != null ? `${f.fuelTankCapacityLitres} L` : null}
                  note="the dashboard's full-tank range derives from this"
                  keepEmpty
                />
                <SettingRow label="Brake fluid" value={f?.brakeFluidSpec} />
                <SettingRow label="Transmission oil" value={f?.transmissionOilSpec} />
                <SettingRow label="Spark plugs" value={f?.sparkPlugPart} />
                <SettingRow label="Oil filter" value={f?.oilFilterPart} />
                <SettingRow label="Air filter" value={f?.airFilterPart} />
                <SettingRow label="Fuel filter" value={f?.fuelFilterPart} />
                <SettingRow label="Cabin filter" value={f?.cabinFilterPart} />
              </Panel>
            </Wrap>
          </Section>

          <Section>
            <Wrap>
              <SectionHead
                title="Tyres"
                rule={<>the targets; the log holds the readings</>}
                link={
                  <span className="head-actions">
                    {edit('tyres', 'the tyre specs')}
                    <AppLink className="sec-link" to="tyres" reg={reg}>
                      Tyre log →
                    </AppLink>
                  </span>
                }
              />
              <Panel>
                <SettingRow label="Size" value={t?.tyreSize} />
                <SettingRow
                  label="Pressure · front"
                  value={t?.pressureFrontPsi != null ? `${t.pressureFrontPsi} psi` : null}
                  note={t?.pressureFrontLadenPsi != null ? `${t.pressureFrontLadenPsi} psi laden` : undefined}
                />
                <SettingRow
                  label="Pressure · rear"
                  value={t?.pressureRearPsi != null ? `${t.pressureRearPsi} psi` : null}
                  note={t?.pressureRearLadenPsi != null ? `${t.pressureRearLadenPsi} psi laden` : undefined}
                />
                <SettingRow
                  label="Minimum tread"
                  value={t?.minTreadMm != null ? `${t.minTreadMm} mm` : null}
                  note="MOT limit is 1.6 mm"
                />
              </Panel>
            </Wrap>
          </Section>

          {/* The old Settings screen led with this, and anyone with that habit lands on it. Road tax and
              insurance each used to appear twice - the stored cost here, the derived expiry there - and are
              fused into one row apiece: the date the countdown runs to, with what it costs underneath. */}
          <Section>
            <Wrap>
              <SectionHead
                title="Statutory & policies"
                rule={<>the inputs; the countdowns are on the dashboard</>}
              />
              <Panel>
                {/* Not a row with an Edit button, and that is the point of the whole project in one control.
                    It derives from the latest pass record, and there is no field for it in the API either. */}
                <DerivedRow
                  label="MOT expiry"
                  badge={<IntegrityPill>Derived · read-only</IntegrityPill>}
                  value={shortDate(mot?.expiryDate) ?? 'no record yet'}
                  source={
                    mot?.source == null ? (
                      <>
                        No MOT record yet, and no seed. Add the pass record and this fills itself in - or seed
                        it below until you do.
                      </>
                    ) : (
                      <>
                        {mot.source}.{' '}
                        <AppLink to="service" reg={reg}>
                          Source record <Icon name="arrow-right" />
                        </AppLink>
                      </>
                    )
                  }
                />

                {/* The seed, and only while there is no record to derive from. A narrow escape hatch, not a
                    way to type the MOT: a pass record always wins, and once one exists this row disappears
                    because there is nothing left for it to answer. */}
                {mot?.source == null && (
                  <SettingRow
                    label="MOT expiry · seed"
                    value={shortDate(mot?.expiryDate)}
                    note="used only until an MOT record exists - a pass record always wins"
                    action={edit('motSeed', 'the MOT expiry seed', 'Seed')}
                    keepEmpty
                  />
                )}

                <SettingRow
                  label="Road tax · VED"
                  value={shortDate(roadTax?.expiryDate)}
                  note={
                    <>
                      {roadTax?.daysRemaining == null ? 'no renewal date' : `${roadTax.daysRemaining} days`}
                      {data.vedAnnualCost !== null && ` · ${money(data.vedAnnualCost)}/yr`}
                    </>
                  }
                  action={edit('ved', 'road tax')}
                  keepEmpty
                />

                <SettingRow
                  label="Insurance"
                  value={shortDate(insurance?.expiryDate)}
                  note={
                    <>
                      {insurance?.daysRemaining == null ? 'no renewal date' : `${insurance.daysRemaining} days`}
                      {ins?.insurer != null && ` · ${ins.insurer}`}
                      {ins?.coverType != null && ` · ${ins.coverType}`}
                    </>
                  }
                  action={edit('insurance', 'the insurance policy')}
                  keepEmpty
                />

                {/* Fields of the insurance sheet above, sitting under the row that opens it. */}
                <SettingRow label="Policy number" value={ins?.policyNumber} />
                <SettingRow
                  label="Premium"
                  value={ins?.premium != null ? `${money(Number(ins.premium))}/yr` : null}
                />
                <SettingRow
                  label="Excess"
                  value={ins?.excessCompulsory != null ? money(Number(ins.excessCompulsory)) : null}
                  note={
                    ins?.excessVoluntary != null ? `+ ${money(Number(ins.excessVoluntary))} voluntary` : undefined
                  }
                />
                <SettingRow label="No-claims" value={ins?.ncbYears != null ? `${ins.ncbYears} years` : null} />

                <SettingRow
                  label="Breakdown"
                  value={bd?.provider}
                  note={
                    <>
                      {bd?.policyNumber ?? 'no policy number'}
                      {bd?.expiry != null && ` · to ${shortDate(bd.expiry)}`}
                    </>
                  }
                  action={edit('breakdown', 'breakdown cover')}
                  keepEmpty
                />
              </Panel>
            </Wrap>
          </Section>

          {/* Two short blocks read together - what the car is, and what it cost. `.twoup` puts them side by
              side above 860px and stacks them below, which removes a section's worth of scroll on a desk
              without changing anything on a phone. */}
          <Section>
            <Wrap>
              <SectionHead title="Identity & purchase" rule={<>what the car is, and what it cost</>} />
              <div className="twoup">
                <div>
                  <SectionHead
                    className="sub"
                    title="Identity"
                    link={edit('identity', 'the identity details')}
                  />
                  <Panel>
                    <SettingRow label="Registration" value={data.registration} />
                    <SettingRow label="Make & model" value={data.name} note={data.variant} />
                    <SettingRow label="Year" value={data.year > 0 ? data.year : null} />
                    <SettingRow label="Colour" value={data.colour} />
                    <SettingRow label="Body style" value={data.bodyStyle} />
                    <SettingRow label="VIN" value={data.vin} />
                    <SettingRow
                      label="Engine"
                      value={data.engineCode}
                      note={
                        data.engineSizeCc !== null ? `${data.engineSizeCc} cc · ${data.fuelType}` : data.fuelType
                      }
                    />
                    <SettingRow label="Transmission" value={data.transmission} />
                    <SettingRow label="Drivetrain" value={data.drivetrain} />
                    <SettingRow
                      label="ULEZ"
                      value={
                        data.ulezCompliant === null ? null : data.ulezCompliant ? 'Compliant' : 'Not compliant'
                      }
                    />
                  </Panel>
                </div>

                <div>
                  <SectionHead
                    className="sub"
                    title="Purchase"
                    link={edit('purchase', 'the purchase details')}
                  />
                  <Panel>
                    <SettingRow label="Bought" value={shortDate(data.purchaseDate)} note={data.seller} />
                    <SettingRow
                      label="Price"
                      value={data.purchasePrice !== null ? money(data.purchasePrice) : null}
                      note="mirrored as a Purchase expense - it moves total outlay"
                      keepEmpty
                    />
                    <SettingRow
                      label="Odometer at purchase"
                      value={`${data.purchaseMileage.toLocaleString('en-GB')} mi`}
                      // Not trivia: MilesSincePurchase and cost-per-mile both rest on it, and it is the
                      // founding MileageReading rather than a number typed twice. Which is also why neither
                      // it nor the purchase date has an edit control: correcting them would leave that
                      // reading behind.
                      note="miles-since-purchase and cost-per-mile derive from this"
                    />
                    <SettingRow label="Default garage" value={data.defaultGarage} keepEmpty />
                  </Panel>
                </div>
              </div>
            </Wrap>
          </Section>

          {/* Its own section at last. This is a note about the car, not about its insurance - `Vehicle.Notes`
              is a root field, sibling to the insurance block, not a member of it - and rendering it as the
              last row under "Policies" said otherwise for as long as the screen has existed. */}
          <Section>
            <Wrap>
              <SectionHead
                title="Notes"
                rule={<>anything about this car that is not a field</>}
                link={edit('notes', 'the notes', data.notes === null ? 'Add' : 'Edit')}
              />
              <Panel className="pad">
                {data.notes === null || data.notes.trim() === '' ? (
                  <p className="panel-empty">
                    Nothing noted. Quirks, part numbers that are not in the list above, what the last mechanic
                    said - anything that does not fit a field.
                  </p>
                ) : (
                  <p style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{data.notes}</p>
                )}
              </Panel>
            </Wrap>
          </Section>

          {/* The only table on the page, and the only section that configures how the app watches the car
              rather than stating a fact about it. */}
          <Section>
            <Wrap>
              <CheckDefinitionsPanel reg={reg} />
            </Wrap>
          </Section>

          {/* Last, and last on purpose. The page is ordered by read urgency - fluids and tyres are what you
              open at a tyre bay in bad light - and retiring or destroying the car is the least urgent thing
              here and the one you least want to reach by accident. */}
          <Section last>
            <Wrap>
              <SectionHead
                title="Lifecycle"
                rule={<>mark it sold or SORN, or remove it for good</>}
              />
              <VehicleLifecyclePanel reg={reg} status={data.status} />
            </Wrap>
          </Section>

          <VehicleEditSheet
            reg={reg}
            which={editing}
            detail={data}
            summary={summary}
            onClose={() => setEditing(null)}
          />
        </>
      )}
    </AppShell>
  )
}
