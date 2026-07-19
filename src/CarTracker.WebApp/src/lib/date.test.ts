import { describe, expect, it } from 'vitest'
import { addMonths, addYears, todayIso } from './date'

describe('todayIso', () => {
  it('is a YYYY-MM-DD string for local today', () => {
    expect(todayIso()).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    const d = new Date()
    const expected = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    expect(todayIso()).toBe(expected)
  })
})

describe('addMonths', () => {
  it('adds within a year', () => {
    expect(addMonths('2026-07-19', 6)).toBe('2027-01-19')
  })

  it('clamps the day to the target month', () => {
    // 31 Jan + 1 month has no 31 Feb — clamp to the last valid day, never roll into March.
    expect(addMonths('2026-01-31', 1)).toBe('2026-02-28')
    expect(addMonths('2024-01-31', 1)).toBe('2024-02-29') // leap year
  })

  it('keeps the day when the target month is long enough', () => {
    expect(addMonths('2026-07-19', 1)).toBe('2026-08-19')
  })
})

describe('addYears', () => {
  it('adds whole years', () => {
    expect(addYears('2026-07-19', 1)).toBe('2027-07-19')
  })

  it('clamps a leap day to the 28th', () => {
    expect(addYears('2024-02-29', 1)).toBe('2025-02-28')
  })
})
