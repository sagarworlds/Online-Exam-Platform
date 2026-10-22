import { AfterViewInit, Component, ElementRef, Renderer2, booleanAttribute, inject, input, output, viewChild } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';

let nextId = 0;

/**
 * A modal popup: a title bar with a Close button, and whatever is placed inside it. It is the browser's own `<dialog>` opened with
 * `showModal()`, so the page behind it cannot be reached by keyboard or screen reader, Escape closes it, and focus goes back to what
 * opened it. The page shows it with `@if` and takes it away again when {@link closed} is emitted; that is the only way it closes.
 *
 * Escape, the Close button and a click on the dimmed area all end up in the dialog's own `close` event, so there is one way out.
 *
 * It can also be a sheet down the right-hand edge ({@link side}), for a form long enough to need the height, and it can hold buttons in a
 * footer that stays in view while the rest scrolls: anything placed in it with the `modalFooter` attribute. A dialog with work in it asks
 * before it closes ({@link guarded}): the three ways out then only ask, with {@link closeRequested}, and the page decides.
 */
@Component({
  selector: 'app-modal-dialog',
  imports: [TranslatePipe],
  templateUrl: './modal-dialog.html',
  styleUrl: './modal-dialog.css',
})
export class ModalDialog implements AfterViewInit {
  /** The title, read out when the dialog opens and shown at the top. */
  readonly heading = input.required<string>();

  /** Shows the dialog as a full-height sheet down the right-hand edge instead of a box in the middle. */
  readonly side = input(false, { transform: booleanAttribute });

  /** Shows the dialog as a sheet up from the bottom edge, as wide as the screen: for a short list a thumb can reach on a phone. */
  readonly bottom = input(false, { transform: booleanAttribute });

  /**
   * Whether closing needs the page's say-so, because something would be lost. While it is true, Escape, the Close button and a click on the
   * dimmed area do not close the dialog; they emit {@link closeRequested} and the page chooses, calling {@link close} to let it go.
   */
  readonly guarded = input(false);

  /** Emitted, while {@link guarded}, when the author tried to close the dialog by Escape, its Close button or a click outside it. */
  readonly closeRequested = output<void>();

  /** Emitted once the dialog has closed, however it was closed. */
  readonly closed = output<void>();

  protected readonly titleId = `modal-title-${nextId++}`;

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly renderer = inject(Renderer2);

  ngAfterViewInit(): void {
    const element = this.dialog().nativeElement;
    // Listened for here and not in the template: a click on the dimmed area is a convenience for a mouse, and the keyboard's ways out
    // (Escape and the Close button) are already there, so the dialog element itself is not made focusable for it.
    this.renderer.listen(element, 'click', (event: MouseEvent) => this.onBackdropClick(event));
    if (typeof element.showModal === 'function') {
      element.showModal();
    } else {
      // A browser without <dialog> support: shown in place, and still closed by the Close button.
      element.setAttribute('open', '');
    }
  }

  /** Closes the dialog whatever {@link guarded} says, so a page that has decided to let it go can; {@link closed} follows from its `close` event. */
  close(): void {
    const element = this.dialog().nativeElement;
    if (typeof element.close === 'function') {
      element.close();
    } else {
      element.removeAttribute('open');
      this.closed.emit();
    }
  }

  /** The Close button and a click outside: closes the dialog, or only asks to, while it is {@link guarded}. */
  protected requestClose(): void {
    if (this.guarded()) {
      this.closeRequested.emit();
    } else {
      this.close();
    }
  }

  /** Escape. The browser closes the dialog after this unless it is told not to, which it is while the dialog is {@link guarded}. */
  protected onCancel(event: Event): void {
    if (this.guarded()) {
      event.preventDefault();
      this.closeRequested.emit();
    }
  }

  /** The dimmed area around the panel is the dialog element itself, so a click whose target is the dialog landed outside the panel. */
  protected onBackdropClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.requestClose();
    }
  }

  protected onClosed(): void {
    this.closed.emit();
  }
}
