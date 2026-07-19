import { describe, expect, it } from 'vitest'
import { recentValues } from './recentValues'

const fills = (stations: (string | null | undefined)[]) => stations.map((station) => ({ station }))

describe('recentValues', () => {
  it('returns distinct values in the order given (most-recent-first)', () => {
    expect(recentValues(fills(['Shell', 'Esso', 'Shell', 'BP']), (r) => r.station)).toEqual(['Shell', 'Esso', 'BP'])
  })

  it('dedupes case-insensitively but keeps the first-seen casing', () => {
    expect(recentValues(fills(['Shell Kingston', 'shell kingston']), (r) => r.station)).toEqual(['Shell Kingston'])
  })

  it('skips blank, whitespace and nullish values, and trims', () => {
    expect(recentValues(fills(['  Esso  ', '', '   ', null, undefined, 'BP']), (r) => r.station)).toEqual(['Esso', 'BP'])
  })

  it('caps at the limit', () => {
    expect(recentValues(fills(['a', 'b', 'c', 'd']), (r) => r.station, 2)).toEqual(['a', 'b'])
  })
})
