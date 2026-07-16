import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { Kv } from '../components/Kv'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface WashItem {
  id: number
  washDate: string
  location: string | null
  washType: string | null
  cost: number | null
  mileage: number | null
  notes: string | null
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

const daysBetween = (a: string, b: string) =>
  Math.round((new Date(`${b}T00:00:00`).getTime() - new Date(`${a}T00:00:00`).getTime()) / 86_400_000)

/** The design's stated target: every 3–4 weeks. Salt and a 2003 Land Rover's arches are the reason. */
const TARGET_MIN = 21
const TARGET_MAX = 28

/**
 * The wash log.
 *
 * A short screen with one derived idea worth having: **cadence**. A list of dates tells you nothing; the gaps
 * between them tell you whether the 3–4 week target is being met, and on a 2003 Freelander with salted roads
 * that is a rust question rather than a vanity one.
 *
 * The gaps are computed here rather than stored, for the same reason as everything else: a stored "14 days
 * ago" is wrong tomorrow.
 */
export function WashPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [adding, setAdding] = useState(false)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'washes'] as const,
    queryFn: async () => {
      const result = await apiRequest<WashItem[]>(`/api/vehicles/${encodeURIComponent(reg)}/washes`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const washes = data ?? []
  const last = washes.at(-1)
  const today = new Date().toISOString().slice(0, 10)
  const sinceLast = last !== undefined ? daysBetween(last.washDate, today) : null

  // The mean gap between consecutive washes. Needs two — one wash has no cadence, and saying "0 days" would
  // be inventing one.
  const gaps = washes.slice(1).map((w, i) => daysBetween(washes[i]!.washDate, w.washDate))
  const averageGap = gaps.length > 0 ? Math.round(gaps.reduce((a, b) => a + b, 0) / gaps.length) : null
  const spend = washes.reduce((sum, w) => sum + (w.cost ?? 0), 0)

  const columns: Column<WashItem>[] = [
    {
      key: 'date',
      label: 'Date',
      width: '70px',
      priority: 'essential',
      render: (w) => (
        <b>
          {dayMonth(w.washDate)}
          <Sub>{year(w.washDate)}</Sub>
        </b>
      ),
    },
    {
      key: 'gap',
      label: 'Gap',
      width: '76px',
      align: 'right',
      render: (w) => {
        const i = washes.findIndex((x) => x.id === w.id)
        const gap = i > 0 ? daysBetween(washes[i - 1]!.washDate, w.washDate) : null
        return gap === null ? (
          <Absent>first</Absent>
        ) : (
          <>
            {gap}
            <Sub>days</Sub>
          </>
        )
      },
    },
    {
      key: 'where',
      label: 'Where',
      width: '1fr',
      render: (w) => (
        <>
          {w.location ?? <Absent>not recorded</Absent>}
          {w.washType !== null && <Sub>{w.washType}</Sub>}
        </>
      ),
    },
    {
      key: 'mileage',
      label: 'Odometer',
      width: '80px',
      align: 'right',
      priority: 'secondary',
      render: (w) => (w.mileage === null ? <Absent /> : w.mileage.toLocaleString('en-GB')),
    },
    {
      key: 'notes',
      label: 'Notes',
      width: '1fr',
      priority: 'secondary',
      render: (w) => w.notes ?? <Absent />,
    },
    {
      key: 'cost',
      label: 'Cost',
      width: '76px',
      align: 'right',
      priority: 'essential',
      render: (w) => (w.cost === null ? <Absent>free</Absent> : <b>{money(w.cost)}</b>),
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="wash"
      center={{ kind: 'action', icon: 'plus', label: 'Log wash', onClick: () => setAdding(true) }}
      footer={
        <>
          The gaps are the point, not the dates. A target of {TARGET_MIN}–{TARGET_MAX} days is about salt and
          arches on a 2003 Land Rover rather than appearance, and it is only visible as the interval between one
          wash and the next — which is computed here, never stored.
        </>
      }
    >
      <PageHead
        eyebrow="Wash log · cadence target every 3–4 weeks"
        title="Wash"
        plate={plate}
        pmeta={
          last === undefined ? undefined : (
            <>
              Last washed <b>{dayMonth(last.washDate)}</b> · {sinceLast} days ago
              <br />
              {averageGap === null
                ? 'one wash — no cadence yet'
                : `average gap ${averageGap} days · target ${TARGET_MIN}–${TARGET_MAX}`}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The wash log could not be loaded</h2>
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
          {washes.length > 0 && (
            <Section>
              <Wrap>
                <SectionHead title="Cadence" rule={<>from the gaps, not the dates</>} />
                <Panel className="stats num">
                  <Kv
                    label="Since last"
                    value={sinceLast === null ? '—' : `${sinceLast} days`}
                    note={
                      sinceLast === null
                        ? 'no washes'
                        : sinceLast > TARGET_MAX
                          ? `over the ${TARGET_MAX}-day target`
                          : 'within target'
                    }
                  />
                  <Kv
                    label="Average gap"
                    value={averageGap === null ? '—' : `${averageGap} days`}
                    // One wash has no cadence. Reporting "0 days" from a single row would be inventing one.
                    note={averageGap === null ? 'needs a second wash' : `target ${TARGET_MIN}–${TARGET_MAX}`}
                  />
                  <Kv label="Washes" value={String(washes.length)} note="logged" />
                  <Kv
                    label="Spend"
                    value={spend > 0 ? money(spend) : '—'}
                    note={spend > 0 ? 'on washes' : 'all free so far'}
                  />
                </Panel>
              </Wrap>
            </Section>
          )}

          <Section last>
            <Wrap>
              <SectionHead
                title="Washes"
                rule={<>newest first</>}
                link={<Mark onClick={() => setAdding(true)}>Log wash</Mark>}
              />
              {washes.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No washes logged. The cadence — the gap between one and the next — starts at the second,
                    because a single date has no interval to measure.
                  </p>
                </Panel>
              ) : (
                <DataTable
                  columns={columns}
                  rows={[...washes].reverse()}
                  rowKey={(w) => w.id}
                  label="Washes, newest first"
                />
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddWashSheet open={adding} onClose={() => setAdding(false)} reg={reg} />
    </AppShell>
  )
}

function AddWashSheet({ open, onClose, reg }: { open: boolean; onClose: () => void; reg: string }) {
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<WashItem>(`/api/vehicles/${encodeURIComponent(reg)}/washes`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          washDate: get('washDate'),
          location: get('location') || null,
          washType: get('washType') || null,
          cost: get('cost') === '' ? null : Number(get('cost')),
          mileage: get('mileage') === '' ? null : Number(get('mileage').replace(/[\s,]/g, '')),
          notes: get('notes') || null,
        }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'washes'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      toast('Wash logged · the cadence recomputed')
      setV({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Log wash"
      subtitle="the gap since the last one is the figure that matters"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : 'Save wash'}
        </Btn>
      }
    >
      <Field label="Date">
        {(p) => <input type="date" value={get('washDate')} onChange={(e) => set('washDate', e.target.value)} {...p} />}
      </Field>

      <Field label="Where" hint="created on first use">
        {(p) => <input type="text" placeholder="Home driveway" value={get('location')} onChange={(e) => set('location', e.target.value)} {...p} />}
      </Field>

      <Field label="Type">
        {(p) => <input type="text" placeholder="Snow foam + two bucket" value={get('washType')} onChange={(e) => set('washType', e.target.value)} {...p} />}
      </Field>

      <Field label="Cost £" hint="leave empty if it was free">
        {(p) => <input type="text" inputMode="decimal" placeholder="8.00" value={get('cost')} onChange={(e) => set('cost', e.target.value)} {...p} />}
      </Field>

      <Field label="Odometer" wide>
        {(p) => <input type="text" inputMode="numeric" placeholder="80,712" value={get('mileage')} onChange={(e) => set('mileage', e.target.value)} {...p} />}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" placeholder="underbody rinse — salted roads" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
      </Field>

      {error !== null && (
        <div className="field wide">
          <span className="hint" style={{ color: 'var(--due)' }} role="alert">
            {error}
          </span>
        </div>
      )}
    </Sheet>
  )
}
