import katex from 'katex';

/** `$$ ... $$` is a display formula, `$ ... $` an inline one; a formula never spans a paragraph. */
const FORMULA = /\$\$([^$]+?)\$\$|\$([^$\s](?:[^$]*[^$\s])?)\$/g;

/** Elements whose text is shown as written: a dollar amount in a code sample is not a formula. */
const LITERAL = new Set(['PRE', 'CODE']);

/**
 * Turns the formulas in already-sanitized question HTML into rendered maths (FR-5). Formulas are kept in the stored
 * text as `$...$` and `$$...$$`, which the server's sanitizer leaves alone because it is plain text, so nothing
 * about maths widens what markup a candidate's browser will accept. KaTeX is run with `trust` off and builds its own
 * elements, so a formula cannot add a link or script; a formula it cannot parse is left as the author typed it.
 */
export function typesetMath(root: HTMLElement): void {
  const walker = root.ownerDocument.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: (node) => {
      for (let el = node.parentElement; el !== null && el !== root; el = el.parentElement) {
        if (LITERAL.has(el.tagName)) {
          return NodeFilter.FILTER_REJECT;
        }
      }
      return node.nodeValue?.includes('$') ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
    },
  });

  const texts: Text[] = [];
  for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
    texts.push(node as Text);
  }
  texts.forEach((text) => typesetText(text));
}

function typesetText(text: Text): void {
  const source = text.nodeValue ?? '';
  const parts: (string | HTMLElement)[] = [];
  let last = 0;
  for (const match of source.matchAll(FORMULA)) {
    const display = match[1] !== undefined;
    const rendered = render(display ? match[1] : match[2], display, text.ownerDocument);
    if (rendered === null) {
      continue;
    }
    parts.push(source.slice(last, match.index), rendered);
    last = match.index + match[0].length;
  }
  if (parts.length === 0) {
    return;
  }
  parts.push(source.slice(last));
  text.replaceWith(...parts.filter((p) => p !== '').map((p) => (typeof p === 'string' ? text.ownerDocument.createTextNode(p) : p)));
}

function render(formula: string, display: boolean, doc: Document): HTMLElement | null {
  const el = doc.createElement(display ? 'div' : 'span');
  try {
    katex.render(formula, el, { displayMode: display, throwOnError: true, trust: false, output: 'htmlAndMathml' });
    return el;
  } catch {
    return null;
  }
}
