import { IMAGE_LIMITS, dataUrlBytes, isAcceptedImageType, scaledSize } from './image-limits';

describe('image limits', () => {
  it('accepts PNG, JPEG, GIF and WebP, and nothing else', () => {
    for (const type of ['image/png', 'image/jpeg', 'image/gif', 'image/webp']) {
      expect(isAcceptedImageType(type)).toBe(true);
    }
    for (const type of ['image/svg+xml', 'image/bmp', 'application/pdf', 'text/html', '']) {
      expect(isAcceptedImageType(type)).toBe(false);
    }
  });

  it('keeps a picture that already fits', () => {
    expect(scaledSize(640, 480, 800)).toEqual({ width: 640, height: 480 });
    expect(scaledSize(800, 800, 800)).toEqual({ width: 800, height: 800 });
  });

  it('shrinks the longest side to the limit and keeps the proportions', () => {
    expect(scaledSize(1600, 1200, 800)).toEqual({ width: 800, height: 600 });
    expect(scaledSize(1000, 4000, 800)).toEqual({ width: 200, height: 800 });
  });

  it('never makes a side smaller than one pixel', () => {
    expect(scaledSize(1, 100000, 800)).toEqual({ width: 1, height: 800 });
  });

  it('measures the decoded size of a data URL', () => {
    // 3 bytes -> 4 base64 characters; padding is not counted.
    expect(dataUrlBytes('data:image/png;base64,AAAA')).toBe(3);
    expect(dataUrlBytes('data:image/png;base64,AAA=')).toBe(2);
    expect(dataUrlBytes('data:image/png;base64,AA==')).toBe(1);
    expect(dataUrlBytes(`data:image/png;base64,${'A'.repeat(4000)}`)).toBe(3000);
  });

  it('keeps the browser limits within what the server accepts', () => {
    expect(IMAGE_LIMITS.maxBytes).toBeLessThanOrEqual(512 * 1024);
    expect(IMAGE_LIMITS.maxPerQuestion).toBe(5);
  });
});
