/** The platform's own primary colour, as tokens.css defines it. Shown in the colour picker while no colour is set; it is not saved. */
export const DEFAULT_PRIMARY_COLOUR = '#1a56db';

/** The colours every branded control is drawn with, all derived from one brand colour (FR-41). */
export interface BrandPalette {
  primary: string;
  onPrimary: string;
  primarySoft: string;
}

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

  return { primary: colour, onPrimary: textColourOn(colour), primarySoft: tintOf(colour) };
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
    return;
  }

  root.style.setProperty('--brand-primary', palette.primary);
  root.style.setProperty('--brand-on-primary', palette.onPrimary);
  root.style.setProperty('--brand-primary-soft', palette.primarySoft);
}
