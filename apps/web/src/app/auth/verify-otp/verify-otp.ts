import { Location } from '@angular/common';
import { Component, DestroyRef, ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { AuthSessionService } from '../auth-session.service';
import { landingRoute } from '../landing-route';
import { OtpChannel, VerifyOtpNavigationState } from '../auth.models';
import { maskContact } from '../contact-mask';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';

/** How many digits a one-time code has; the API issues six. */
const CODE_LENGTH = 6;
const CODE_PATTERN = new RegExp(`^\\d{${CODE_LENGTH}}$`);

/** Seconds before a new code can be asked for, so the first one has time to arrive and the button is not pressed in a row. */
const RESEND_COOLDOWN_SECONDS = 30;

@Component({
  selector: 'app-verify-otp',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe],
  templateUrl: './verify-otp.html',
})
export class VerifyOtp {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  private readonly queryParams = this.route.snapshot.queryParamMap;
  /** The challenge a code is checked against. A new one replaces it when a code is asked for again. */
  private readonly challenge = signal(this.queryParams.get('challengeId'));
  protected get challengeId(): string | null {
    return this.challenge();
  }
  protected readonly isRegistration = this.queryParams.get('purpose') === 'Registration';

  /** Without the challenge the page cannot verify anything, so it says so instead of offering a form. */
  protected readonly missingChallenge = !this.challengeId;

  /** Where the code went, as typed; null when no navigation state carried it (e.g. a hard reload). Only the masked form is ever shown. */
  private readonly destination = this.readDestination();

  /** Where the code went, masked; null when no navigation state carried it (e.g. a hard reload). */
  protected readonly maskedDestination = this.destination === null ? null : maskContact(this.destination);

  /**
   * Whether a new code can be asked for from here: only for a login, where the page knows where the code went. A registration's code is tied
   * to the details just entered, and a second-factor code to a password already checked, so for those the way back is the login page.
   */
  protected readonly canResend = this.queryParams.get('purpose') === 'Login' && this.destination !== null;

  /** Seconds until a new code can be asked for; 0 once it can. */
  protected readonly resendIn = signal(RESEND_COOLDOWN_SECONDS);
  protected readonly resending = signal(false);
  /** Said, politely, once a new code has been sent. */
  protected readonly resentNotice = signal(false);
  private cooldownTimer: ReturnType<typeof setInterval> | undefined;

  /** The six boxes the code is shown in, left to right. */
  protected readonly positions = Array.from({ length: CODE_LENGTH }, (_, index) => index);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether the user has pressed Verify; a code that is too short is named only from then on. */
  protected readonly attempted = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(CODE_PATTERN)]],
  });

  constructor() {
    if (this.canResend) {
      this.startCooldown();
    }
    inject(DestroyRef).onDestroy(() => clearInterval(this.cooldownTimer));
  }

  /** The time until a new code can be asked for, as m:ss. */
  protected resendTime(): string {
    const seconds = this.resendIn();
    return `${Math.floor(seconds / 60)}:${(seconds % 60).toString().padStart(2, '0')}`;
  }

  private startCooldown(): void {
    clearInterval(this.cooldownTimer);
    this.resendIn.set(RESEND_COOLDOWN_SECONDS);
    this.cooldownTimer = setInterval(() => {
      const left = this.resendIn() - 1;
      this.resendIn.set(Math.max(0, left));
      if (left <= 0) {
        clearInterval(this.cooldownTimer);
      }
    }, 1000);
  }

  /**
   * Asks for a new code, to where the first one went. The old one stops working with the new challenge, so the boxes are emptied and the
   * focus goes back to them: the candidate types the new one.
   */
  protected resend(): void {
    const destination = this.destination;
    if (!this.canResend || destination === null || this.resendIn() > 0 || this.resending()) {
      return;
    }

    this.resending.set(true);
    this.errorMessage.set(null);
    this.resentNotice.set(false);
    const channel: OtpChannel = destination.includes('@') ? 'Email' : 'Sms';
    this.authApi.requestOtp({ channel, destination }).subscribe({
      next: ({ otpChallengeId }) => {
        this.resending.set(false);
        this.challenge.set(otpChallengeId);
        this.form.controls.code.setValue('');
        this.attempted.set(false);
        this.resentNotice.set(true);
        this.startCooldown();
        this.focusCode();
        // The address keeps pointing at the code that can be used, for a reload, without leaving a trail of pages in the history.
        const state: VerifyOtpNavigationState = { destination };
        void this.router.navigate([], { queryParams: { challengeId: otpChallengeId }, queryParamsHandling: 'merge', replaceUrl: true, state });
      },
      error: (error: unknown) => {
        this.resending.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  /** Puts the cursor in the code box, where the candidate types or pastes, with what is in it selected when asked. */
  private focusCode(select = false): void {
    afterNextRender(
      () => {
        const box = this.host.nativeElement.querySelector<HTMLInputElement>('#verify-otp-code');
        box?.focus();
        if (select) {
          box?.select();
        }
      },
      { injector: this.injector },
    );
  }

  protected codeError(): MessageKey | null {
    return this.attempted() && this.form.controls.code.invalid ? 'verify.codeRequired' : null;
  }

  /** The digit typed into a box, or an empty string while the box is still empty. */
  protected digitAt(position: number): string {
    return this.form.controls.code.value.charAt(position);
  }

  /** The box the next digit goes into, so the one the user is typing in looks active. Past the last box once complete. */
  protected nextPosition(): number {
    return this.form.controls.code.value.length;
  }

  /** Keeps only digits, and no more than the code has, so the boxes always show exactly what will be sent. */
  protected keepDigits(event: Event): void {
    const input = event.target as HTMLInputElement;
    const digits = input.value.replace(/\D/g, '').slice(0, CODE_LENGTH);
    input.value = digits;
    this.form.controls.code.setValue(digits);
    this.resentNotice.set(false);
    // The sixth digit is the last thing there is to type, so the code is sent without a second action; a pasted or auto-filled code is just as complete.
    if (digits.length === CODE_LENGTH) {
      this.submit();
    }
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.form.invalid || this.submitting() || !this.challengeId) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const { code } = this.form.getRawValue();

    this.authApi.verifyOtp({ otpChallengeId: this.challengeId, code }).subscribe({
      next: (result) => {
        if (!result.accessToken) {
          this.submitting.set(false);
          this.errorMessage.set(this.i18n.t('verify.unexpectedResponse'));
          return;
        }

        this.authSession.login(result.accessToken);
        this.router.navigateByUrl(this.queryParams.get('returnUrl') ?? landingRoute(this.authSession));
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
        // The digits stay, as typed, and are selected with the cursor back in the box: typing a whole new code replaces them, and a single
        // wrong digit can be mended, with the reason above.
        this.focusCode(true);
      },
    });
  }

  /**
   * Reads {@link VerifyOtpNavigationState}. The router's current navigation holds
   * it while this component is created as part of that navigation; afterwards
   * the router has copied it to history.state, which Location.getState() reads.
   */
  private readDestination(): string | null {
    const state = (this.router.currentNavigation()?.extras.state ?? this.location.getState()) as
      | Partial<VerifyOtpNavigationState>
      | null
      | undefined;
    const destination = state?.destination;
    return typeof destination === 'string' && destination.trim().length > 0 ? destination : null;
  }
}
