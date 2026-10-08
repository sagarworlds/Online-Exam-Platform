import { Observable, Subscription } from 'rxjs';

/** Fetches one picture of a question by the key its marker carries. */
export type PictureLoader = (key: string) => Observable<Blob>;

/** The words on the button that stands in for a picture, in the candidate's language. */
export interface LazyPictureLabels {
  /** Offers the picture: its size, and what the author said it shows when they said anything. */
  load(size: string, alt: string): string;
  /** Said while the picture is on its way. */
  loading: string;
  /** Said when the picture could not be fetched and the button can be pressed again. */
  failed(size: string): string;
}

const KEY_PREFIX = 'lazy-media--';
const BYTES_PREFIX = 'lazy-bytes--';

/** A size a candidate can weigh against their connection: bytes, kilobytes or megabytes. */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(bytes < 10 * 1024 ? 1 : 0)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * Finds the pictures a question was sent without (FR-53) and puts a button in place of each, which fetches the picture when it is pressed.
 * The API leaves a marker for each, an `img` with the classes `lazy-media`, `lazy-media--{key}` and `lazy-bytes--{size}`, because a class
 * is what survives Angular's own sanitizer. A picture fetched once stays in `shown`, so showing the same text again
 * puts it straight back instead of fetching it again.
 *
 * @param root The element holding the question's sanitized HTML.
 * @param load Fetches a picture by its key.
 * @param labels What the buttons say.
 * @param shown The pictures already fetched, by key, as object URLs; this adds to it.
 * @returns A function that stops any fetch still on its way, for when the text is replaced or the page is left.
 */
export function attachLazyPictures(
  root: HTMLElement,
  load: PictureLoader,
  labels: LazyPictureLabels,
  shown: Map<string, string>,
): () => void {
  const fetching = new Set<Subscription>();

  for (const marker of Array.from(root.querySelectorAll<HTMLImageElement>('img.lazy-media'))) {
    const classes = Array.from(marker.classList);
    const key = classes.find((c) => c.startsWith(KEY_PREFIX))?.slice(KEY_PREFIX.length);
    if (key === undefined || key === '') {
      continue;
    }

    const alt = marker.getAttribute('alt') ?? '';
    const size = formatBytes(Number(classes.find((c) => c.startsWith(BYTES_PREFIX))?.slice(BYTES_PREFIX.length)) || 0);

    const cached = shown.get(key);
    if (cached !== undefined) {
      marker.replaceWith(pictureOf(root.ownerDocument, cached, alt));
      continue;
    }

    const button = root.ownerDocument.createElement('button');
    button.type = 'button';
    button.className = 'lazy-picture';
    button.textContent = labels.load(size, alt);
    button.addEventListener('click', () => {
      button.disabled = true;
      button.textContent = labels.loading;
      const subscription = load(key).subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          shown.set(key, url);
          button.replaceWith(pictureOf(root.ownerDocument, url, alt));
        },
        error: () => {
          button.disabled = false;
          button.textContent = labels.failed(size);
        },
      });
      fetching.add(subscription);
    });
    marker.replaceWith(button);
  }

  return () => {
    fetching.forEach((subscription) => subscription.unsubscribe());
    fetching.clear();
  };
}

function pictureOf(document: Document, url: string, alt: string): HTMLImageElement {
  const image = document.createElement('img');
  image.className = 'lazy-picture__image';
  image.alt = alt;
  image.src = url;
  return image;
}
