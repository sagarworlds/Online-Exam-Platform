import { Component, signal, viewChild } from '@angular/core';
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

/** A sheet down the side, with buttons in its footer, that asks before it closes while there is work in it. */
@Component({
  imports: [ModalDialog],
  template: `
    @if (shown) {
      <app-modal-dialog heading="New question" side [guarded]="guarded()" (closeRequested)="requested = requested + 1" (closed)="onClosed()">
        <p class="inside">Fields</p>
        <button modalFooter type="button" class="in-footer">Save</button>
      </app-modal-dialog>
    }
  `,
})
class SheetHost {
  readonly dialog = viewChild(ModalDialog);
  shown = true;
  // A signal, so changing it marks the host to be checked: the test fixture does not run change detection over a view nothing dirtied.
  readonly guarded = signal(true);
  requested = 0;
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

  it('is a box in the middle, with nothing in its footer, unless it is asked to be otherwise', () => {
    const { root, dialog } = render();

    expect(dialog()!.classList.contains('modal--side')).toBe(false);
    // Empty, so the footer's own rule takes it out of the layout.
    expect(root.querySelector('.modal__footer')!.childNodes).toHaveLength(0);
  });

  describe('as a sheet down the side, with a footer and a guard', () => {
    function renderSheet() {
      TestBed.configureTestingModule({ imports: [SheetHost] });
      const fixture = TestBed.createComponent(SheetHost);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      return { fixture, root, host: fixture.componentInstance, dialog: () => root.querySelector<HTMLDialogElement>('dialog') };
    }

    it('is drawn down the side', () => {
      const { dialog } = renderSheet();

      expect(dialog()!.classList.contains('modal--side')).toBe(true);
    });

    it('puts what is marked as its footer in the footer, apart from the body that scrolls', () => {
      const { root } = renderSheet();

      expect(root.querySelector('.modal__footer .in-footer')?.textContent).toBe('Save');
      expect(root.querySelector('.modal__body .in-footer')).toBeNull();
      expect(root.querySelector('.modal__body .inside')?.textContent).toBe('Fields');
    });

    it('only asks to close when the Close button is pressed, while it is guarded', () => {
      const { fixture, root, host } = renderSheet();

      root.querySelector<HTMLButtonElement>('.modal__close')!.click();
      fixture.detectChanges();

      expect(host.requested).toBe(1);
      expect(host.closedCount).toBe(0);
      expect(root.querySelector('dialog')).not.toBeNull();
    });

    it('only asks to close when the dimmed area is clicked, while it is guarded', () => {
      const { fixture, host, dialog } = renderSheet();

      dialog()!.click();
      fixture.detectChanges();

      expect(host.requested).toBe(1);
      expect(host.closedCount).toBe(0);
    });

    it('keeps Escape from closing it, and asks instead, while it is guarded', () => {
      const { fixture, host, dialog } = renderSheet();
      const escape = new Event('cancel', { cancelable: true });

      dialog()!.dispatchEvent(escape);
      fixture.detectChanges();

      expect(escape.defaultPrevented).toBe(true);
      expect(host.requested).toBe(1);
    });

    it('lets all three ways out through once it is no longer guarded', () => {
      const { fixture, root, host, dialog } = renderSheet();
      host.guarded.set(false);
      fixture.detectChanges();
      const escape = new Event('cancel', { cancelable: true });

      dialog()!.dispatchEvent(escape);
      expect(escape.defaultPrevented).toBe(false);

      root.querySelector<HTMLButtonElement>('.modal__close')!.click();
      fixture.detectChanges();

      expect(host.requested).toBe(0);
      expect(host.closedCount).toBe(1);
    });

    it('can be closed by the page whatever the guard says, once the page has decided', () => {
      const { fixture, host } = renderSheet();

      host.dialog()!.close();
      fixture.detectChanges();

      expect(host.requested).toBe(0);
      expect(host.closedCount).toBe(1);
    });
  });
});
