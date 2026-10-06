import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
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
