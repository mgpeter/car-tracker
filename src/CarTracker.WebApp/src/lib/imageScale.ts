/**
 * Decode once, draw scaled, encode: the canvas path the chat's attachments and the document renditions share.
 *
 * `createImageBitmap` applies the file's EXIF orientation, so a portrait phone photo decodes portrait. The
 * canvas re-encode is what drops every byte of metadata the camera wrote (GPS included): only pixels survive a
 * draw. Throws when the browser cannot decode the file (HEIC outside Safari); callers decide what that means.
 */
export function decodeImage(file: Blob): Promise<ImageBitmap> {
  return createImageBitmap(file)
}

/**
 * Draws `bitmap` so its long edge is at most `maxEdge` (never upscaled) and encodes it as `type`.
 *
 * Resolves with whatever `toBlob` produced, which is not always `type`: a canvas that cannot encode WebP
 * silently returns PNG. Callers that care check `blob.type`.
 */
export async function encodeScaled(
  bitmap: ImageBitmap,
  maxEdge: number,
  type: string,
  quality: number,
): Promise<Blob> {
  const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height))

  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)

  const context = canvas.getContext('2d')
  if (context === null) throw new Error('no 2d context')
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, quality))
  if (blob === null) throw new Error('the image could not be re-encoded')
  return blob
}
