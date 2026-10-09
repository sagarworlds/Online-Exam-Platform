import { InjectionToken } from '@angular/core';
import { IMAGE_LIMITS, ImageRejectedError, dataUrlBytes, isAcceptedImageType, scaledSize } from './image-limits';

/** Turns a picked file into a data URL that fits the limits. Injected into the editor so tests do not need a canvas. */
export type ImagePreparer = (file: File) => Promise<string>;

/** The picture-preparing function the editor uses; replace it in a test to avoid the browser's canvas. */
export const IMAGE_PREPARER = new InjectionToken<ImagePreparer>('IMAGE_PREPARER', {
  providedIn: 'root',
  factory: () => prepareImage,
});

/** JPEG qualities tried in turn until the picture fits, best first. */
const JPEG_QUALITIES = [0.85, 0.7, 0.55] as const;

/**
 * Shrinks a picture to the question bank's limits and returns it as a data URL, ready to embed in the question.
 *
 * A PNG stays a PNG when it still fits (crisp for diagrams and screenshots, and it keeps transparency); otherwise
 * it, and every JPEG and WebP, is re-encoded as a JPEG. A GIF is kept untouched or refused, because re-encoding
 * would flatten an animation to its first frame.
 *
 * @throws ImageRejectedError the file is not a usable picture, or cannot be made small enough.
 */
export async function prepareImage(file: File): Promise<string> {
  if (!isAcceptedImageType(file.type)) {
    throw new ImageRejectedError('Use a PNG, JPEG, GIF or WebP picture.');
  }
  if (file.size > IMAGE_LIMITS.maxInputBytes) {
    throw new ImageRejectedError(`That file is too large to open. Pictures can be up to ${IMAGE_LIMITS.maxInputBytes / (1024 * 1024)} MB.`);
  }

  if (file.type === 'image/gif') {
    if (file.size > IMAGE_LIMITS.maxBytes) {
      throw new ImageRejectedError(`A GIF can be at most ${IMAGE_LIMITS.maxBytes / 1024} KB. Use a PNG or JPEG for a bigger picture.`);
    }
    return readAsDataUrl(file);
  }

  const bitmap = await decode(file);
  try {
    const { width, height } = scaledSize(bitmap.width, bitmap.height, IMAGE_LIMITS.maxEdgePx);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    if (context === null) {
      throw new ImageRejectedError('This browser cannot prepare pictures.');
    }

    if (file.type === 'image/png') {
      context.drawImage(bitmap, 0, 0, width, height);
      const png = await toDataUrl(canvas, 'image/png');
      if (dataUrlBytes(png) <= IMAGE_LIMITS.maxBytes) {
        return png;
      }
    }

    // JPEG has no transparency: paint white first so a see-through area does not come out black.
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, width, height);
    context.drawImage(bitmap, 0, 0, width, height);
    for (const quality of JPEG_QUALITIES) {
      const jpeg = await toDataUrl(canvas, 'image/jpeg', quality);
      if (dataUrlBytes(jpeg) <= IMAGE_LIMITS.maxBytes) {
        return jpeg;
      }
    }
    throw new ImageRejectedError(`That picture is too detailed to fit in ${IMAGE_LIMITS.maxBytes / 1024} KB. Try a smaller or simpler one.`);
  } finally {
    bitmap.close();
  }
}

async function decode(file: File): Promise<ImageBitmap> {
  try {
    return await createImageBitmap(file);
  } catch {
    // A corrupt file or one with the wrong extension: the author needs to know, and the cause is always the file.
    throw new ImageRejectedError('That file could not be read as a picture.');
  }
}

function toDataUrl(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<string> {
  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob === null ? reject(new ImageRejectedError('The picture could not be prepared.')) : readAsDataUrl(blob).then(resolve, reject)),
      type,
      quality,
    );
  });
}

function readAsDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(new ImageRejectedError('The picture could not be read.'));
    reader.readAsDataURL(blob);
  });
}
