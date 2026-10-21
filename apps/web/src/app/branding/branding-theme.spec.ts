import { applyBrandPalette, contrastRatio, inkOn, paletteFor, relativeLuminance, textColourOn, tintOf } from './branding-theme';
import { MAX_LOGO_BYTES, isHexColour, logoProblem } from './branding.models';

/** A root element that records the custom properties written onto it, without relying on the DOM's own handling of them. */
function recordingRoot(): { root: HTMLElement; values: Map<string, string> } {
  const values = new Map<string, string>();
  const style = {
    setProperty: (name: string, value: string) => values.set(name, value),
    removeProperty: (name: string) => values.delete(name),
  };

  return { root: { style } as unknown as HTMLElement, values };
}

describe('branding theme', () => {
  it('measures relative luminance with white at 1 and black at 0', () => {
    expect(relativeLuminance('#ffffff')).toBeCloseTo(1, 5);
    expect(relativeLuminance('#000000')).toBeCloseTo(0, 5);
  });

  it('picks white text on a dark colour and black text on a light one', () => {
    expect(textColourOn('#1a56db')).toBe('#ffffff');
    expect(textColourOn('#000000')).toBe('#ffffff');
    expect(textColourOn('#ffffff')).toBe('#000000');
    expect(textColourOn('#ffe600')).toBe('#000000');
  });

  it('mixes a pale tint of the colour with white', () => {
    expect(tintOf('#000000', 0.12)).toBe('#e0e0e0');
    expect(tintOf('#ffffff', 0.12)).toBe('#ffffff');
  });

  it('has no palette when the institute set no colour, so the default look stays', () => {
    expect(paletteFor(null)).toBeNull();
  });

  it('derives the whole palette from the one colour the institute chose', () => {
    expect(paletteFor('#1A56DB')).toEqual({
      primary: '#1A56DB',
      onPrimary: '#ffffff',
      // A wash over the page's own surface and a step toward its text, so both follow the light or dark scheme.
      primarySoft: 'color-mix(in srgb, #1A56DB 14%, var(--surface))',
      primaryHover: 'color-mix(in srgb, #1A56DB 85%, var(--text))',
      // The platform blue already reads on a light page; on a dark one it is lightened.
      inkLight: '#1a56db',
      inkDark: expect.stringMatching(/^#[0-9a-f]{6}$/),
    });
  });

  it('measures contrast from 1 to 21', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
  });

  describe('the colour used for text and outlines (WCAG 1.4.3)', () => {
    const lightPage = ['#f3f5fa', '#ffffff'];
    const darkPage = ['#0e1320', '#161c2d'];

    it.each(['#ffe600', '#1a56db', '#ff8800', '#00a86b', '#000000', '#ffffff', '#0b1226', '#7f7fff'])(
      'reads at 4.5:1 on a light and on a dark page, whatever colour %s the institute chose',
      (colour) => {
        const palette = paletteFor(colour)!;

        for (const background of lightPage) {
          expect(contrastRatio(palette.inkLight, background)).toBeGreaterThanOrEqual(4.5);
        }
        for (const background of darkPage) {
          expect(contrastRatio(palette.inkDark, background)).toBeGreaterThanOrEqual(4.5);
        }
      },
    );

    it('keeps the institute colour as it is when it already reads, and moves a yellow toward black on a light page', () => {
      expect(inkOn('#1a56db', lightPage, '#000000')).toBe('#1a56db');
      const yellow = inkOn('#ffe600', lightPage, '#000000');

      expect(yellow).not.toBe('#ffe600');
      expect(relativeLuminance(yellow)).toBeLessThan(relativeLuminance('#ffe600'));
    });
  });

  it('writes the palette onto the root element, and removes it again when the colour is cleared', () => {
    const { root, values } = recordingRoot();

    applyBrandPalette(root, paletteFor('#1A56DB'));
    expect(values.get('--brand-primary')).toBe('#1A56DB');
    expect(values.get('--brand-on-primary')).toBe('#ffffff');
    expect(values.has('--brand-primary-soft')).toBe(true);
    expect(values.has('--brand-primary-hover')).toBe(true);
    expect(values.has('--brand-ink-light')).toBe(true);
    expect(values.has('--brand-ink-dark')).toBe(true);

    applyBrandPalette(root, null);
    expect(values.size).toBe(0);
  });
});

describe('branding input rules', () => {
  it('accepts only six-digit hex colours, with or without surrounding spaces', () => {
    expect(isHexColour('#1A56DB')).toBe(true);
    expect(isHexColour('  #1a56db  ')).toBe(true);
    expect(isHexColour('#1AD')).toBe(false);
    expect(isHexColour('red')).toBe(false);
    expect(isHexColour('1A56DB')).toBe(false);
  });

  it('refuses a logo that is not PNG, JPEG or WebP, and one over the size limit', () => {
    expect(logoProblem({ type: 'image/png', size: 1000 })).toBeNull();
    expect(logoProblem({ type: 'image/webp', size: 1000 })).toBeNull();
    expect(logoProblem({ type: 'image/gif', size: 1000 })).toBe('type');
    expect(logoProblem({ type: 'image/svg+xml', size: 1000 })).toBe('type');
    expect(logoProblem({ type: 'image/png', size: MAX_LOGO_BYTES + 1 })).toBe('size');
    expect(logoProblem({ type: 'image/png', size: MAX_LOGO_BYTES })).toBeNull();
  });
});
