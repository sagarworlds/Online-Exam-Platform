import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, beforeEach, vi } from 'vitest';
import { PictureLoader } from './lazy-pictures';
import { MathDirective } from './math.directive';

@Component({ imports: [MathDirective], template: '<div class="t" [appMath]="html()"></div>' })
class Host {
  readonly html = signal<string | null>('<p>Area is $\\pi r^2$</p>');
}

describe('MathDirective', () => {
  it('shows the html with its formulas typeset, and follows changes', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const el = (fixture.nativeElement as HTMLElement).querySelector('.t') as HTMLElement;

    expect(el.querySelector('.katex')).not.toBeNull();

    fixture.componentInstance.html.set('<p>No formula</p>');
    fixture.detectChanges();
    expect(el.querySelector('.katex')).toBeNull();
    expect(el.textContent).toBe('No formula');
  });

  it('still removes script that arrives in the html', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.html.set('<p>Hi<script>alert(1)</script><img src=x onerror=alert(1)></p>');
    fixture.detectChanges();
    const el = (fixture.nativeElement as HTMLElement).querySelector('.t') as HTMLElement;

    expect(el.querySelector('script')).toBeNull();
    expect(el.querySelector('img')?.getAttribute('onerror') ?? null).toBeNull();
  });

  it('shows nothing for no text', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.html.set(null);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.t')?.innerHTML).toBe('');
  });
});

const MARKED = '<p>Which city?</p><img class="lazy-media lazy-media--q-0 lazy-bytes--45000" alt="A map">';

@Component({ imports: [MathDirective], template: '<div class="t" [appMath]="html" [appMathPicture]="loader()"></div>' })
class PictureHost {
  html = MARKED;
  readonly loader = signal<PictureLoader | null>(null);
}

describe('MathDirective with a picture loader (FR-53)', () => {
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:fetched');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => vi.restoreAllMocks());

  const create = (loader: PictureLoader | null) => {
    const fixture = TestBed.createComponent(PictureHost);
    fixture.componentInstance.loader.set(loader);
    fixture.detectChanges();
    return { fixture, el: (fixture.nativeElement as HTMLElement).querySelector('.t') as HTMLElement };
  };

  it('keeps the marker through Angular’s own sanitizer, so the page can offer the picture', () => {
    const { el } = create(() => of(new Blob()));

    const button = el.querySelector('button.lazy-picture');
    expect(button?.textContent).toBe('Show picture: A map (44 KB)');
    expect(el.textContent).toContain('Which city?');
    expect(el.querySelector('img')).toBeNull();
  });

  it('shows the marker as it is when the text carries its pictures, with no loader', () => {
    const { el } = create(null);

    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('img.lazy-media')).not.toBeNull();
  });

  it('fetches the picture by its key when the button is pressed, and shows it', () => {
    const load = vi.fn(() => of(new Blob(['x'], { type: 'image/png' })));
    const { fixture, el } = create(load);

    (el.querySelector('button.lazy-picture') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(load).toHaveBeenCalledWith('q-0');
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('img')?.getAttribute('src')).toBe('blob:fetched');
    expect(el.querySelector('img')?.getAttribute('alt')).toBe('A map');
  });

  it('gives the pictures back to the browser when the page is left', () => {
    const { fixture, el } = create(() => of(new Blob(['x'])));
    (el.querySelector('button.lazy-picture') as HTMLButtonElement).click();

    fixture.destroy();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:fetched');
  });
});
