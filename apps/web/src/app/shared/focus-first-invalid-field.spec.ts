import { Component, ElementRef, Injector, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { focusFirstInvalidFieldAfterRender } from './focus-first-invalid-field';

@Component({
  template: `
    <form>
      <input id="fine" />
      <details id="outer">
        <summary>Outer</summary>
        <details id="inner">
          <summary>Inner</summary>
          <input id="bad" aria-invalid="true" />
        </details>
      </details>
    </form>
  `,
})
class Host {
  readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly injector = inject(Injector);

  focusFirstInvalid(): void {
    focusFirstInvalidFieldAfterRender(this.host, this.injector);
  }
}

@Component({ template: `<form><input id="fine" /></form>` })
class NothingWrong {
  readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly injector = inject(Injector);
}

describe('focusFirstInvalidFieldAfterRender', () => {
  it('focuses the first field marked invalid', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelectorAll('details').forEach((d) => (d.open = true));

    fixture.componentInstance.focusFirstInvalid();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.activeElement?.id).toBe('bad');
  });

  it('opens the folded blocks around a field that is invalid, so a person can reach it', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector<HTMLDetailsElement>('#outer')!.open).toBe(false);

    fixture.componentInstance.focusFirstInvalid();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(root.querySelector<HTMLDetailsElement>('#outer')!.open).toBe(true);
    expect(root.querySelector<HTMLDetailsElement>('#inner')!.open).toBe(true);
    expect(document.activeElement?.id).toBe('bad');
  });

  it('does nothing when no field is invalid', async () => {
    const fixture = TestBed.createComponent(NothingWrong);
    fixture.detectChanges();
    document.body.focus();

    focusFirstInvalidFieldAfterRender(fixture.componentInstance.host, fixture.componentInstance.injector);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.activeElement).toBe(document.body);
  });
});
