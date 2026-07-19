import { describe, expect, it } from 'vitest'
import { ApiFailure } from '../api/queries'
import { fieldError, formError, reportApiError } from './formErrors'

const httpError = (errors?: Record<string, string[]>, message = 'Bad Request') =>
  new ApiFailure({ kind: 'http', status: 400, message, ...(errors && { errors }) })

describe('reportApiError', () => {
  it('maps a server field error to the matching (lowercased) field key', () => {
    const errors = reportApiError(httpError({ Litres: ['A fill must have litres.'] }), ['litres', 'mileage'])
    expect(fieldError(errors, 'litres')).toBe('A fill must have litres.')
    expect(formError(errors)).toBeUndefined()
  })

  it('normalises inconsistent server casing against the field keys', () => {
    // Server sends PascalCase `Mileage` and lowercase `type`; both should land on their fields.
    const errors = reportApiError(httpError({ Mileage: ['> 0'], type: ['pick one'] }), ['mileage', 'type'])
    expect(fieldError(errors, 'mileage')).toBe('> 0')
    expect(fieldError(errors, 'type')).toBe('pick one')
  })

  it('folds keys with no matching field into the form-level banner, never dropping them', () => {
    // Collection-level (`Targets`) and dotted (`Insurance.PeriodEnd`) keys are not rendered fields.
    const errors = reportApiError(
      httpError({ Targets: ['duplicate category'], 'Insurance.PeriodEnd': ['ends before it starts'] }),
      ['amount'],
    )
    expect(fieldError(errors, 'amount')).toBeUndefined()
    const banner = formError(errors)
    expect(banner).toMatch(/duplicate category|ends before it starts/)
    // Both messages are preserved under the form key.
    expect(errors['_']).toHaveLength(2)
  })

  it('falls back to the message when there is no field map (conflict / network)', () => {
    const conflict = new ApiFailure({ kind: 'http', status: 409, message: "'BT53 AKJ' already exists" })
    const errors = reportApiError(conflict, ['registration'])
    expect(formError(errors)).toBe("'BT53 AKJ' already exists")
    expect(fieldError(errors, 'registration')).toBeUndefined()
  })

  it('handles a non-ApiFailure throwable', () => {
    expect(formError(reportApiError(new Error('boom')))).toBe('boom')
    expect(formError(reportApiError('weird'))).toBe('Could not save.')
  })
})
