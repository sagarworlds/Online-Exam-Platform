import { applyBrandPalette, paletteFor, relativeLuminance, textColourOn, tintOf } from './branding-theme';
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
      primarySoft: tintOf('#1A56DB'),
    });
  });

  it('writes the palette onto the root element, and removes it again when the colour is cleared', () => {
    const { root, values } = recordingRoot();

    applyBrandPalette(root, paletteFor('#1A56DB'));
    expect(values.get('--brand-primary')).toBe('#1A56DB');
    expect(values.get('--brand-on-primary')).toBe('#ffffff');
    expect(values.has('--brand-primary-soft')).toBe(true);

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
