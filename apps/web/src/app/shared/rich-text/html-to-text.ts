/** What stands in for a picture where only text can be shown (a dropdown option, a one-line summary). */
export const IMAGE_PLACEHOLDER = '[Image]';

/** Elements that end a line of text, so adjacent paragraphs and list items do not run together once the markup is gone. */
const LINE_BREAKING = 'p, li, br, blockquote, pre, ul, ol';

/**
 * Reduces question HTML to one line of plain text, for the places that cannot show markup: a dropdown option, a list
 * summary. Pictures become {@link IMAGE_PLACEHOLDER} so a picture-only question is still recognisable.
 *
 * It parses with `DOMParser`, which builds an inert document: nothing in the HTML runs and no image is fetched,
 * so it is safe on text that has not been cleaned.
 *
 * @param html The question text as the API returns it.
 * @param maxLength Cut the result to this many characters, ending in an ellipsis; omit for no limit.
 */
export function htmlToPlainText(html: string | null | undefined, maxLength?: number): string {
  if (!html) {
    return '';
  }

  const body = new DOMParser().parseFromString(html, 'text/html').body;
  body.querySelectorAll('script, style').forEach((element) => element.remove());
  body.querySelectorAll('img').forEach((image) => image.replaceWith(` ${IMAGE_PLACEHOLDER} `));
  body.querySelectorAll(LINE_BREAKING).forEach((element) => element.after(' '));

  const text = (body.textContent ?? '').replace(/\s+/g, ' ').trim();
  return maxLength !== undefined && text.length > maxLength ? `${text.slice(0, Math.max(0, maxLength - 1)).trimEnd()}…` : text;
}
