interface RegPlateProps {
  /** The registration, e.g. "BT53 AKJ". */
  reg: string
  /**
   * `sm` inline beside an <h1> (15 screens); `lg` as the dossier's hero plate.
   *
   * This is what the fork drift actually was: `theme.css` sets `.plate .reg` at 15px and dashboard's inlined
   * copy at 30px. Not two versions of one rule — one rule at two sizes, which is a prop.
   */
  size?: 'sm' | 'lg'
}

/**
 * A UK number plate.
 *
 * Renders the registration as text, so it is selectable, searchable and announced — the plate is chrome around
 * real content, not a picture of it. A non-breaking space keeps the two halves from wrapping.
 *
 * Its colours are theme-independent on purpose: a plate is yellow in dark mode too. See `--plate-*` in
 * tokens.css.
 */
export function RegPlate({ reg, size = 'sm' }: RegPlateProps) {
  return (
    <span className={`plate ${size}`}>
      <span className="reg">{reg.replace(/ /g, ' ')}</span>
    </span>
  )
}
