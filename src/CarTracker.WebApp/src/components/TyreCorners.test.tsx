import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { TyreCorners, type CornerReading } from './TyreCorners'

const base: CornerReading = {
  psiFrontLeft: 35, psiFrontRight: 35, psiRearLeft: 33, psiRearRight: 33, psiSpare: null,
  treadFrontLeft: 6, treadFrontRight: 6, treadRearLeft: 5.5, treadRearRight: 5.5,
}

describe('TyreCorners', () => {
  it('lays out the four corners with pressure and tread', () => {
    render(<TyreCorners reading={base} />)
    for (const label of ['Front left', 'Front right', 'Rear left', 'Rear right']) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
    expect(screen.getAllByText(/psi/).length).toBeGreaterThan(0)
    expect(screen.getAllByText('6.0 mm').length).toBe(2) // both fronts read 6.0
    expect(screen.getAllByText('5.5 mm').length).toBe(2) // both rears
  })

  it('renders the spare as never logged with no tread target', () => {
    render(<TyreCorners reading={base} />)
    const spare = screen.getByText('Spare').closest('.tyre-spare') as HTMLElement
    expect(within(spare).getByText('never logged')).toBeInTheDocument()
    expect(within(spare).getByText('no tread target')).toBeInTheDocument()
  })

  it('warns when a tread approaches the MOT limit', () => {
    render(<TyreCorners reading={{ ...base, treadFrontLeft: 2.4 }} />)
    expect(screen.getByText(/Approaching 1.6 mm/)).toBeInTheDocument()
  })

  it('flags a tread at or below the MOT limit as illegal', () => {
    render(<TyreCorners reading={{ ...base, treadRearRight: 1.5 }} />)
    expect(screen.getByText('Below MOT limit')).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = render(<TyreCorners reading={{ ...base, psiSpare: 35 }} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
