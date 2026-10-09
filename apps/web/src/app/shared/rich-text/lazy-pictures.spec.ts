import { Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, vi } from 'vitest';
import { LazyPictureLabels, attachLazyPictures, formatBytes } from './lazy-pictures';

describe('formatBytes', () => {
  it.each([
    [0, '0 B'],
    [900, '900 B'],
    [1024, '1.0 KB'],
    [4500, '4.4 KB'],
    [10 * 1024, '10 KB'],
    [250 * 1024, '250 KB'],
    [1024 * 1024, '1.0 MB'],
    [int(2.5 * 1024 * 1024), '2.5 MB'],
  ])('writes %d bytes as %s', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected);
  });

  function int(value: number): number {
    return Math.round(value);
  }
});

describe('attachLazyPictures (FR-53)', () => {
  const labels: LazyPictureLabels = {
    load: (size, alt) => (alt === '' ? `Show picture (${size})` : `Show picture: ${alt} (${size})`),
    loading: 'Loading picture…',
    failed: (size) => `Could not load. Try again (${size})`,
  };

  let root: HTMLElement;
  let shown: Map<string, string>;
  let urls = 0;

  const marker = (key: string, bytes: number, alt = '') =>
    `<img class="lazy-media lazy-media--${key} lazy-bytes--${bytes}"${alt === '' ? '' : ` alt="${alt}"`}>`;

  const buttons = () => Array.from(root.querySelectorAll<HTMLButtonElement>('button.lazy-picture'));

  beforeEach(() => {
    root = document.createElement('div');
    shown = new Map();
    urls = 0;
    URL.createObjectURL = vi.fn(() => `blob:picture-${++urls}`);
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => vi.restoreAllMocks());

  it('puts a button with the size in place of each marker, and fetches nothing yet', () => {
    root.innerHTML = `<p>Look</p>${marker('q-0', 45_000, 'A map')}${marker('q-1', 900)}`;
    const load = vi.fn();

    attachLazyPictures(root, load, labels, shown);

    expect(root.querySelector('img')).toBeNull();
    expect(buttons().map((b) => b.textContent)).toEqual(['Show picture: A map (44 KB)', 'Show picture (900 B)']);
    expect(buttons().every((b) => b.type === 'button')).toBe(true);
    expect(load).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Look');
  });

  it('fetches the picture by its key when the button is pressed, and shows it where the button was', () => {
    root.innerHTML = `<p>Look</p>${marker('q-0', 45_000, 'A map')}`;
    const fetched = new Subject<Blob>();
    const load = vi.fn(() => fetched);
    attachLazyPictures(root, load, labels, shown);

    buttons()[0].click();

    expect(load).toHaveBeenCalledWith('q-0');
    expect(buttons()[0].disabled).toBe(true);
    expect(buttons()[0].textContent).toBe('Loading picture…');

    fetched.next(new Blob(['x'], { type: 'image/png' }));

    expect(buttons()).toHaveLength(0);
    const image = root.querySelector('img');
    expect(image?.getAttribute('src')).toBe('blob:picture-1');
    expect(image?.getAttribute('alt')).toBe('A map');
    expect(shown.get('q-0')).toBe('blob:picture-1');
  });

  it('lets the candidate try again when the picture could not be fetched', () => {
    root.innerHTML = marker('q-0', 2048);
    const load = vi.fn().mockReturnValueOnce(throwError(() => new Error('offline'))).mockReturnValueOnce(new Subject<Blob>());
    attachLazyPictures(root, load, labels, shown);

    buttons()[0].click();

    expect(buttons()[0].disabled).toBe(false);
    expect(buttons()[0].textContent).toBe('Could not load. Try again (2.0 KB)');

    buttons()[0].click();

    expect(load).toHaveBeenCalledTimes(2);
    expect(buttons()[0].disabled).toBe(true);
  });

  it('puts a picture fetched before straight back, without fetching it again', () => {
    shown.set('q-0', 'blob:kept');
    root.innerHTML = marker('q-0', 45_000, 'A map');
    const load = vi.fn();

    attachLazyPictures(root, load, labels, shown);

    expect(buttons()).toHaveLength(0);
    expect(root.querySelector('img')?.getAttribute('src')).toBe('blob:kept');
    expect(load).not.toHaveBeenCalled();
  });

  it('leaves alone a picture that is not one of ours, and a marker with no key', () => {
    root.innerHTML = '<img alt="Other" src="data:image/png;base64,AAAA"><img class="lazy-media">';

    attachLazyPictures(root, vi.fn(), labels, shown);

    expect(root.querySelectorAll('img')).toHaveLength(2);
    expect(buttons()).toHaveLength(0);
  });

  it('stops a fetch still on its way when told to', () => {
    root.innerHTML = marker('q-0', 100);
    const fetched = new Subject<Blob>();
    const stop = attachLazyPictures(root, () => fetched, labels, shown);
    buttons()[0].click();
    expect(fetched.observed).toBe(true);

    stop();

    expect(fetched.observed).toBe(false);
  });
});
