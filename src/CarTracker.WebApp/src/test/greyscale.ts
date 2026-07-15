import { within, type RenderResult } from '@testing-library/react'

/**
 * Asserts a rendered status carries its state in TEXT, not only in colour.
 *
 * The property this protects, from the spec's own overview: *"a greyscale screenshot of a status row still
 * distinguishes overdue from OK, because the stripe and mono label carry the state"*.
 *
 * jsdom has no layout and no colour, so it cannot test the *rendering*. That is not a weakness here — it is
 * why this assertion is worth having. It tests the thing that actually makes greyscale work: that the state's
 * name is present as readable text. A component that passed by painting `--due` and nothing else would fail,
 * because there would be no text to find. The visual half is proved in Chrome under `filter: grayscale(1)` in
 * stage 6; this half is what stops it regressing between those checks.
 *
 * Queries by TEXT, never by class. A test that asserted `.pill.due` would pass for a component that rendered
 * an empty coloured box — which is precisely the failure being guarded against.
 */
export function expectStateIsReadable(result: RenderResult | HTMLElement, expectedLabel: string) {
  const scope = 'container' in result ? within(result.container) : within(result)
  const el = scope.getByText(expectedLabel)

  if (el.textContent === null || el.textContent.trim() === '') {
    throw new Error(`the status renders no text — greyscale would erase it entirely`)
  }

  return el
}

/**
 * The stronger claim: two states must be distinguishable from their text alone.
 *
 * Colour-blind users and greyscale prints both collapse the colour channel. If two states share a label, the
 * only thing telling them apart is the thing that just disappeared.
 */
export function expectStatesDistinguishable(labels: readonly string[]) {
  const unique = new Set(labels.map((l) => l.trim().toLowerCase()))
  if (unique.size !== labels.length) {
    throw new Error(
      `two states share a label, so they are indistinguishable without colour: ${labels.join(' | ')}`,
    )
  }
}
