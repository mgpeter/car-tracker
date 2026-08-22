import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Btn } from '../../components/Btn'
import { Seg } from '../../components/Seg'
import { Sheet } from '../../components/Sheet'
import { useAdminAccess } from '../../api/queries'
import { useToast } from '../../shell/Toast'
import {
  adminKeys,
  clearPlanOverride,
  getAdminUser,
  setPlanOverride,
  type AccountPlan,
} from '../../api/admin'

const when = (iso: string | null | undefined) =>
  iso === null || iso === undefined
    ? 'never'
    : new Date(iso).toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' })

const tokens = (n: number) => n.toLocaleString('en-GB')

const PLAN_OPTIONS: ReadonlyArray<{ value: AccountPlan; label: string }> = [
  { value: 'Free', label: 'Free' },
  { value: 'Pro', label: 'Pro' },
]

/**
 * One account, and the only write on this surface.
 *
 * **Setting a tier and clearing the override are different operations**, which is why there are two controls
 * rather than a three-way switch. Null means no administrator has decided anything and the account falls
 * through to the comp list; `Free` means it is pinned below whatever that list says. A control that collapsed
 * them would make the second unreachable, and the second is the only answer to a domain comp entry that has
 * caught somebody it should not.
 *
 * The plan control renders only for a principal holding `admin:plan:write`, tested `=== true`, so an in-flight
 * response hides it rather than offering a button that would answer 403.
 */
export function AccountSheet({ userId, onClose }: { userId: number; onClose: () => void }) {
  const client = useQueryClient()
  const { toast } = useToast()
  const admin = useAdminAccess()

  const { data, isError } = useQuery({
    queryKey: adminKeys.user(userId),
    queryFn: () => getAdminUser(userId),
  })

  // Invalidates the whole ['admin'] prefix rather than the row: a tier change moves the list entry, the
  // detail and nothing else here, but a narrower key would leave the list showing the old plan beside a sheet
  // showing the new one.
  const settle = (message: string) => {
    void client.invalidateQueries({ queryKey: adminKeys.all })
    toast(message)
  }

  const set = useMutation({
    mutationFn: (plan: AccountPlan) => setPlanOverride(userId, plan),
    onSuccess: (row) => settle(`${row.email} is now on ${row.plan}.`),
    onError: () => toast('Could not change the plan.'),
  })

  const clear = useMutation({
    mutationFn: () => clearPlanOverride(userId),
    onSuccess: (row) => settle(`Override cleared. ${row.email} is on ${row.plan}.`),
    onError: () => toast('Could not clear the override.'),
  })

  const account = data?.account
  const busy = set.isPending || clear.isPending

  return (
    <Sheet
      open
      onClose={onClose}
      title={account?.email ?? 'Account'}
      {...(account === undefined ? {} : { subtitle: `signed up ${when(account.createdAt)}` })}
      footer={
        <Btn onClick={onClose} variant="ghost">
          Close
        </Btn>
      }
    >
      {isError && <p className="faint">Could not read this account.</p>}
      {data === undefined && !isError && <p className="faint">Reading…</p>}

      {data !== undefined && account !== undefined && (
        <>
          <div className="setrow ro">
            <span className="sk">Plan</span>
            <span className="sv">
              <b>{account.plan}</b>
              <i>
                {account.planOverride === null
                  ? 'from the comp list and a verified address'
                  : 'set by an administrator'}
              </i>
            </span>
          </div>

          <div className="setrow ro">
            <span className="sk">Address</span>
            <span className="sv">
              <b>{account.emailVerified ? 'Verified' : 'Not verified'}</b>
              <i>last seen {when(account.lastSeenAt)}</i>
            </span>
          </div>

          <div className="setrow ro">
            <span className="sk">Cars</span>
            <span className="sv">
              <b>{account.vehicleCount === 0 ? 'None' : account.vehicleCount}</b>
              <i>
                {data.vehicles.length === 0
                  ? 'nothing in the garage'
                  : data.vehicles
                      .map((v) => `${v.maskedRegistration} · ${v.make} ${v.model} (${v.year})`)
                      .join(', ')}
              </i>
            </span>
          </div>

          <div className="setrow ro">
            <span className="sk">Assistant</span>
            <span className="sv">
              <b>{tokens(account.chatTokens30d)}</b>
              <i>
                tokens over 30 days, {account.chatTurns30d} turns · {tokens(account.chatTokensToday)} today
              </i>
            </span>
          </div>

          <div className="setrow ro">
            <span className="sk">Held</span>
            <span className="sv">
              <b>{account.documentCount}</b>
              <i>
                documents · {account.assistantTokenCount} assistant{' '}
                {account.assistantTokenCount === 1 ? 'token' : 'tokens'} ·{' '}
                {account.openAnomalyCount} open{' '}
                {account.openAnomalyCount === 1 ? 'flag' : 'flags'}
              </i>
            </span>
          </div>

          {admin?.canWritePlans === true && (
            <>
              <Seg
                label="Set plan"
                options={PLAN_OPTIONS}
                value={account.planOverride ?? account.plan}
                onChange={(plan) => {
                  if (busy) return
                  set.mutate(plan)
                }}
              />
              <p className="faint">
                Pinning a tier here takes effect on this account's next request, with no restart. Choosing{' '}
                <b>Free</b> holds it below the comp list rather than merely leaving it there.
              </p>
              {account.planOverride !== null && (
                <Btn onClick={() => clear.mutate()} variant="ghost" disabled={busy}>
                  Clear the override
                </Btn>
              )}
            </>
          )}
        </>
      )}
    </Sheet>
  )
}
