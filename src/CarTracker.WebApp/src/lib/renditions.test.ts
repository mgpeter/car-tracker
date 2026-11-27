import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DISPLAY_EDGE, makeRenditions, MAX_THUMBNAIL_BYTES, THUMBNAIL_EDGE } from './renditions'

/**
 * jsdom has no image decoder and no canvas encoder, so both are stubbed: `createImageBitmap` answers with the
 * dimensions a real decode would, and `toBlob` records the canvas size and the type it was asked for and hands
 * back a blob of a type the test chooses. That is enough to pin the arithmetic and the fallbacks; whether a
 * real canvas drops EXIF is checked by hand (assumed: A3).
 */
interface Encoded {
  width: number
  height: number
  type: string
  quality: number | undefined
  blob: Blob
}

let encoded: Encoded[] = []
let answerType: (requested: string) => string = (requested) => requested
let answerSize: (requested: string, width: number) => number = () => 1000

function stubDecoder(width: number, height: number) {
  vi.stubGlobal('createImageBitmap', vi.fn(async () => ({ width, height, close: vi.fn() })))
}

beforeEach(() => {
  encoded = []
  answerType = (requested) => requested
  answerSize = () => 1000

  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
    () => ({ drawImage: vi.fn() }) as unknown as CanvasRenderingContext2D,
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
    this: HTMLCanvasElement,
    callback: BlobCallback,
    type?: string,
    quality?: number,
  ) {
    const requested = type ?? 'image/png'
    const blob = new Blob([new Uint8Array(answerSize(requested, this.width))], { type: answerType(requested) })
    encoded.push({ width: this.width, height: this.height, type: requested, quality, blob })
    callback(blob)
  })
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const photo = (type = 'image/jpeg', size = 4_000_000) =>
  new File([new Uint8Array(size)], 'IMG_0412.HEIC', { type })

describe('makeRenditions', () => {
  it('scales a phone photo to a 480 px thumbnail and a 2048 px display copy, both WebP', async () => {
    stubDecoder(4032, 3024)

    const result = await makeRenditions(photo())

    expect(result).not.toBeNull()
    expect(result!.width).toBe(4032)
    expect(result!.height).toBe(3024)
    expect(result!.thumbnail.type).toBe('image/webp')
    expect(result!.display.type).toBe('image/webp')

    const thumbnail = encoded.find((e) => Math.max(e.width, e.height) === THUMBNAIL_EDGE)
    const display = encoded.find((e) => Math.max(e.width, e.height) === DISPLAY_EDGE)
    expect(thumbnail).toMatchObject({ width: 480, height: 360, type: 'image/webp', quality: 0.8 })
    expect(display).toMatchObject({ width: 2048, height: 1536, type: 'image/webp', quality: 0.85 })
  })

  it('keeps a portrait photo portrait', async () => {
    stubDecoder(3024, 4032)

    const result = await makeRenditions(photo())

    expect(result).toMatchObject({ width: 3024, height: 4032 })
    expect(encoded.some((e) => e.width === 360 && e.height === 480)).toBe(true)
  })

  it('re-encodes as JPEG when the browser hands back PNG for WebP', async () => {
    stubDecoder(4032, 3024)
    // Safari before 17 and some others: canvas.toBlob('image/webp') silently returns a PNG.
    answerType = (requested) => (requested === 'image/webp' ? 'image/png' : requested)

    const result = await makeRenditions(photo())

    expect(result!.thumbnail.type).toBe('image/jpeg')
    expect(result!.display.type).toBe('image/jpeg')
    expect(encoded.filter((e) => e.type === 'image/jpeg')).toHaveLength(2)
  })

  it('gives no renditions for an image the browser cannot decode, such as HEIC in Chrome', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn(async () => {
      throw new DOMException('The source image could not be decoded.', 'InvalidStateError')
    }))

    expect(await makeRenditions(photo('image/heic'))).toBeNull()
    expect(encoded).toHaveLength(0)
  })

  it('gives no renditions for a PDF and never tries to decode one', async () => {
    const decode = vi.fn()
    vi.stubGlobal('createImageBitmap', decode)

    expect(await makeRenditions(new File(['%PDF-1.7'], 'invoice.pdf', { type: 'application/pdf' }))).toBeNull()
    expect(decode).not.toHaveBeenCalled()
  })

  it('never returns the original bytes, even for a small JPEG that needs no scaling', async () => {
    stubDecoder(320, 240)
    const small = photo('image/jpeg', 20_000)

    const result = await makeRenditions(small)

    // Canvas re-encoding is what drops EXIF (and with it GPS). A shortcut that passed a small original through
    // would publish the camera's metadata in every preview.
    expect(result!.thumbnail).not.toBe(small)
    expect(result!.display).not.toBe(small)
    const fromCanvas = encoded.map((e) => e.blob)
    expect(fromCanvas).toContain(result!.thumbnail)
    expect(fromCanvas).toContain(result!.display)
    // Not upscaled.
    expect(encoded.every((e) => e.width === 320 && e.height === 240)).toBe(true)
  })

  it('steps the quality down to fit the cap, and gives up rather than send an oversize rendition', async () => {
    stubDecoder(4032, 3024)
    answerSize = (_type, width) => (width === 480 ? MAX_THUMBNAIL_BYTES + 1 : 1000)

    expect(await makeRenditions(photo())).toBeNull()
    const thumbnails = encoded.filter((e) => e.width === 480)
    expect(thumbnails.length).toBeGreaterThan(1)
    expect(thumbnails.map((e) => e.quality)).toEqual([...thumbnails.map((e) => e.quality)].sort().reverse())
  })
})
