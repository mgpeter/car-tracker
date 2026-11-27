import { useEffect, useMemo } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { kindOf, useDocumentTypes, type DocumentTypeInfo } from '../api/documents'
import { useGarage } from '../api/queries'
import { Panel, Section, Wrap } from '../components/layout'
import { AppLink, hrefFor, type LinkQuery } from '../lib/link'
import { AppShell } from '../shell/AppShell'
import { cleanDate, cleanLink, cleanTitle, LINK_KEYS } from './documents/filingQuery'

type Outcome = { kind: 'waiting' } | { kind: 'go'; href: string } | { kind: 'not-yours' } | { kind: 'failed' }

const normalise = (registration: string) => registration.replace(/\s+/g, '').toUpperCase()

/**
 * Where a filing link goes: `/file?vehicle=...&type=...` from `offer_to_file`, opened from an MCP client.
 *
 * It matches the vehicle against this garage (case and spaces ignored), reads the category's kind from the
 * server's table, and replaces itself with the Photos or Documents screen, its upload sheet pre-filled. The
 * destination is always built by `hrefFor` from a registration in the garage, never taken from the query, and a
 * vehicle that is not in this garage is refused rather than swapped for the default (assumed: A1).
 */
export function FileLinkPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const garage = useGarage()
  const types = useDocumentTypes()

  const outcome = useMemo(
    (): Outcome => resolve(params, garage.data, garage.isError, types.data, types.isError),
    [params, garage.data, garage.isError, types.data, types.isError],
  )

  useEffect(() => {
    if (outcome.kind === 'go') void navigate(outcome.href, { replace: true })
  }, [outcome, navigate])

  // Inside the shell, so a refused link still leaves the top bar and the way back to the garage.
  return (
    <AppShell scope={{ kind: 'filing' }} current="file">
      <Wrap>
        <Section last>
          <Panel>
            {outcome.kind === 'not-yours' ? (
              <p className="panel-empty">
                This link is for a vehicle that isn&apos;t in your garage.{' '}
                <AppLink to="garage">Go to your garage</AppLink>
              </p>
            ) : outcome.kind === 'failed' ? (
              <p className="panel-empty" role="alert">
                Your garage could not be loaded, so this link cannot be opened yet.{' '}
                <AppLink to="garage">Go to your garage</AppLink>
              </p>
            ) : (
              <p className="panel-empty" role="status">
                Opening the filing link…
              </p>
            )}
          </Panel>
        </Section>
      </Wrap>
    </AppShell>
  )
}

function resolve(
  params: URLSearchParams,
  garage: { registration: string }[] | undefined,
  garageFailed: boolean,
  types: DocumentTypeInfo[] | undefined,
  typesFailed: boolean,
): Outcome {
  if (garageFailed) return { kind: 'failed' }
  if (garage === undefined || (types === undefined && !typesFailed)) return { kind: 'waiting' }

  const wanted = params.get('vehicle')
  const vehicle = wanted === null ? undefined : garage.find((v) => normalise(v.registration) === normalise(wanted))
  if (vehicle === undefined) return { kind: 'not-yours' }

  // An unknown category is filed as Other on Documents, where the owner can re-tag it, rather than refused.
  const asked = params.get('type') ?? ''
  const known = types?.find((t) => t.type === asked)
  const type = known?.type ?? 'Other'
  const screen = kindOf(type, types) === 'Photo' ? 'photos' : 'documents'

  const prefill: LinkQuery = { file: 1, type }
  const title = cleanTitle(params.get('title'))
  if (title !== undefined) prefill['title'] = title
  const date = cleanDate(params.get('date'))
  if (date !== undefined) prefill['date'] = date
  const link = cleanLink(params)
  if (link !== undefined) prefill[LINK_KEYS[link.kind]] = link.id

  return { kind: 'go', href: hrefFor(screen, vehicle.registration, prefill) }
}
