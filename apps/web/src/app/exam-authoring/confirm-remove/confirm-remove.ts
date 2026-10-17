import { Component, ElementRef, HostListener, Injector, afterNextRender, inject, input, output, signal, viewChild } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';

/**
 * A trash icon that removes something only after the author has said yes. Pressing the icon puts a question and two buttons in its place
 * (Remove, and Cancel, which has focus so that Enter cannot remove by accident); nothing is sent until Remove is pressed. It only asks:
 * the page that owns the thing being removed makes the request, so this never needs to know how it is stored.
 *
 * Used for a section, a question in a section, and a draw rule, so all three are removed the same way.
 */
@Component({
  selector: 'app-confirm-remove',
  imports: [TranslatePipe],
  templateUrl: './confirm-remove.html',
  styleUrl: './confirm-remove.css',
})
export class ConfirmRemove {
  private readonly injector = inject(Injector);

  /** What the icon is called for a screen reader, and what the confirmation is about, e.g. "Remove Algebra from the exam". */
  readonly label = input.required<string>();
  /** The question put to the author once the icon is pressed, which also says what happens to what is inside. */
  readonly prompt = input.required<string>();
  /** The words on the button that confirms. */
  readonly confirmLabel = input.required<string>();
  /** True while a request about the exam is running, so nothing can be sent twice. */
  readonly busy = input(false);

  /** The author confirmed. */
  readonly confirmed = output<void>();

  protected readonly confirming = signal(false);

  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');

  /** Escape takes the question back wherever focus is inside it, as it does for the tip on the question bank. */
  @HostListener('keydown.escape')
  protected onEscape(): void {
    if (this.confirming()) {
      this.cancel();
    }
  }

  /** Asks the question and moves focus to Cancel, the safe answer: a button that has just been pressed cannot be pressed again by key repeat. */
  protected ask(): void {
    this.confirming.set(true);
    afterNextRender(() => this.cancelButton()?.nativeElement.focus(), { injector: this.injector });
  }

  /** Puts the question away without removing anything, and gives focus back to the icon that opened it. */
  protected cancel(): void {
    this.confirming.set(false);
    this.focusTriggerOnceShown();
  }

  protected confirm(): void {
    if (this.busy()) {
      return;
    }

    this.confirming.set(false);
    this.confirmed.emit();
    // When the removal worked this element is gone with what it removed, and there is nothing to focus; when it was refused the icon is
    // still here and is where the author was.
    this.focusTriggerOnceShown();
  }

  private focusTriggerOnceShown(): void {
    afterNextRender(() => this.trigger()?.nativeElement.focus(), { injector: this.injector });
  }
}
