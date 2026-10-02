import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { AuthSessionService } from '../auth-session.service';
import { landingRoute } from '../landing-route';
import { AuthResult, OtpChannel } from '../auth.models';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
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
  }

  protected submitPassword(): void {
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
    if (this.otpForm.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const { destination } = this.otpForm.getRawValue();
    const channel: OtpChannel = destination.includes('@') ? 'Email' : 'Sms';

    this.authApi.requestOtp({ channel, destination }).subscribe({
      next: ({ otpChallengeId }) =>
        this.router.navigate(['/verify-otp'], {
          queryParams: { challengeId: otpChallengeId, purpose: 'Login', destination, ...this.returnUrlParam() },
        }),
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
