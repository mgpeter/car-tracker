import 'vitest'

interface AxeMatchers<R = unknown> {
  /** Asserts an axe-core run produced no violations. See `src/test/axe.ts`. */
  toHaveNoViolations(): R
}

declare module 'vitest' {
  interface Assertion<T = unknown> extends AxeMatchers<T> {}
  interface AsymmetricMatchersContaining extends AxeMatchers {}
}
