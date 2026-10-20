import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ModalDialog } from './modal-dialog';

@Component({
  imports: [ModalDialog],
  template: `
    @if (shown) {
      <app-modal-dialog heading="Edit profile" (closed)="onClosed()">
        <p class="inside">Hello</p>
      </app-modal-dialog>
    }
  `,
})
class Host {
  shown = true;
  closedCount = 0;

  onClosed(): void {
    this.closedCount++;
    this.shown = false;
  }
}

describe('ModalDialog', () => {
  function render() {
    TestBed.configureTestingModule({ imports: [Host] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, dialog: () => root.querySelector<HTMLDialogElement>('dialog') };
  }

  it('shows its title and what is placed inside it, in a dialog named by that title', () => {
    const { root, dialog } = render();

    const title = root.querySelector('.modal__title')!;
    expect(title.textContent?.trim()).toBe('Edit profile');
    expect(dialog()!.getAttribute('aria-labelledby')).toBe(title.id);
    expect(root.querySelector('.modal__body .inside')?.textContent).toBe('Hello');
  });

  it('is open as soon as it is shown', () => {
    const { dialog } = render();

    expect(dialog()!.hasAttribute('open')).toBe(true);
  });

  it('has a Close button that is named for assistive technology', () => {
    const { root } = render();

    expect(root.querySelector('.modal__close')?.getAttribute('aria-label')).toBe('Close');
  });

  it('says it closed, once, when the Close button is pressed', () => {
    const { fixture, root } = render();

    root.querySelector<HTMLButtonElement>('.modal__close')!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.closedCount).toBe(1);
    expect(root.querySelector('dialog')).toBeNull();
  });

  it('closes when the dimmed area around the panel is clicked, but not when the panel is', () => {
    const { fixture, root, dialog } = render();

    root.querySelector<HTMLElement>('.modal__panel')!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.closedCount).toBe(0);

    dialog()!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.closedCount).toBe(1);
  });

  it('closes when the browser closes the dialog itself, as Escape does', () => {
    const { fixture, dialog } = render();

    dialog()!.dispatchEvent(new Event('close'));
    fixture.detectChanges();

    expect(fixture.componentInstance.closedCount).toBe(1);
  });
});
