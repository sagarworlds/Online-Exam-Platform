import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { OtpCodesApiService } from './otp-codes-api.service';
import { OutstandingOtp } from './otp-codes.models';

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
 * never on screen for someone who was not looking for it. A revealed code hides again when the search changes, or after a minute. The API
 * records every search.
 */
@Component({
  selector: 'app-otp-codes',
  imports: [DatePipe, TranslatePipe],
  templateUrl: './otp-codes.html',
})
export class OtpCodes {
  private readonly api = inject(OtpCodesApiService);
  private readonly destroyRef = inject(DestroyRef);

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
    this.revealed.update((ids) => new Set(ids).add(challengeId));
    this.clearTimer(challengeId);
    this.hideTimers.set(
      challengeId,
      setTimeout(() => this.hide(challengeId), REVEAL_MS),
    );
  }

  protected hide(challengeId: string): void {
    this.clearTimer(challengeId);
    this.revealed.update((ids) => {
      const next = new Set(ids);
      next.delete(challengeId);
      return next;
    });
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
