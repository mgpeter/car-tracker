import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import type { VehicleSummary } from './client'

/**
 * The codegen loop's guard.
 *
 * The point of generating types is that a C# rename becomes a TypeScript build error rather than an undefined
 * at runtime — which is the defect class this whole project exists to eliminate, reintroduced at the wire.
 * But that only holds while something actually *reads* the generated types. Until a screen does (M1), a
 * rename would sail through: the types would regenerate, nothing would reference the old name, and the build
 * would stay green while the app quietly broke.
 *
 * So this file is the consumer, and it is deliberately about the properties that matter most.
 */

/**
 * These are TYPE assertions — `tsc -b` is what runs them, not vitest. Vitest only holds them somewhere that
 * gets compiled and noticed.
 *
 * The naive version (`const mpg: number | null = ({} as VehicleSummary).fuel.averageMpg`) fails at runtime for
 * a reason worth remembering: the cast is a lie the compiler allows, so `.fuel` is `undefined` and the test
 * explodes before its assertion means anything. Type queries never touch a value.
 */
type Fuel = VehicleSummary['fuel']
type Mileage = VehicleSummary['mileage']
type Spend = VehicleSummary['spend']
type Checks = VehicleSummary['checks']
type Entry = Fuel['entries'][number]

describe('the generated contract', () => {
  it('types every legitimately-null derived figure as nullable', () => {
    // Assigning null compiles ONLY if the type admits it. The domain returns null here for real reasons —
    // MPG with no previous fill, cost-per-mile at zero miles — and each null is something the UI must say out
    // loud rather than render blank. If any of these loses `| null`, this file stops compiling.
    const mpg: Fuel['averageMpg'] = null
    const best: Fuel['bestMpg'] = null
    const worst: Fuel['worstMpg'] = null
    const pricePerLitre: Fuel['averagePricePerLitre'] = null
    const currentMileage: Mileage['currentMileage'] = null
    const costPerMile: Spend['costPerMile'] = null

    expect([mpg, best, worst, pricePerLitre, currentMileage, costPerMile].every((v) => v === null)).toBe(true)
  })

  it('types the figures that can never be null as plain numbers', () => {
    // The inverse: these are assignable TO number, which a `number | null` is not. If the domain ever starts
    // returning null here it has changed its mind about something, and the screens need to know.
    const litres: number = 0 as Fuel['totalLitres']
    const fills: number = 0 as Fuel['fillCount']
    const overdue: number = 0 as Checks['overdueCount']

    expect([litres, fills, overdue]).toEqual([0, 0, 0])
  })

  it('carries the five check states the domain models', () => {
    // Never-logged is the point of the fourth — see CheckStatus's own comment; the workbook's Dashboard says 17
    // of 18 because it fell out of every bucket. Attention is the fifth: a bad verdict on the last log overrides
    // the date-derived status, so it needs its own count.
    const counts: number[] = [
      0 as Checks['okCount'],
      0 as Checks['dueSoonCount'],
      0 as Checks['overdueCount'],
      0 as Checks['neverLoggedCount'],
      0 as Checks['attentionCount'],
    ]
    expect(counts).toHaveLength(5)
  })

  it('exposes per-fill MPG with its reliability, not just a number', () => {
    // A per-fill MPG that is null, or present-but-unreliable, are different states — the fuel log renders
    // them differently, and the design's own sheet got this rule wrong.
    const perFillMpg: Entry['mpg'] = null
    const reliable: boolean = true as Entry['isReliable']
    const plausible: boolean = true as Entry['isPlausible']

    expect([perFillMpg, reliable, plausible]).toEqual([null, true, true])
  })

  // The document is a committed artifact, so a stale one is a real possibility: someone changes C#, does not
  // rebuild, and the front end generates from yesterday's contract. This catches the file going missing or
  // losing its shape; CI catches staleness by regenerating and diffing.
  it('generates from a contract that describes the summary', async () => {
    const contract = JSON.parse(
      await readFile(join(process.cwd(), '../../api-contract/v1.json'), 'utf8'),
    ) as { paths: Record<string, unknown>; components: { schemas: Record<string, unknown> } }

    expect(Object.keys(contract.paths)).toContain('/api/vehicles/{registration}/summary')
    expect(Object.keys(contract.components.schemas)).toContain('VehicleSummary')

    // The bug this guards: .NET emits numerics as ["number","string"] because JsonSerializerDefaults.Web
    // accepts a stringified number on input. Accurate for requests, wrong for responses — and it would make
    // every derived figure `number | string`, burying the null case that actually matters under a case that
    // never happens. NumericTypeSchemaTransformer narrows it; this asserts it stayed narrowed.
    const fuel = contract.components.schemas['FuelEconomySummary'] as {
      properties: Record<string, { type: string | string[] }>
    }
    expect(fuel.properties['averageMpg']!.type).toEqual(['null', 'number'])
    expect(fuel.properties['totalLitres']!.type).toBe('number')
  })
})
