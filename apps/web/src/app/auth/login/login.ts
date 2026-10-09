import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { AuthSessionService } from '../auth-session.service';
import { landingRoute } from '../landing-route';
import { AuthResult, OtpChannel, VerifyOtpNavigationState } from '../auth.models';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { MessageKey } from '../../i18n/messages.en';
import { SessionEndReason, isSessionEndReason } from '../session-end-reason';

/** Banner copy for each reason the API gives when it refuses a session (see authInterceptor). */
const SESSION_END_MESSAGES: Record<SessionEndReason, string> = {
  session_superseded: 'You were signed out because your account signed in on another device.',
  session_revoked:
    'You were signed out because your session was ended, for example by logging out elsewhere or resetting your password.',
  session_expired: 'Your session expired. Please log in again.',
  account_locked:
    'You were signed out because your account is locked. Contact your administrator if you think this is a mistake.',
  session_unknown: 'Your session is no longer valid. Please log in again.',
};

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe],
  templateUrl: './login.html',
})
export class Login {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly mode = signal<'password' | 'otp'>('password');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether the user has pressed submit on the current form; field problems are shown only after that. */
  protected readonly attempted = signal(false);
  protected readonly passwordVisible = signal(false);

  /** Why the user was signed out, when the interceptor redirected here with a known `?reason=`. */
  protected readonly sessionEndedMessage = describeSessionEnd(this.route.snapshot.queryParamMap.get('reason'));

  protected readonly passwordForm = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected readonly otpForm = this.formBuilder.nonNullable.group({
    destination: ['', Validators.required],
  });

  protected setMode(mode: 'password' | 'otp'): void {
    this.mode.set(mode);
    this.errorMessage.set(null);
    this.attempted.set(false);
  }

  protected togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  /** What is wrong with the email, once the user has tried to submit; the button stays enabled so it can say so. */
  protected emailError(): MessageKey | null {
    const control = this.passwordForm.controls.email;
    if (!this.attempted() || control.valid) {
      return null;
    }

    return control.hasError('required') ? 'login.emailRequired' : 'login.emailInvalid';
  }

  protected passwordError(): MessageKey | null {
    return this.attempted() && this.passwordForm.controls.password.invalid ? 'login.passwordRequired' : null;
  }

  protected destinationError(): MessageKey | null {
    return this.attempted() && this.otpForm.controls.destination.invalid ? 'login.destinationRequired' : null;
  }

  protected submitPassword(): void {
    this.attempted.set(true);
    if (this.passwordForm.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const { email, password } = this.passwordForm.getRawValue();

    this.authApi.login({ email, password }).subscribe({
      next: (result) => this.handleAuthResult(result),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected submitOtpRequest(): void {
    this.attempted.set(true);
    if (this.otpForm.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const { destination } = this.otpForm.getRawValue();
    const channel: OtpChannel = destination.includes('@') ? 'Email' : 'Sms';

    this.authApi.requestOtp({ channel, destination }).subscribe({
      next: ({ otpChallengeId }) => {
        const state: VerifyOtpNavigationState = { destination };
        this.router.navigate(['/verify-otp'], {
          queryParams: { challengeId: otpChallengeId, purpose: 'Login', ...this.returnUrlParam() },
          state,
        });
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private handleAuthResult(result: AuthResult): void {
    if (result.requiresTwoFactor && result.otpChallengeId) {
      this.router.navigate(['/verify-otp'], {
        queryParams: { challengeId: result.otpChallengeId, purpose: 'TwoFactor', ...this.returnUrlParam() },
      });
      return;
    }

    if (result.accessToken) {
      this.authSession.login(result.accessToken);
      this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') ?? landingRoute(this.authSession));
      return;
    }

    this.submitting.set(false);
    this.errorMessage.set('Unexpected response from the server.');
  }

  /** The query the register link carries, so signing up does not lose the page the user was heading to. */
  protected get registerQueryParams(): Record<string, string> {
    return this.returnUrlParam();
  }

  private returnUrlParam(): Record<string, string> {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    return returnUrl ? { returnUrl } : {};
  }
}

/** Maps a `?reason=` value to its banner copy; unknown or missing reasons show no banner. */
function describeSessionEnd(reason: string | null): string | null {
  return isSessionEndReason(reason) ? SESSION_END_MESSAGES[reason] : null;
}
