import { useMeta } from '../api/queries'
import type { components } from '../api/generated/schema'

export type LegalPublication = components['schemas']['LegalPublication']

/**
 * Who is accountable for the data on this deployment, or null while nothing is published.
 *
 * **Read as `?? null`, so an in-flight `meta` is indistinguishable from an unpublished deployment.** That is
 * the hide-when-absent polarity the three capability flags already use, and it is right here for the same
 * reason: a link to a page that will not exist is worse than a link that appears a moment late.
 *
 * The call itself is free. `useMeta` is anonymous and is the same cache entry `Footer` fills for the version
 * line, so a legal page makes no request the app was not making anyway - which is also why these pages need no
 * session and can sit outside the login wall.
 */
export function useLegal(): LegalPublication | null {
  return useMeta().data?.legal ?? null
}

/**
 * Which third parties this deployment actually sends data to.
 *
 * **Derived from the deployment's own capability flags rather than written into the prose** (DEC-024). A
 * disclosure written as a fixed sentence is a stored derived value in the one document whose whole worth is
 * being accurate: it would claim a processor an install does not use, or omit one it does, and nobody would
 * ever notice. An install holding no model credential must not tell its users that their photographs go to a
 * model API.
 *
 * Both flags are read as `=== true`, so an in-flight `meta` discloses nothing rather than over-claiming.
 */
export interface Processors {
  /** Always true. There is no deployment of this app without an identity provider. */
  identity: true
  /** Whether the in-app assistant can run here at all, which is what decides if anything reaches a model API. */
  assistant: boolean
  /** Whether a registration typed into the add-car sheet can reach DVLA and DVSA. */
  lookup: boolean
}

export function useProcessors(): Processors {
  const meta = useMeta().data

  return {
    identity: true,
    assistant: meta?.chatConfigured === true,
    lookup: meta?.vehicleLookupConfigured === true,
  }
}
