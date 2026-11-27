import { decodeImage, encodeScaled } from './imageScale'

/** The grid's preview: small enough that a month of photos loads on a forecourt's signal. */
export const THUMBNAIL_EDGE = 480
/** The viewer's copy: sharp on a laptop, a fraction of a phone original's bytes. */
export const DISPLAY_EDGE = 2048

/** Kept in step with the server's caps (`DocumentStore.MaxThumbnailBytes`, `MaxDisplayBytes`). */
export const MAX_THUMBNAIL_BYTES = 300 * 1024
export const MAX_DISPLAY_BYTES = 3 * 1024 * 1024

export interface Renditions {
  thumbnail: Blob
  display: Blob
  /** The decoded original's size in pixels, after EXIF orientation. */
  width: number
  height: number
}

/**
 * The two previews the browser makes for an image document: a thumbnail and a display copy.
 *
 * Null when there is nothing to make: not an image, an image this browser cannot decode (HEIC in Chrome,
 * Firefox and Edge), or one that will not fit its cap even at low quality. The upload goes ahead without
 * renditions in every one of those cases; the server never requires them.
 *
 * **Every blob returned came out of `canvas.toBlob`**, never the original or a slice of it, even when the
 * original is a small JPEG that needs no scaling. The re-encode is what strips EXIF, GPS included, so a
 * shortcut here would put the camera's location in every preview (assumed: A3).
 */
export async function makeRenditions(file: Blob): Promise<Renditions | null> {
  if (!file.type.startsWith('image/')) return null

  let bitmap: ImageBitmap
  try {
    bitmap = await decodeImage(file)
  } catch {
    return null
  }

  try {
    const thumbnail = await encodeWithin(bitmap, THUMBNAIL_EDGE, [0.8, 0.6, 0.4], MAX_THUMBNAIL_BYTES)
    if (thumbnail === null) return null
    const display = await encodeWithin(bitmap, DISPLAY_EDGE, [0.85, 0.7, 0.5], MAX_DISPLAY_BYTES)
    if (display === null) return null

    return { thumbnail, display, width: bitmap.width, height: bitmap.height }
  } catch {
    return null
  } finally {
    bitmap.close()
  }
}

/** The first encoding, by descending quality, that fits under `cap`. Null when none does. */
async function encodeWithin(bitmap: ImageBitmap, edge: number, qualities: number[], cap: number) {
  for (const quality of qualities) {
    const blob = await encode(bitmap, edge, quality)
    if (blob.size <= cap) return blob
  }
  return null
}

/**
 * WebP, or JPEG where the canvas cannot encode WebP. `toBlob('image/webp')` does not fail on such a browser; it
 * quietly hands back a PNG, which is large and which the server refuses as a rendition.
 */
async function encode(bitmap: ImageBitmap, edge: number, quality: number): Promise<Blob> {
  const webp = await encodeScaled(bitmap, edge, 'image/webp', quality)
  return webp.type === 'image/webp' ? webp : encodeScaled(bitmap, edge, 'image/jpeg', quality)
}
