import type { ReactNode } from 'react'

type Common = {
  children: ReactNode
  /** Only where an icon is the sole content. Text content names the control by itself. */
  'aria-label'?: string
}

/**
 * A button XOR a link — never "polymorphic".
 *
 * The design puts `.btn` and `.mark` on both `<button>` and `<a>` (`.mark` is its most-used class, 334 times,
 * and mostly on anchors). A single component with an `as` prop would let a caller render a *navigation* as a
 * `<button>` or an *action* as an `<a>`, which breaks middle-click, Ctrl+click, "open in new tab" and the
 * screen-reader announcement, all silently. The union makes `href` and `onClick` mutually exclusive, so the
 * element follows from what the thing actually does.
 */
type Action = Common & {
  onClick: () => void
  href?: undefined
  /** `submit` inside a `<Sheet onSubmit>` (stage 4). The design has no forms at all, so everything is a button. */
  type?: 'button' | 'submit'
}

type Link = Common & {
  href: string
  onClick?: undefined
  type?: undefined
}

type BtnProps = (Action | Link) & {
  /** `ghost` is the design's outline variant — 4 screens use it for a secondary action beside a solid one. */
  variant?: 'solid' | 'ghost'
}

/** The primary action. Filled, pill-shaped, uppercase mono. */
export function Btn({ variant = 'solid', children, ...rest }: BtnProps) {
  const className = variant === 'ghost' ? 'btn ghost' : 'btn'

  if (rest.href !== undefined) {
    return (
      <a className={className} href={rest.href} aria-label={rest['aria-label']}>
        {children}
      </a>
    )
  }

  return (
    <button className={className} type={rest.type ?? 'button'} onClick={rest.onClick} aria-label={rest['aria-label']}>
      {children}
    </button>
  )
}

/**
 * The secondary action: a small outlined pill that fills on hover.
 *
 * Same shape as `<Btn>`, lighter weight. Used for row-level actions ("Mark done today", "Edit fill →") and,
 * on 14 screens, as the mobile nav sheet's links.
 */
export function Mark({ children, ...rest }: Action | Link) {
  if (rest.href !== undefined) {
    return (
      <a className="mark" href={rest.href} aria-label={rest['aria-label']}>
        {children}
      </a>
    )
  }

  return (
    <button className="mark" type={rest.type ?? 'button'} onClick={rest.onClick} aria-label={rest['aria-label']}>
      {children}
    </button>
  )
}
