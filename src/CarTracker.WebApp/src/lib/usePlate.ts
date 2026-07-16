import { useVehicleSummary } from '../api/queries'
import { useVehicleReg } from '../routes'

/**
 * The registration as it appears on the plate — "BT53 AKJ", not "bt53akj".
 *
 * **This exists because the same bug shipped twelve times.** The route param is normalised for matching, which
 * is right for a URL and wrong on a plate: no plate in Britain looks like `BT53AKJ`. It was fixed once on the
 * settings screen in M1c by reading `data.registration` instead — and then eleven more screens were written
 * with `plate={reg}`, because the fix lived in one file and the lesson lived in a commit message.
 *
 * The space cannot be reinserted by rule: a registration's format is a DVLA matter and the database holds the
 * real one. So the only correct source is the vehicle, and this makes reaching it a one-liner — the slug is a
 * fallback for the moment before the summary lands, not an answer.
 */
export function usePlate(): string {
  const reg = useVehicleReg()
  const { data } = useVehicleSummary(reg)
  return data?.registration ?? reg
}
