import { Component, DestroyRef, ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';
import { extractErrorMessage } from '../../shared/problem-details';
import { ToastService } from '../../shared/toast/toast.service';
import { OtpCodesApiService } from './otp-codes-api.service';
import { OutstandingOtp } from './otp-codes.models';
import { LocaleDatePipe } from '../../i18n/locale-date.pipe';

/** How long a revealed code stays on screen before it is hidden again: long enough to read out, not long enough to be left up. */
export const REVEAL_MS = 60_000;

/** The words for each purpose and channel, as message keys, so a card reads as a sentence in the language the staff member chose. */
const PURPOSE_KEYS: Readonly<Record<OutstandingOtp['purpose'], MessageKey>> = {
  Login: 'admin.codes.purpose.Login',
  Registration: 'admin.codes.purpose.Registration',
};

const CHANNEL_KEYS: Readonly<Record<OutstandingOtp['channel'], MessageKey>> = {
  Email: 'admin.codes.channel.Email',
  Sms: 'admin.codes.channel.Sms',
};

/**
 * Admin page: the sign-in and registration codes candidates are waiting for, so support can read one out to a candidate whose email or
 * SMS never arrived. A search is needed before anything is looked up, and a code stays hidden until someone asks to see it, so a code is
 * never on screen for someone who was not looking for it. A revealed code hides again when the search changes, or after a minute, and says
 * so when it does it by itself. It can be copied, so support need not read it off the screen. The API records every search.
 */
@Component({
  selector: 'app-otp-codes',
  imports: [LocaleDatePipe, TranslatePipe],
  templateUrl: './otp-codes.html',
})
export class OtpCodes {
  private readonly api = inject(OtpCodesApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly clipboard = inject(ClipboardService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);

  protected readonly purposeKeys = PURPOSE_KEYS;
  protected readonly channelKeys = CHANNEL_KEYS;

  protected readonly search = signal('');
  protected readonly codes = signal<OutstandingOtp[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether Find was pressed with the search box empty. Nothing is looked up then. */
  protected readonly needsSearch = signal(false);
  /** The challenge ids of the codes currently on screen. */
  protected readonly revealed = signal<ReadonlySet<string>>(new Set());
  private readonly hideTimers = new Map<string, ReturnType<typeof setTimeout>>();

  constructor() {
    this.destroyRef.onDestroy(() => this.clearTimers());
  }

  protected find(): void {
    if (this.loading()) {
      return;
    }

    // A new search hides whatever was on screen from the last one.
    this.hideAll();
    this.errorMessage.set(null);
    const destination = this.search().trim();
    if (destination === '') {
      this.needsSearch.set(true);
      this.codes.set(null);
      return;
    }

    this.needsSearch.set(false);
    this.loading.set(true);
    this.api.list(destination).subscribe({
      next: (codes) => {
        this.codes.set(codes);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.codes.set(null);
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected isRevealed(challengeId: string): boolean {
    return this.revealed().has(challengeId);
  }

  /** Shows one code, and hides it again after {@link REVEAL_MS} if nobody has hidden it first. */
  protected reveal(challengeId: string): void {
    const hadFocus = this.focusIsIn(challengeId);
    this.revealed.update((ids) => new Set(ids).add(challengeId));
    this.keepFocusIn(challengeId, hadFocus);
    this.clearTimer(challengeId);
    this.hideTimers.set(
      challengeId,
      setTimeout(() => {
        this.hide(challengeId);
        // A code that vanishes by itself is said, in a message a screen reader hears as well: whoever was reading it out is not left looking for it.
        const destination = this.codes()?.find((item) => item.challengeId === challengeId)?.destination;
        if (destination !== undefined) {
          this.toast.info(this.i18n.t('admin.codes.autoHidden', { destination }));
        }
      }, REVEAL_MS),
    );
  }

  /** Puts the code on the clipboard, and says whether that worked. The code stays on screen to be selected when the browser refuses. */
  protected async copy(item: OutstandingOtp): Promise<void> {
    if (await this.clipboard.copy(item.code)) {
      this.toast.success(this.i18n.t('admin.codes.copied'));
    } else {
      this.toast.error(this.i18n.t('admin.codes.copyRefused'));
    }
  }

  protected hide(challengeId: string): void {
    const hadFocus = this.focusIsIn(challengeId);
    this.clearTimer(challengeId);
    this.revealed.update((ids) => {
      const next = new Set(ids);
      next.delete(challengeId);
      return next;
    });
    this.keepFocusIn(challengeId, hadFocus);
  }

  private card(challengeId: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[data-challenge="${challengeId}"]`);
  }

  private focusIsIn(challengeId: string): boolean {
    return this.card(challengeId)?.contains(document.activeElement) ?? false;
  }

  /**
   * Showing or hiding a code swaps the card's buttons, which takes away the one that had focus and would leave it on the top of the page. It goes
   * to the card's first button, which is the one that now does the opposite: Hide and Copy when shown, Show when hidden.
   */
  private keepFocusIn(challengeId: string, hadFocus: boolean): void {
    if (!hadFocus) {
      return;
    }
    afterNextRender(() => this.card(challengeId)?.querySelector<HTMLButtonElement>('button')?.focus(), { injector: this.injector });
  }

  /** The code as two groups of three digits, so it can be read out in two pieces. A code of any other shape is shown as it was sent. */
  protected groupedCode(code: string): string {
    return /^\d{6}$/.test(code) ? `${code.slice(0, 3)} ${code.slice(3)}` : code;
  }

  private hideAll(): void {
    this.clearTimers();
    this.revealed.set(new Set());
  }

  private clearTimer(challengeId: string): void {
    const timer = this.hideTimers.get(challengeId);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.hideTimers.delete(challengeId);
    }
  }

  private clearTimers(): void {
    for (const timer of this.hideTimers.values()) {
      clearTimeout(timer);
    }
    this.hideTimers.clear();
  }
}
