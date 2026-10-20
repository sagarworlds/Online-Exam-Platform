import { AfterViewInit, Component, ElementRef, Renderer2, inject, input, output, viewChild } from '@angular/core';

let nextId = 0;

/**
 * A modal popup: a title bar with a Close button, and whatever is placed inside it. It is the browser's own `<dialog>` opened with
 * `showModal()`, so the page behind it cannot be reached by keyboard or screen reader, Escape closes it, and focus goes back to what
 * opened it. The page shows it with `@if` and takes it away again when {@link closed} is emitted; that is the only way it closes.
 *
 * Escape, the Close button and a click on the dimmed area all end up in the dialog's own `close` event, so there is one way out.
 */
@Component({
  selector: 'app-modal-dialog',
  templateUrl: './modal-dialog.html',
  styleUrl: './modal-dialog.css',
})
export class ModalDialog implements AfterViewInit {
  /** The title, read out when the dialog opens and shown at the top. */
  readonly heading = input.required<string>();

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

  /** Closes the dialog; {@link closed} follows from its `close` event. */
  protected close(): void {
    const element = this.dialog().nativeElement;
    if (typeof element.close === 'function') {
      element.close();
    } else {
      element.removeAttribute('open');
      this.closed.emit();
    }
  }

  /** The dimmed area around the panel is the dialog element itself, so a click whose target is the dialog landed outside the panel. */
  protected onBackdropClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close();
    }
  }

  protected onClosed(): void {
    this.closed.emit();
  }
}
