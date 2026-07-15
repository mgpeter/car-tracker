import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { axe } from './axe'

describe('test infrastructure', () => {
  it('renders a component and queries it by accessible name', () => {
    render(<button type="button">Add fuel</button>)
    expect(screen.getByRole('button', { name: 'Add fuel' })).toBeInTheDocument()
  })

  it('reports an accessibility violation when one exists', async () => {
    // An image with no alt text is a known axe violation. This test exists so that a silently no-op axe —
    // which would make every later `toHaveNoViolations` pass vacuously — fails loudly here instead.
    const { container } = render(<img src="/x.png" />)
    const results = await axe(container)
    expect(results.violations.map((v) => v.id)).toContain('image-alt')
  })

  it('passes axe on accessible markup', async () => {
    const { container } = render(<button type="button">Add fuel</button>)
    expect(await axe(container)).toHaveNoViolations()
  })
})
