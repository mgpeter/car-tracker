import type { DocumentItem } from '../../api/documents'

const LONDON_DAY = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/London',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/** The London calendar day an instant falls on, as an ISO date: when a photo was uploaded, as the owner counts days. */
export const londonDay = (instant: string) => LONDON_DAY.format(new Date(instant))

/**
 * The day a photo is filed under: its own date when it has one, else the day it was uploaded, marked as such
 * so a caption can say "uploaded" rather than pass an upload day off as when the picture was taken (assumed: A7).
 */
export function photoDay(photo: Pick<DocumentItem, 'documentDate' | 'createdAt'>): { day: string; uploaded: boolean } {
  return photo.documentDate !== null
    ? { day: photo.documentDate, uploaded: false }
    : { day: londonDay(photo.createdAt), uploaded: true }
}

/** Newest first: by day, then by id, so two photos of the same day keep the order they were filed in reverse. */
export function newestFirst<T extends Pick<DocumentItem, 'id' | 'documentDate' | 'createdAt'>>(photos: readonly T[]): T[] {
  return [...photos].sort((a, b) => {
    const byDay = photoDay(b).day.localeCompare(photoDay(a).day)
    return byDay !== 0 ? byDay : b.id - a.id
  })
}

/** Oldest first, for a timeline and the compare view. */
export function oldestFirst<T extends Pick<DocumentItem, 'id' | 'documentDate' | 'createdAt'>>(photos: readonly T[]): T[] {
  return newestFirst(photos).reverse()
}

/** "November 2026" for an ISO date. */
export const monthLabel = (day: string) =>
  new Date(`${day.slice(0, 7)}-01T00:00:00`).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })
