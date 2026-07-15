import { describe, expect, it } from 'vitest'
import { countdownText, countdownVerb, renewalPresentation } from './renewal'

describe('renewal labels', () => {
  it('never says Overdue', () => {
    // That word belongs to a check, where it means past its interval. A renewal expires, on a date, with
    // legal consequences. Keeping the two vocabularies apart is why `DueStatus` does not cover renewals.
    const all = [
      renewalPresentation('Ok', 243),
      renewalPresentation('Amber', 45),
      renewalPresentation('Red', 23),
      renewalPresentation('Red', -12),
      renewalPresentation(null, null),
    ]
    expect(all.map((p) => p.label)).not.toContain('Overdue')
  })

  it('separates expired from due, which share the Red bucket', () => {
    // The whole reason this takes two arguments. `UrgencyOf` is `< 30 => Red` with no floor, so an MOT 23
    // days away and one that lapsed 12 days ago arrive here identically. One is a reminder; the other is a
    // car that is illegal to drive.
    expect(renewalPresentation('Red', 23)).toEqual({ label: 'Due', tone: 'due' })
    expect(renewalPresentation('Red', -12)).toEqual({ label: 'Expired', tone: 'due' })
  })

  it('reads the sign even when urgency disagrees', () => {
    // Defensive: if the domain ever bucketed a negative as anything but Red, expired still wins. The sign is
    // the fact; the bucket is a summary of it.
    expect(renewalPresentation('Ok', -1).label).toBe('Expired')
  })

  it('does not call a missing date OK', () => {
    // OK is a claim. With no date there is nothing to base it on, and a freshly added vehicle has no MOT,
    // no policy and no VED — the state the design never renders because its four renewals are all populated.
    expect(renewalPresentation(null, null)).toEqual({ label: 'Not set', tone: 'never' })
    expect(renewalPresentation('Ok', null).label).toBe('Not set')
  })

  it('maps the two thresholds', () => {
    expect(renewalPresentation('Amber', 45).label).toBe('Due soon')
    expect(renewalPresentation('Ok', 243).label).toBe('OK')
  })
})

describe('countdown phrasing', () => {
  it('never renders a negative day count', () => {
    // "-12 days" is not a thing anyone says, and it is exactly what a naive template produces at the one
    // moment the sentence most needs to be plain.
    expect(countdownText(-12)).toBe('12 days ago')
    expect(countdownText(-1)).toBe('1 days ago')
  })

  it('handles the boundaries as English', () => {
    expect(countdownText(0)).toBe('today')
    expect(countdownText(1)).toBe('tomorrow')
    expect(countdownText(243)).toBe('243 days')
    expect(countdownText(null)).toBe('no date')
  })

  it('groups thousands', () => {
    expect(countdownText(6788)).toBe('6,788 days')
  })

  it('agrees with the verb in front of it', () => {
    expect(`${countdownVerb(-12)} ${countdownText(-12)}`).toBe('expired 12 days ago')
    expect(`${countdownVerb(243)} ${countdownText(243)}`).toBe('expires in 243 days')
    expect(`${countdownVerb(0)} ${countdownText(0)}`).toBe('expires today')
  })
})
