/**
 * The limits on a picture in a question. The server enforces its own (see `Question` on the API: at most 5 pictures,
 * each at most 512 KB); these are stricter on purpose, so a picture that passes here is always accepted there.
 */
export const IMAGE_LIMITS = {
  /** The longest side after shrinking, in pixels. Wide enough for a diagram on a phone and on a desk. */
  maxEdgePx: 800,
  /** The largest picture after shrinking, in bytes. */
  maxBytes: 300 * 1024,
  /** The largest file we agree to open before shrinking it. */
  maxInputBytes: 10 * 1024 * 1024,
  /** The most pictures in one question. */
  maxPerQuestion: 5,
  acceptedTypes: ['image/png', 'image/jpeg', 'image/gif', 'image/webp'],
} as const;

/** A picture that cannot be used; the message says why and is meant to be shown to the author. */
export class ImageRejectedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ImageRejectedError';
  }
}

/** Whether the file's type is one the question bank accepts. SVG is left out on purpose: an SVG file can carry script. */
export function isAcceptedImageType(type: string): boolean {
  return (IMAGE_LIMITS.acceptedTypes as readonly string[]).includes(type);
}

/** The size to draw a picture at: its own size, shrunk (never enlarged) so the longest side fits `maxEdge`. */
export function scaledSize(width: number, height: number, maxEdge: number): { width: number; height: number } {
  const longest = Math.max(width, height);
  if (longest <= maxEdge) {
    return { width, height };
  }

  const scale = maxEdge / longest;
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

/** The decoded size, in bytes, of a base64 data URL. */
export function dataUrlBytes(dataUrl: string): number {
  const payload = dataUrl.slice(dataUrl.indexOf(',') + 1);
  const padding = payload.endsWith('==') ? 2 : payload.endsWith('=') ? 1 : 0;
  return Math.floor((payload.length * 3) / 4) - padding;
}
