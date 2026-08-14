import type { ChatFile } from '../api/chat'

/**
 * What the assistant can be asked to read, and what a phone actually produces.
 *
 * The list is the server's `ChatFiles.Readable`, and it is deliberately shorter than what the documents screen
 * will store: that one keeps bytes it never has to understand, this one is sent to a model to be read.
 */
export const READABLE = ['image/jpeg', 'image/png', 'image/webp', 'application/pdf']

/**
 * The long edge an image is reduced to.
 *
 * Both candidate models see a 2576 px long edge at full resolution and downscale anything larger themselves —
 * so sending a 4032 px phone photo uploads three times the bytes to be given back the same reading. Doing it
 * here makes the upload quick on a forecourt's signal, which is where this feature is used.
 */
export const MAX_EDGE = 2576

/** Kept in step with the server's per-file cap, which refuses anything larger with its own sentence. */
export const MAX_BYTES = 10 * 1024 * 1024

export type Prepared = { ok: true; value: ChatFile; preview: string } | { ok: false; reason: string }

/**
 * Turns something the owner picked into something the assistant can read.
 *
 * PDFs pass through **untransformed**: rasterising one discards its text layer, which is the most reliable
 * thing in an emailed certificate and the part a model reads most accurately.
 *
 * Images go through a canvas, which does three jobs at once — it downscales to {@link MAX_EDGE}, it
 * re-encodes to JPEG, and that re-encoding is what converts a **HEIC** photo, the default on every iPhone and
 * a format no model here accepts. The conversion only works where the browser can decode HEIC in the first
 * place (Safari can; Chrome on Windows cannot), so a failure is reported as a sentence naming the format
 * rather than as a silent drop — the owner can then share it as a JPEG, which every phone offers.
 */
export async function prepare(file: File): Promise<Prepared> {
  if (file.type === 'application/pdf') {
    if (file.size > MAX_BYTES) return { ok: false, reason: tooBig(file) }

    return {
      ok: true,
      value: { mediaType: 'application/pdf', data: await base64(file) },
      preview: file.name,
    }
  }

  if (!file.type.startsWith('image/')) {
    return { ok: false, reason: `${file.name} is not a photo or a PDF, so the assistant cannot read it.` }
  }

  try {
    const bitmap = await createImageBitmap(file)
    const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height))

    const canvas = document.createElement('canvas')
    canvas.width = Math.round(bitmap.width * scale)
    canvas.height = Math.round(bitmap.height * scale)

    const context = canvas.getContext('2d')
    if (context === null) throw new Error('no 2d context')

    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    bitmap.close()

    const blob = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob(resolve, 'image/jpeg', 0.85),
    )
    if (blob === null) throw new Error('the image could not be re-encoded')

    if (blob.size > MAX_BYTES) return { ok: false, reason: tooBig(file) }

    return {
      ok: true,
      value: { mediaType: 'image/jpeg', data: await base64(blob) },
      preview: file.name,
    }
  } catch {
    // Overwhelmingly HEIC on a browser that cannot decode it. Say the format, because "could not read" leaves
    // someone standing at a pump with no idea what to do next.
    return {
      ok: false,
      reason: `${file.name} is in a format this browser cannot open (HEIC photos do this). Share it as a JPEG and try again.`,
    }
  }
}

function tooBig(file: File): string {
  return `${file.name} is ${Math.ceil(file.size / (1024 * 1024))} MB and the limit is ${MAX_BYTES / (1024 * 1024)} MB.`
}

/** Base64 without the `data:` prefix — the wire wants the payload, not a URL. */
async function base64(blob: Blob): Promise<string> {
  const buffer = await blob.arrayBuffer()
  const bytes = new Uint8Array(buffer)

  // Chunked: one spread of a multi-megabyte array overflows the argument stack, and a phone photo is exactly
  // that size.
  let binary = ''
  const chunk = 0x8000
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk))
  }

  return btoa(binary)
}
