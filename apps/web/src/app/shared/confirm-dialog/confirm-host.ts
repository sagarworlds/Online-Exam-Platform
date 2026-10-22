import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, viewChild } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ModalDialog } from '../modal-dialog/modal-dialog';
import { ConfirmService } from './confirm.service';

/**
 * Draws the question `ConfirmService` is asking, in a modal dialog: a title, what it means, what it will change, and two buttons. The page
 * around the app holds it once (app.html).
 *
 * The focus goes where a stray key does least harm: to Cancel when the answer cannot be undone, to the confirm button when it can. The
 * dialog is the browser's own, so the page behind it cannot be reached, Escape is "no", and the focus goes back to what opened it.
 */
@Component({
  selector: 'app-confirm-host',
  imports: [ModalDialog, TranslatePipe],
  templateUrl: './confirm-host.html',
  styleUrl: './confirm-host.css',
})
export class ConfirmHost {
  protected readonly service = inject(ConfirmService);
  private readonly injector = inject(Injector);

  /**
   * The question on screen as a list of one, so the template draws it with `@for ... track` and a new question is a new dialog. A plain `@if`
   * would keep the same dialog when one question follows another in the same tick, and a dialog that has closed is not opened again.
   */
  protected readonly shown = computed(() => {
    const pending = this.service.current();
    return pending === null ? [] : [pending];
  });

  private readonly dialog = viewChild(ModalDialog);
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');
  private readonly confirmButton = viewChild<ElementRef<HTMLButtonElement>>('confirmButton');

  /** What the person chose, kept until the dialog has closed, since every way of closing it ends in the same event. */
  private answer = false;

  constructor() {
    // Each new question puts the focus on its safe button once it is drawn.
    effect(() => {
      const pending = this.service.current();
      if (pending !== null) {
        const dangerous = pending.options.danger === true;
        afterNextRender(() => (dangerous ? this.cancelButton() : this.confirmButton())?.nativeElement.focus(), { injector: this.injector });
      }
    });
  }

  protected decide(confirmed: boolean): void {
    this.answer = confirmed;
    this.dialog()?.close();
  }

  /** The dialog has closed, by a button, by Escape or by a click outside: anything but the confirm button is "no". */
  protected onClosed(): void {
    const confirmed = this.answer;
    this.answer = false;
    this.service.answer(confirmed);
  }
}
