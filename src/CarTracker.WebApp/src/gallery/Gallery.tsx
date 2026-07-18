import { useState } from 'react'
import { Btn, Mark } from '../components/Btn'
import { Cadence } from '../components/Cadence'
import { Contours } from '../components/Contours'
import { FChip, FSel, FSort, Filters } from '../components/Filters'
import { Icon, ICON_NAMES } from '../components/Icon'
import { Kv, Stats } from '../components/Kv'
import { DueBadge, IntegrityPill, Pill, PrioTag } from '../components/Pill'
import { RegPlate } from '../components/RegPlate'
import { Seg } from '../components/Seg'
import { Field, Select, Sheet } from '../components/Sheet'
import { StatTile, StatTiles } from '../components/StatTile'
import { CFoot, Panel, RuleMark, Section, SectionHead, Wrap } from '../components/layout'
import { DUE_STATUS, PRIORITY, type DueStatus, type Priority } from '../lib/status'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

const DUE_STATES = Object.keys(DUE_STATUS) as DueStatus[]
const PRIORITIES = Object.keys(PRIORITY) as Priority[]

/**
 * Every component, in every state, on one page.
 *
 * Not a demo — the verification surface for task 4. jsdom has no layout and no colour, so the unit tests can
 * assert the *mechanism* of the greyscale property (a status renders its label as text) but never the claim
 * itself. This page is what `filter: grayscale(1)` gets applied to in Chrome, which is the only place that
 * claim can actually be checked.
 *
 * It replaces App.tsx's scaffold body until task 5 brings routing and real screens.
 */
