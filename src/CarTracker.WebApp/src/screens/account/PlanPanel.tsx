import { useQuery } from '@tanstack/react-query'
import { ApiFailure, queryKeys, useAllowances, useMeta } from '../../api/queries'
import { apiRequest } from '../../api/client'
import { Panel } from '../../components/layout'
import type { components } from '../../api/generated/schema'

type AccountSummary = components['schemas']['AccountSummary']

/**
 * Which plan this account is on, and what it allows.
 *
 * **It states figures, not a sales pitch.** There is nowhere to send somebody who wants the paid tier yet - the
 * comp list is a configuration key and a restart - so a "Upgrade" button would be a control that cannot work,
 * which this app does not render. When checkout ships, this is the panel it goes in, and the entry point the
 * shell currently hides for an unentitled account becomes visible and routes here.
 *
 * **The assistant row says included-or-not rather than a token count.** A daily ceiling of 1,000,000 tokens is
 * a true fact about a thing nobody outside this repository can size, and printing it would be precision
 * standing in for information. The 429 says the number when the number starts to matter.
 */
export function PlanPanel() {
  const allowances = useAllowances()
  const chatConfigured = useMeta().data?.chatConfigured === true

  // The same cache entry DangerZonePanel fills, so opening this screen makes one request between them. It is
  // the only source of a *used* figure here: documents are rows and can be counted, while a lookup leaves
  // nothing behind and a token count is not the sort of thing to put on a settings screen.
  const summary = useQuery({
    queryKey: queryKeys.account,
    queryFn: async () => {
      const r = await apiRequest<AccountSummary>('/api/account/summary')
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const plan = allowances?.chatEnabled === true ? 'Pro' : 'Free'
  const documents = summary.data?.documentCount

  return (
    <Panel>
      <div className="setrow">
        <span className="sk">Plan</span>
        <span className="sv">
          <b>{allowances === undefined ? '…' : plan}</b>
          <i>
            {allowances === undefined
              ? 'reading your account'
              : plan === 'Pro'
                ? 'the assistant, and headroom on documents and lookups'
                : 'everything except the assistant'}
          </i>
        </span>
      </div>

      <div className="setrow">
        <span className="sk">Assistant</span>
        <span className="sv">
          <b>{allowances === undefined ? '…' : allowances.chatEnabled ? 'Included' : 'Not on this plan'}</b>
          <i>
            {/* Two reasons the assistant can be absent, and they are not the same thing to act on: this
                deployment holds no model credential, or this account is on a plan without it. The shell hides
                the entry point either way, so this row is the only place the difference is visible at all. */}
            {allowances?.chatEnabled === true
              ? 'ask about your cars in the app, and it can log a fill or a service for you'
              : !chatConfigured
                ? 'this deployment holds no model credential, so the assistant is switched off for everybody'
                : 'the rest of the app is unaffected'}
          </i>
        </span>
      </div>

      <div className="setrow">
        <span className="sk">Documents</span>
        <span className="sv">
          <b>
            {documents === undefined || allowances === undefined
              ? '…'
              : `${documents} of ${allowances.maxDocuments}`}
          </b>
          <i>filed across every vehicle - 25 MB a file on any plan</i>
        </span>
      </div>

      <div className="setrow">
        <span className="sk">Registration lookups</span>
        <span className="sv">
          <b>{allowances === undefined ? '…' : `${allowances.dailyVehicleLookups} a day`}</b>
          <i>the DVLA pre-fill on the add-car form - typing the details in is never limited</i>
        </span>
      </div>
    </Panel>
  )
}
