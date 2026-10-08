import { Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { AuthSessionService } from '../auth-session.service';
import { landingRoute } from '../landing-route';
import { VerifyOtpNavigationState } from '../auth.models';
import { maskContact } from '../contact-mask';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';

/** How many digits a one-time code has; the API issues six. */
const CODE_LENGTH = 6;
const CODE_PATTERN = new RegExp(`^\\d{${CODE_LENGTH}}$`);

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

  private readonly queryParams = this.route.snapshot.queryParamMap;
  protected readonly challengeId = this.queryParams.get('challengeId');
  protected readonly isRegistration = this.queryParams.get('purpose') === 'Registration';

  /** Without the challenge the page cannot verify anything, so it says so instead of offering a form. */
  protected readonly missingChallenge = !this.challengeId;

  /** Where the code went, masked; null when no navigation state carried it (e.g. a hard reload). */
  protected readonly maskedDestination = this.readMaskedDestination();

  /** The six boxes the code is shown in, left to right. */
  protected readonly positions = Array.from({ length: CODE_LENGTH }, (_, index) => index);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether the user has pressed Verify; a code that is too short is named only from then on. */
  protected readonly attempted = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(CODE_PATTERN)]],
  });

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
      },
    });
  }

  /**
   * Reads {@link VerifyOtpNavigationState}. The router's current navigation holds
   * it while this component is created as part of that navigation; afterwards
   * the router has copied it to history.state, which Location.getState() reads.
   */
  private readMaskedDestination(): string | null {
    const state = (this.router.currentNavigation()?.extras.state ?? this.location.getState()) as
      | Partial<VerifyOtpNavigationState>
      | null
      | undefined;
    const destination = state?.destination;
    return typeof destination === 'string' && destination.trim().length > 0 ? maskContact(destination) : null;
  }
}
