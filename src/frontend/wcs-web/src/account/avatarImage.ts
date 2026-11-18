/**
 * Prepares a picked file for upload as an avatar. The server validates everything again (content type from the bytes, size, dimensions); this only
 * saves a round trip and makes large phone photos usable: an image bigger than 512 px is scaled down in the browser before it is sent.
 */
export const AVATAR_MAX_BYTES = 512 * 1024
const MAX_EDGE = 512
const MIN_EDGE = 32

export type AvatarProblem = 'type' | 'size' | 'image'
export class AvatarPrepError extends Error {
  readonly problem: AvatarProblem
  constructor(problem: AvatarProblem) { super(problem); this.problem = problem }
}

const toBlob = (canvas: HTMLCanvasElement, type: string, quality?: number) =>
  new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, quality))

export async function prepareAvatar(file: File): Promise<Blob> {
  if (file.type !== 'image/png' && file.type !== 'image/jpeg') throw new AvatarPrepError('type')
  let bitmap: ImageBitmap
  try { bitmap = await createImageBitmap(file) } catch { throw new AvatarPrepError('image') }
  try {
    const { width, height } = bitmap
    if (width < MIN_EDGE || height < MIN_EDGE) throw new AvatarPrepError('image')
    if (width <= MAX_EDGE && height <= MAX_EDGE && file.size <= AVATAR_MAX_BYTES) return file

    const scale = Math.min(1, MAX_EDGE / Math.max(width, height))
    const w = Math.max(MIN_EDGE, Math.round(width * scale)), h = Math.max(MIN_EDGE, Math.round(height * scale))
    const canvas = document.createElement('canvas'); canvas.width = w; canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new AvatarPrepError('image')
    if (file.type === 'image/jpeg') { ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, w, h) }
    ctx.drawImage(bitmap, 0, 0, w, h)
    let out = await toBlob(canvas, file.type, 0.9)
    if (out && out.size > AVATAR_MAX_BYTES && file.type === 'image/png') {   // a photo saved as PNG: JPEG is far smaller (transparency becomes white)
      ctx.globalCompositeOperation = 'destination-over'; ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, w, h)
      out = await toBlob(canvas, 'image/jpeg', 0.9)
    }
    if (!out) throw new AvatarPrepError('image')
    if (out.size > AVATAR_MAX_BYTES) throw new AvatarPrepError('size')
    return out
  } finally { bitmap.close() }
}
