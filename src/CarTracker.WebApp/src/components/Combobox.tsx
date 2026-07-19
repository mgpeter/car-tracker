import { useId, useState, type KeyboardEvent } from 'react'

export interface Suggestion {
  value: string
  /** A right-aligned note, e.g. a use-count "5×". */
  hint?: string | undefined
}

interface ComboboxProps {
  /** From the `Field` render prop, so the label associates. */
  id?: string
  value: string
  onChange: (value: string) => void
  suggestions: Suggestion[]
  placeholder?: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

/**
 * A free-type-or-pick combobox: type anything (the value is never constrained to the list), or open it to pick
 * a recent/known value. Built to the field-manual identity under the strict CSP, like `Spark`/`Seg` — no
 * library. It renders inside a `<Field>` render prop, so `id`/`aria-describedby`/`aria-invalid` thread through
 * and the red invalid outline works exactly as it does on a plain input.
 *
 * The keyboard model is the WAI-ARIA editable-combobox one: focus stays in the input, and Arrow keys move an
 * `aria-activedescendant` highlight through the `listbox` rather than moving DOM focus, so typing never breaks.
 */
export function Combobox({ id, value, onChange, suggestions, placeholder, ...aria }: ComboboxProps) {
  const listId = useId()
  const optBase = useId()
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)

  const query = value.trim().toLowerCase()
  // Empty query → the recent list as given; otherwise a case-insensitive contains filter.
  const matches = query === '' ? suggestions : suggestions.filter((s) => s.value.toLowerCase().includes(query))
  const showList = open && matches.length > 0
  const optionId = (i: number) => `${optBase}-${i}`

  const choose = (s: Suggestion) => {
    onChange(s.value)
    setOpen(false)
    setActive(-1)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) return setOpen(true)
      setActive((a) => Math.min(a + 1, matches.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      if (showList && active >= 0 && active < matches.length) {
        e.preventDefault()
        choose(matches[active]!)
      }
    } else if (e.key === 'Escape') {
      if (open) {
        e.preventDefault()
        setOpen(false)
        setActive(-1)
      }
    }
  }

  return (
    <div className="cbx">
      <input
        {...aria}
        id={id}
        type="text"
        role="combobox"
        autoComplete="off"
        aria-expanded={showList}
        aria-controls={listId}
        aria-autocomplete="list"
        {...(showList && active >= 0 && { 'aria-activedescendant': optionId(active) })}
        value={value}
        placeholder={placeholder}
        onChange={(e) => {
          onChange(e.target.value)
          setOpen(true)
          setActive(-1)
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
        // Focus leaving the input closes the list. An option's mousedown is prevented (below), so a click still
        // lands before this fires.
        onBlur={() => {
          setOpen(false)
          setActive(-1)
        }}
      />
      {showList && (
        <ul className="cbx-list" role="listbox" id={listId}>
          {matches.map((s, i) => (
            <li
              key={s.value}
              id={optionId(i)}
              role="option"
              aria-selected={i === active}
              className={i === active ? 'cbx-opt active' : 'cbx-opt'}
              // preventDefault keeps focus in the input, so onBlur doesn't close the list before onClick selects.
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => choose(s)}
            >
              <span className="cbx-val">{s.value}</span>
              {s.hint !== undefined && <span className="cbx-hint">{s.hint}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
