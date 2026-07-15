import { useRef } from 'react'

interface SegOption<T extends string> {
  value: T
  label: string
}

interface SegProps<T extends string> {
  /** Names the group. Required — an unlabelled radiogroup announces as an anonymous set of choices. */
  label: string
  options: ReadonlyArray<SegOption<T>>
  value: T
  onChange: (value: T) => void
}

/**
 * A segmented control: one choice among several.
 *
 * Rendered as a **radiogroup**, diverging from the design deliberately. The design uses `aria-pressed` on each
 * button, which says "toggle button" — so three of them side by side announce as three independent toggles
 * that merely look grouped, and nothing conveys that picking one unpicks the others. This is one choice among
 * N, which is what a radiogroup is for, and arrow-key navigation follows from the role rather than from extra
 * code. The visual treatment is the design's, unchanged; only the selected-state hook moves from
 * `[aria-pressed]` to `[aria-checked]`.
 *
 * Generic over the value so a caller cannot pass a string the union does not contain.
 */
export function Seg<T extends string>({ label, options, value, onChange }: SegProps<T>) {
  const groupRef = useRef<HTMLDivElement>(null)

  const move = (step: number) => {
    const i = options.findIndex((o) => o.value === value)
    const next = options[(i + step + options.length) % options.length]!
    onChange(next.value)

    // Synchronously, and scoped to THIS group. Both matter: the buttons already exist (selection only moves
    // tabIndex, it does not remount), so there is nothing to wait for — a rAF here just races the caller's
    // assertions. And a document-wide query would grab the wrong control the moment a screen has two Segs,
    // which Settings does.
    groupRef.current?.querySelector<HTMLButtonElement>(`[data-seg-option="${next.value}"]`)?.focus()
  }

  return (
    <div className="seg" role="radiogroup" aria-label={label} ref={groupRef}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={value === option.value}
          // Roving tabindex: the whole group is one tab stop, and arrows move within it.
          tabIndex={value === option.value ? 0 : -1}
          data-seg-option={option.value}
          onClick={() => onChange(option.value)}
          onKeyDown={(e) => {
            const forward = e.key === 'ArrowRight' || e.key === 'ArrowDown'
            const back = e.key === 'ArrowLeft' || e.key === 'ArrowUp'
            if (!forward && !back) return
            e.preventDefault()
            move(forward ? 1 : -1)
          }}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
