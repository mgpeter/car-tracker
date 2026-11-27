/** "8 Jul 2026" from an ISO calendar date. */
export const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** Sizes are for a human deciding whether a thing will download quickly, so one decimal is plenty. */
export const fileSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`

/** A file name without its extension: the first guess at a title, since the name itself is not stored. */
export const titleFromFileName = (name: string) => name.replace(/\.[^.]+$/, '')
