import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { MathTextDirective } from './math-text.directive';

@Component({
  imports: [MathTextDirective],
  template: '<span id="shown" [appMathText]="text()"></span>',
})
class Host {
  readonly text = signal<string | null>('Take $x^2$ away');
}

describe('MathTextDirective', () => {
  function show(text: string | null) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.text.set(text);
    fixture.detectChanges();
    return { fixture, shown: fixture.nativeElement.querySelector('#shown') as HTMLElement };
  }

  it('renders a formula in plain text and keeps the words around it', () => {
    const { shown } = show('Take $x^2$ away');

    expect(shown.querySelector('.katex')).not.toBeNull();
    expect(shown.textContent).toContain('Take ');
    expect(shown.textContent).toContain(' away');
    expect(shown.textContent).not.toContain('$');
  });

  it.each([
    ['inline brackets', 'Take \\(x^2\\) away', false],
    ['display brackets', 'Take \\[x^2\\] away', true],
    ['display dollars', 'Take $$x^2$$ away', true],
  ])('renders %s', (_name, text, display) => {
    const { shown } = show(text);

    expect(shown.querySelector('.katex')).not.toBeNull();
    expect(shown.querySelector('.katex-display') !== null).toBe(display);
    expect(shown.textContent).not.toContain('\\(');
    expect(shown.textContent).not.toContain('\\[');
  });

  it('shows text with no formula exactly as written', () => {
    const { shown } = show('It costs $5 or $ 6 today, and 3 < 4');

    expect(shown.querySelector('.katex')).toBeNull();
    expect(shown.textContent).toBe('It costs $5 or $ 6 today, and 3 < 4');
  });

  it('never reads the text as markup, even around a formula', () => {
    const { shown } = show('<img src=x onerror=alert(1)> and $x$ <b>bold</b>');

    expect(shown.querySelector('img')).toBeNull();
    expect(shown.querySelector('b')).toBeNull();
    expect(shown.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(shown.querySelector('.katex')).not.toBeNull();
  });

  it('leaves a formula it cannot parse exactly as typed', () => {
    const { shown } = show('Broken $\\frac{1$ here');

    expect(shown.querySelector('.katex')).toBeNull();
    expect(shown.textContent).toBe('Broken $\\frac{1$ here');
  });

  it('shows nothing for a missing text, and follows the text when it changes', () => {
    const { fixture, shown } = show(null);
    expect(shown.textContent).toBe('');

    fixture.componentInstance.text.set('Now $y$');
    fixture.detectChanges();

    expect(shown.querySelector('.katex')).not.toBeNull();
    expect(shown.textContent).toContain('Now ');
  });
});
