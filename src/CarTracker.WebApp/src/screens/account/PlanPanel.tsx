import { useQuery } from '@tanstack/react-query'
import { ApiFailure, queryKeys, useAllowances, useMeta, usePlan } from '../../api/queries'
import { apiRequest } from '../../api/client'
import { Panel } from '../../components/layout'
import type { components } from '../../api/generated/schema'

type AccountSummary = components['schemas']['AccountSummary']
type PlanReason = components['schemas']['PlanReason']

/**
 * Why this account is on the tier it is on, in one sentence each.
 *
 * **Four of these are different instructions**, which is the reason the server sends a reason at all rather
 * than only a plan. `NobodyIsComped` is the odd one: it is about the *deployment*, and the person reading it
 * is the only one who can act on it. It exists because 0.24.0 shipped to a live deployment with an empty comp
 * list, every account landed on Free, and the screen could say nothing more useful than "Free".
 *
 * Typed `Record<PlanReason, string>` off the generated union, so a sixth reason added server-side fails the
 * build here rather than rendering an empty line - the `Record<string, ...>` mistake `ANOMALY_KIND` made, and
 * which took a whole release to notice.
 */
const REASON: Record<PlanReason, string> = {
  Comped: 'ask about your cars in the app, and it can log a fill or a service for you',
  NotOnCompList: 'this account is not on the paid tier - ask whoever runs this deployment to add you',
  AddressNotVerified:
    'your address has not been confirmed yet, so it cannot be matched - follow the link the sign-in provider emailed you, then sign in again',
  AddressUnknown:
    'this deployment cannot read the email address behind your sign-in, so it cannot match you to a plan',
  NobodyIsComped: 'no account on this deployment is on the paid tier - Plans:CompEmails is empty',
}

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
  const account = usePlan()
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

  const plan = account?.plan
  const documents = summary.data?.documentCount

  return (
    <Panel>
      <div className="setrow">
        <span className="sk">Plan</span>
        <span className="sv">
          <b>{plan === undefined ? '…' : plan}</b>
          <i>
            {plan === undefined
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
            {/* The deployment holding no model credential outranks every account-level reason: it switches the
                assistant off for everybody, so telling one owner to ask for a comp would send them to somebody
                who cannot help. Below that, the server's own reason - see REASON, and note this row is the only
                place any of it is visible, because the shell just hides the entry point. */}
            {account === undefined
              ? 'reading your account'
              : !chatConfigured
                ? 'this deployment holds no model credential, so the assistant is switched off for everybody'
                : REASON[account.reason]}
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
