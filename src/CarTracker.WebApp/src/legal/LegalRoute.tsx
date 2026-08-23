import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useMeta } from '../api/queries'
import { LegalLoading } from './LegalPage'

/**
 * The three public routes, guarded on whether this deployment publishes anything.
 *
 * **A deployment with no `Legal:` configuration has no legal pages** (DEC-024), so `/privacy` sends the
 * visitor to the landing page rather than rendering a document with nobody's name on it. That is the whole
 * point of the polarity: a NAS install must not tell somebody's household to write to a stranger about their
 * data, and the fail-safe direction for a legal document is to publish nothing.
 *
 * **The decision waits for `meta` to answer.** An in-flight response and an unpublished deployment are
 * indistinguishable from the client, and treating them alike would bounce somebody off a page that was about
 * to render - so `isPending` shows a splash and only a settled `legal === null` redirects. That is the
 * opposite treatment from the footer links, which hide while in flight; the difference is that a missing link
 * costs a moment and a wrong redirect costs the page.
 */
export function LegalRoute({ title, children }: { title: string; children: ReactNode }) {
  const meta = useMeta()

  if (meta.isPending) return <LegalLoading title={title} />

  // Any other failure renders the document anyway, on AuthGate's reasoning: a page that disappears whenever
  // the API is unreachable turns an outage into a missing privacy policy, and the pages need the server only
  // for whose name goes on them. The document itself handles a null publication.
  if (meta.isSuccess && (meta.data.legal ?? null) === null) return <Navigate to="/" replace />

  return <>{children}</>
}
