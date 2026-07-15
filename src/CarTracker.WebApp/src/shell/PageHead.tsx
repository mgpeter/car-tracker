import type { ReactNode } from 'react'
import { Contours } from '../components/Contours'
import { RegPlate } from '../components/RegPlate'
import { Wrap } from '../components/layout'

interface PageHeadProps {
  /** The sand caption above the title — "Wash log · cadence target every 3–4 weeks". */
  eyebrow: string
  title: string
  /** Omitted on unscoped screens. The garage hero has no plate; plates live on its cards. */
  plate?: string
  /** The right-hand context block. Rich, and often carries live figures. */
  pmeta?: ReactNode
}

/**
 * The standard page head — 15 of 17 screens.
 *
 * The dashboard's `.dossier` and the garage's `.g-hero` are NOT this component with different props: they are
 * different headers that happen to share the contour band and the plate. They compose `<Contours>` and
 * `<RegPlate>` directly and land with their screens. Forcing all three into one component would mean a prop
 * for every difference, which is a fair definition of the wrong abstraction.
 */
export function PageHead({ eyebrow, title, plate, pmeta }: PageHeadProps) {
  return (
    <header className="phead">
      <Contours variant="phead" />
      <Wrap className="phead-in">
        <div>
          <div className="eyebrow">{eyebrow}</div>
          <h1>
            {title}
            {plate !== undefined && (
              <>
                {' '}
                <RegPlate reg={plate} />
              </>
            )}
          </h1>
        </div>
        {pmeta !== undefined && <div className="pmeta num">{pmeta}</div>}
      </Wrap>
    </header>
  )
}
