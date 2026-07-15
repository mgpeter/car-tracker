import type { ReactNode } from 'react'

/**
 * A filter strip.
 *
 * One of only four places in the whole design where anything scrolls horizontally — and all four are chip
 * strips. Data never scrolls sideways; it reflows. That distinction is deliberate and worth keeping: a chip
 * strip that runs off the edge still reads as "there are more filters", whereas a table that does is just
 * broken.
 */
export function Filters({ children }: { children: ReactNode }) {
  return <div className="filters">{children}</div>
}

interface FChipProps {
  active: boolean
  onClick: () => void
  children: ReactNode
}

/**
 * A toggleable filter chip.
 *
 * `aria-pressed` is correct here, unlike on `<Seg>`: filters are independent toggles — several can be on at
 * once — which is exactly what a toggle button is. `<Seg>` is one choice among N, so it gets radio semantics
 * instead. Same design vocabulary, two different meanings, and the difference is real rather than pedantic.
 */
export function FChip({ active, onClick, children }: FChipProps) {
  return (
    <button className="fchip" type="button" aria-pressed={active} onClick={onClick}>
      {children}
    </button>
  )
}

/** A filter dropdown. Wrapped for the chevron — see `<Select>`. */
export function FSel({
  label,
  children,
  ...rest
}: React.SelectHTMLAttributes<HTMLSelectElement> & { label: string; children: ReactNode }) {
  return (
    <span className="sel-wrap fsel-wrap">
      {/* A filter has no visible label in the design — the options name it ("All categories"). aria-label
          keeps it from announcing as an anonymous combobox. */}
      <select className="fsel" aria-label={label} {...rest}>
        {children}
      </select>
    </span>
  )
}

/**
 * The sort indicator — "sorted · date ↓".
 *
 * A plain `<span>`, not a control: sorting is not interactive in the design, so this is status text. Rendering
 * it as a button would promise something that does not happen.
 */
export function FSort({ children }: { children: ReactNode }) {
  return <span className="fsort">{children}</span>
}
