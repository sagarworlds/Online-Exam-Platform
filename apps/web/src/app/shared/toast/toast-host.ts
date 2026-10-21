import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { Toast, ToastService } from './toast.service';

/**
 * Draws the messages `ToastService` is given, in one place at the bottom of the screen, above everything but a dialog. The page around the
 * app holds it once (app.html).
 *
 * It holds two regions that are always in the page, because a screen reader only announces what is put into a live region that is already
 * there: an assertive one for errors, which are read at once, and a polite one for everything else, which waits its turn. They are plain
 * `aria-live` regions, not `role="alert"` and `role="status"`, which mean the same to a screen reader but would make every page carry two
 * empty alerts and statuses of its own for a test, or a person looking for "the alert on this page", to trip over. A message is a plain
 * block inside the one for its kind.
 */
@Component({
  selector: 'app-toast-host',
  imports: [NgTemplateOutlet, TranslatePipe],
  templateUrl: './toast-host.html',
  styleUrl: './toast-host.css',
})
export class ToastHost {
  protected readonly service = inject(ToastService);

  protected readonly errors = computed(() => this.service.toasts().filter((toast) => toast.kind === 'error'));
  protected readonly others = computed(() => this.service.toasts().filter((toast) => toast.kind !== 'error'));

  /** The mark in front of a message: a shape as well as a colour, and the words say the rest. */
  protected mark(toast: Toast): string {
    return { success: '✓', error: '✕', warning: '!', info: 'i' }[toast.kind];
  }

  protected act(toast: Toast): void {
    toast.action?.run();
    this.service.dismiss(toast.id);
  }
}
