/** The platform's own primary colour, as tokens.css defines it. Shown in the colour picker while no colour is set; it is not saved. */
export const DEFAULT_PRIMARY_COLOUR = '#1a56db';

/** The colours every branded control is drawn with, all derived from one brand colour (FR-41). */
export interface BrandPalette {
  primary: string;
  onPrimary: string;
  /** A wash of the colour over the page's own surface, so it is pale on a light page and dark on a dark one. Written as CSS, resolved by the page. */
  primarySoft: string;
  /** The colour a hovered control moves to: a little away from the label's colour, so the label reads at least as well on it as on the base colour. */
  primaryHover: string;
  /** The colour for text, links and outlines on a light page: the brand colour, darkened until it reads at 4.5:1. */
  inkLight: string;
  /** The colour for text, links and outlines on a dark page: the brand colour, lightened until it reads at 4.5:1. */
  inkDark: string;
}

/** The page backgrounds tokens.css gives a light and a dark page: the page itself and a card on it. Text must read against both. */
const LIGHT_PAGE = ['#f3f5fa', '#ffffff'];
const DARK_PAGE = ['#0e1320', '#161c2d', '#1b2236'];

/** The share of the brand colour in its wash over a surface, as `primarySoft` uses it, in percent and as a fraction. */
const SOFT_PERCENT = 14;
const SOFT_SHARE = SOFT_PERCENT / 100;

/** The contrast text needs against its background (WCAG 1.4.3). */
const TEXT_CONTRAST = 4.5;

/** The relative luminance of an sRGB colour, as WCAG defines it: 0 for black, 1 for white. */
export function relativeLuminance(hex: string): number {
  const [red, green, blue] = [1, 3, 5].map((offset) => {
    const channel = parseInt(hex.slice(offset, offset + 2), 16) / 255;
    // The sRGB transfer curve: the linear light a screen emits for this channel value.
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });

  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

/**
 * Black or white text, whichever contrasts more with the colour. A brand colour can be light or dark, so the text colour is worked out
 * for each one, not assumed. A label on a button must stay readable whatever colour the institute chose.
 */
export function textColourOn(hex: string): '#000000' | '#ffffff' {
  const luminance = relativeLuminance(hex);
  const againstWhite = 1.05 / (luminance + 0.05);
  const againstBlack = (luminance + 0.05) / 0.05;

  return againstWhite >= againstBlack ? '#ffffff' : '#000000';
}

/** The WCAG contrast ratio between two colours, from 1 (identical) to 21 (black on white). */
export function contrastRatio(first: string, second: string): number {
  const [lighter, darker] = [relativeLuminance(first), relativeLuminance(second)].sort((a, b) => b - a);

  return (lighter + 0.05) / (darker + 0.05);
}

/** Two colours mixed: `share` of the first and the rest of the second. */
function mix(first: string, second: string, share: number): string {
  const channels = [1, 3, 5].map((offset) => {
    const a = parseInt(first.slice(offset, offset + 2), 16);
    const b = parseInt(second.slice(offset, offset + 2), 16);
    return Math.round(a * share + b * (1 - share)).toString(16).padStart(2, '0');
  });

  return `#${channels.join('')}`;
}

/**
 * The colour a hovered control with the brand colour as its fill moves to. It moves away from the label's colour (toward black under a white
 * label, toward white under a black one), whatever the page's colour scheme, so the label's contrast never drops below what the base colour had.
 */
export function hoverOf(colour: string): string {
  return mix(colour, textColourOn(colour) === '#ffffff' ? '#000000' : '#ffffff', 0.85);
}

/**
 * The colour to write text, links and outlines in on a page: the brand colour itself when it already reads at 4.5:1 against the page and
 * against its own wash on it, otherwise the brand colour moved toward black (light page) or white (dark page) a step at a time until it does.
 * A yellow brand colour on a white page is 1.4:1, so without this its links could not be read.
 */
export function inkOn(colour: string, page: string[], toward: '#000000' | '#ffffff'): string {
  const readsEverywhere = (ink: string) =>
    page.every(
      (background) =>
        contrastRatio(ink, background) >= TEXT_CONTRAST && contrastRatio(ink, mix(colour, background, SOFT_SHARE)) >= TEXT_CONTRAST,
    );

  for (let step = 0; step <= 20; step++) {
    const ink = mix(toward, colour, step / 20);
    if (readsEverywhere(ink)) {
      return ink;
    }
  }

  return toward;
}

/** A pale tint of the colour, mixed with white, for the background of a selected item. */
export function tintOf(hex: string, share = 0.12): string {
  const channels = [1, 3, 5].map((offset) => {
    const value = parseInt(hex.slice(offset, offset + 2), 16);
    return Math.round(value * share + 255 * (1 - share)).toString(16).padStart(2, '0');
  });

  return `#${channels.join('')}`;
}

/** The palette for a brand colour, or null when the institute has set none, so the platform's default look stays. */
export function paletteFor(colour: string | null): BrandPalette | null {
  if (colour === null) {
    return null;
  }

  return {
    primary: colour,
    onPrimary: textColourOn(colour),
    primarySoft: `color-mix(in srgb, ${colour} ${SOFT_PERCENT}%, var(--surface))`,
    primaryHover: hoverOf(colour),
    inkLight: inkOn(colour, LIGHT_PAGE, '#000000'),
    inkDark: inkOn(colour, DARK_PAGE, '#ffffff'),
  };
}

/**
 * Writes a palette onto the page's root element as custom properties (FR-41). The tokens in tokens.css read these and fall back to the
 * platform's own colours, so with no palette the properties are removed and the default look is unchanged. The exam page's high-contrast
 * palette sets its own colours, so it is not affected.
 */
export function applyBrandPalette(root: HTMLElement, palette: BrandPalette | null): void {
  if (palette === null) {
    root.style.removeProperty('--brand-primary');
    root.style.removeProperty('--brand-on-primary');
    root.style.removeProperty('--brand-primary-soft');
    root.style.removeProperty('--brand-primary-hover');
    root.style.removeProperty('--brand-ink-light');
    root.style.removeProperty('--brand-ink-dark');
    return;
  }

  root.style.setProperty('--brand-primary', palette.primary);
  root.style.setProperty('--brand-on-primary', palette.onPrimary);
  root.style.setProperty('--brand-primary-soft', palette.primarySoft);
  root.style.setProperty('--brand-primary-hover', palette.primaryHover);
  root.style.setProperty('--brand-ink-light', palette.inkLight);
  root.style.setProperty('--brand-ink-dark', palette.inkDark);
}
