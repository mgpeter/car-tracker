import axeCore, { type AxeResults, type ElementContext, type RunOptions } from 'axe-core'

// Written against axe-core directly rather than `vitest-axe`, which the spec named: vitest-axe@0.1.0 is the
// latest release and reaches into a Vitest internal (`__vitest_poll_takeover__`) that Vitest 4 removed, so it
// throws on import. The matcher below is the whole of what we used it for.

// jsdom has no layout engine, so rules that need computed geometry cannot reach a verdict and report as
// "incomplete" rather than passing or failing. Disabling them keeps results honest instead of noisy; real
// colour-contrast is covered by the greyscale check in task 4.8.
const JSDOM_UNSUPPORTED = ['color-contrast'] as const

export async function axe(container: ElementContext, options: RunOptions = {}): Promise<AxeResults> {
  return axeCore.run(container, {
    rules: Object.fromEntries(JSDOM_UNSUPPORTED.map((id) => [id, { enabled: false }])),
    ...options,
  })
}

export function toHaveNoViolations(results: AxeResults) {
  const { violations } = results
  if (violations.length === 0) {
    return { pass: true, message: () => 'expected accessibility violations, found none' }
  }

  const detail = violations
    .map((v) => {
      const nodes = v.nodes.map((n) => `      ${n.html}`).join('\n')
      return `  [${v.impact ?? 'unknown'}] ${v.id}: ${v.help}\n    ${v.helpUrl}\n${nodes}`
    })
    .join('\n\n')

  return {
    pass: false,
    message: () => `expected no accessibility violations, found ${violations.length}:\n\n${detail}`,
  }
}
