import { Component, ElementRef, HostListener, computed, inject, signal } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { QuestionApiService } from '../question-api.service';
import { ContentEncryptionStatus } from '../question.models';

/** What the padlock shows: still reading, could not read, every value encrypted, or some still plain text. */
type EncryptionState = 'loading' | 'error' | 'complete' | 'incomplete';

/**
 * The admin's view of whether the bank's question content is encrypted at rest (#57), as a padlock beside the page's heading. The details
 * (how many stored values there are and how many are still plain text, so an administrator can see that the backfill finished before
 * questions are used) are in a tip that opens when a mouse is over the padlock, when it has keyboard focus, or when it is tapped, and that
 * Escape puts away. The padlock itself shows the state without opening anything: a tick when everything is encrypted, an exclamation mark
 * in red when it is not, so a problem is never hidden behind a hover.
 *
 * When the status could not be read, selecting the padlock reads it again.
 */
@Component({
  selector: 'app-content-encryption-status',
  imports: [TranslatePipe],
  templateUrl: './content-encryption-status.html',
  styleUrl: './content-encryption-status.css',
})
export class ContentEncryptionStatusPanel {
  private readonly api = inject(QuestionApiService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly status = signal<ContentEncryptionStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  /** A mouse is over the padlock or its tip; a finger or a pen never counts, since it has no "over". */
  private readonly hovering = signal(false);
  /** The padlock has keyboard focus. A tap or a click focuses a button too, so only focus the keyboard gave counts. */
  private readonly focused = signal(false);
  /** The tip was opened on purpose, by a tap, a click or Enter, and stays until it is closed the same way, or the reader goes elsewhere. */
  private readonly pinned = signal(false);
  /** Escape was pressed: the tip stays away until the pointer or the focus leaves and returns. */
  private readonly dismissed = signal(false);

  protected readonly state = computed<EncryptionState>(() => {
    if (this.loading()) {
      return 'loading';
    }
    if (this.errorMessage()) {
      return 'error';
    }
    return this.status()?.complete ? 'complete' : 'incomplete';
  });

  protected readonly visible = computed(() => !this.dismissed() && (this.hovering() || this.focused() || this.pinned()));

  constructor() {
    this.load();
  }

  @HostListener('pointerenter', ['$event'])
  protected pointerEnter(event: PointerEvent): void {
    if (event.pointerType === 'mouse') {
      this.hovering.set(true);
    }
  }

  @HostListener('pointerleave', ['$event'])
  protected pointerLeave(event: PointerEvent): void {
    if (event.pointerType === 'mouse') {
      this.hovering.set(false);
      this.dismissed.set(false);
    }
  }

  protected focusIn(event: FocusEvent): void {
    if (isKeyboardFocus(event.target)) {
      this.focused.set(true);
    }
  }

  protected focusOut(): void {
    this.focused.set(false);
    this.pinned.set(false);
    this.dismissed.set(false);
  }

  /** Opens or closes the tip on purpose; when the status could not be read, reads it again instead. */
  protected activate(): void {
    if (this.state() === 'error') {
      this.load();
      return;
    }

    this.dismissed.set(false);
    this.pinned.update((pinned) => !pinned);
  }

  /** Puts the tip away until the pointer or the focus leaves and comes back. Escape works wherever the focus is, as the tip may be open for the mouse. */
  @HostListener('document:keydown.escape')
  protected dismiss(): void {
    this.dismissed.set(true);
    this.pinned.set(false);
  }

  /** A tap or a click anywhere else closes a tip that was opened on purpose, which a touch screen has no pointer-leave to do. */
  @HostListener('document:click', ['$event'])
  protected clickAway(event: Event): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.pinned.set(false);
    }
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.api.encryptionStatus().subscribe({
      next: (status) => {
        this.status.set(status);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}

/**
 * Whether focus reached the element by the keyboard (the browser's own `:focus-visible`), not by a click or a tap. A browser that cannot
 * tell is given the benefit of the doubt, so keyboard users still see the tip.
 */
function isKeyboardFocus(target: EventTarget | null): boolean {
  try {
    return (target as Element | null)?.matches(':focus-visible') ?? true;
  } catch {
    return true;
  }
}