export function Gallery() {
  const [sheetOpen, setSheetOpen] = useState(false)
  const [greyscale, setGreyscale] = useState(false)
  const [chips, setChips] = useState<Record<string, boolean>>({ fuel: true, service: false })
  const [seg, setSeg] = useState<'yes' | 'no'>('yes')
  const { toast } = useToast()

  return (
    <div id="gallery" className={greyscale ? 'greyscale-proof' : undefined}>
      <PageHead
        eyebrow="Component gallery · task 4"
        title="Vocabulary"
        plate="BT53 AKJ"
        pmeta={
          <>
            Every component, every state · <b>the greyscale check</b> lives here
            <br />
            Figures below are fixtures, not derived — real data arrives with task 5
          </>
        }
      />

      <Wrap>
        <Section>
          <SectionHead
            title="Status"
            rule={
              <>
                the axis that must survive greyscale · <RuleMark>label first</RuleMark>, colour third
              </>
            }
          />
          <Panel>
            <Stats columns={4}>
              <Kv
                label="Greyscale proof"
                value={
                  <Btn onClick={() => setGreyscale((g) => !g)}>
                    {greyscale ? 'Restore colour' : 'Remove colour'}
                  </Btn>
                }
                note="every state must stay legible"
              />
              <Kv label="Due badges" value={<Row>{DUE_STATES.map((d) => <DueBadge key={d} due={d} />)}</Row>} />
              <Kv label="Priority" value={<Row>{PRIORITIES.map((p) => <PrioTag key={p} priority={p} />)}</Row>} />
              <Kv
                label="Integrity — a separate axis"
                value={
                  <Row>
                    <IntegrityPill>Unreliable</IntegrityPill>
                    <IntegrityPill>Recomputed</IntegrityPill>
                  </Row>
                }
                note="never a due state"
              />
            </Stats>

            <StatTiles>
              {/* The workbook's real counts: 18 definitions, and its Dashboard says 17 because the
                  never-logged one falls out of every bucket. These four must sum to 18. */}
              <StatTile due="Overdue" count={7} href="#g" />
              <StatTile due="DueSoon" count={3} href="#g" />
              <StatTile due="Ok" count={7} href="#g" />
              <StatTile due="NeverLogged" count={1} href="#g" />
            </StatTiles>

            <CFoot>
              <span>
                7 + 3 + 7 + 1 = <b>18</b> — the sheet counts 17, because never-logged joins no bucket
              </span>
              <span>
                Never logged is a real fourth state, <b>not a data-integrity flag</b>
              </span>
            </CFoot>
          </Panel>
        </Section>

        <Section>
          <SectionHead title="Pills" rule={<>a generic tone label — most uses are not status</>} />
          <Panel>
            <Stats columns={4}>
              <Kv label="Affirmative" value={<Row><Pill tone="ok">Owned</Pill><Pill tone="ok">Best</Pill><Pill tone="ok">Healthy</Pill></Row>} />
              <Kv label="Attention" value={<Row><Pill tone="soon">Monitoring</Pill><Pill tone="soon">On order</Pill></Row>} />
              <Kv label="Neutral" value={<Row><Pill tone="plain">No budget</Pill><Pill tone="never">Never logged</Pill></Row>} />
              <Kv label="Cadence — not a status" value={<Row><Cadence>Weekly</Cadence><Cadence>Monthly</Cadence></Row>} />
            </Stats>
          </Panel>
        </Section>

        <Section>
          <SectionHead title="Icons" rule={<>8 sprite symbols · DEC-013</>} />
          <Panel>
            <Stats columns={4}>
              {ICON_NAMES.map((name) => (
                <Kv key={name} label={name} value={<Icon name={name} />} />
              ))}
            </Stats>
            <CFoot>
              <span>
                Text glyphs stay text: <b>Δ prior</b> · <b>CO₂</b> · <b>≈ 206 days</b> · <b>28.7 MPG ≡ 9.8 L/100 km</b> ·{' '}
                <b>date ↓</b>
              </span>
            </CFoot>
          </Panel>
        </Section>

        <Section>
          <SectionHead title="Controls" link={<Mark onClick={() => toast('Toast fired · one owner, one timer, 4000ms')}>Fire a toast</Mark>} />
          <Panel>
            <Stats columns={4}>
              <Kv label="Primary" value={<Btn onClick={() => setSheetOpen(true)}><Icon name="plus" /> Fuel</Btn>} />
              <Kv label="Ghost" value={<Btn variant="ghost" onClick={() => {}}>Dismiss</Btn>} />
              <Kv label="Mark" value={<Mark onClick={() => {}}>Mark done today</Mark>} />
              <Kv label="Segmented" value={<Seg label="Underbody rinse" value={seg} onChange={setSeg} options={[{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }]} />} />
            </Stats>

            <div style={{ padding: '0 18px 8px' }}>
              <Filters>
                <FChip active={chips.fuel ?? false} onClick={() => setChips((c) => ({ ...c, fuel: !c.fuel }))}>
                  Fuel
                </FChip>
                <FChip active={chips.service ?? false} onClick={() => setChips((c) => ({ ...c, service: !c.service }))}>
                  Service
                </FChip>
                <FSel label="Category" defaultValue="all">
                  <option value="all">All categories</option>
                  <option value="fuel">Fuel</option>
                </FSel>
                <FSort>sorted · date ↓</FSort>
              </Filters>
            </div>
            <CFoot>
              <span>
                Filter chips use <b>aria-pressed</b> — independent toggles. The segmented control is a{' '}
                <b>radiogroup</b> — one choice among N. Same look, different meaning.
              </span>
            </CFoot>
          </Panel>
        </Section>

        <Section>
          <SectionHead title="Reg plate & contours" />
          <Panel>
            <Stats columns={4}>
              <Kv label="Small" value={<RegPlate reg="BT53 AKJ" />} />
              <Kv label="Large" value={<RegPlate reg="BT53 AKJ" size="lg" />} />
              <Kv label="Null figure" value={<span style={{ fontSize: 13 }}>No previous fill</span>} note="says so, never blank" />
              <Kv label="Contours" value={<span style={{ position: 'relative', display: 'block', height: 40, background: 'var(--head-bg)', overflow: 'hidden', borderRadius: 4 }}><Contours variant="card" /></span>} />
            </Stats>
          </Panel>
        </Section>

        <Section last>
          <SectionHead title="Sheet" rule={<>Escape · focus trap · restore · scroll lock · inert · Enter submits</>} />
          <Panel>
            <Stats columns={4}>
              <Kv label="Open it" value={<Btn onClick={() => setSheetOpen(true)}>Add fuel</Btn>} note="then press Escape, or Tab around" />
            </Stats>
          </Panel>
        </Section>
      </Wrap>

      <Sheet
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        title="Add fuel"
        subtitle="last 10 Jul · 80,712 mi"
        onSubmit={() => {
          setSheetOpen(false)
          toast('Fill logged · MPG computed live · mirrored to expenses')
        }}
        footer={<Btn onClick={() => {}} type="submit">Save fill</Btn>}
      >
        <Field label="Mileage">{(p) => <input type="text" inputMode="numeric" placeholder="80712" {...p} />}</Field>
        <Field label="Litres" hint="the sole basis of MPG — not the fill level">
          {(p) => <input type="text" inputMode="decimal" placeholder="47.03" {...p} />}
        </Field>
        <Field label="Price / litre">{(p) => <input type="text" inputMode="decimal" placeholder="1.799" {...p} />}</Field>
        {/* A <Select> inside a <Field> — the combination the gallery lacked, which is why two chevrons
            (native + masked) reached a real form before anything noticed. */}
        <Field label="Fill level" hint="Full closes the tank and measures MPG; Half/Quarter defers it">
          {(p) => (
            <Select {...p}>
              <option>Full</option>
              <option>Half</option>
              <option>Quarter</option>
            </Select>
          )}
        </Field>
      </Sheet>
    </div>
  )
}

function Row({ children }: { children: React.ReactNode }) {
  return <span style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>{children}</span>
}

/** The gallery inside the real shell, so the shell is on trial here too. */
export function GalleryPage() {
  const { toast } = useToast()
  return (
    <AppShell
      scope={{ kind: 'vehicle', reg: 'BT53 AKJ' }}
      current="dashboard"
      center={{ kind: 'action', icon: 'plus', label: 'Quick add', onClick: () => toast('Quick add') }}
      footer={
        <>
          Component gallery — the vocabulary the 17 screens are built from. Every figure here is a fixture;{' '}
          <b>nothing is derived yet</b>. Real data arrives with task 5.
        </>
      }
    >
      <Gallery />
    </AppShell>
  )
}
