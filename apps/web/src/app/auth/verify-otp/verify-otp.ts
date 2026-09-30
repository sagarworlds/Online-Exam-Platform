import { Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { AuthSessionService } from '../auth-session.service';
<<<<<<< HEAD
import { landingRoute } from '../landing-route';
=======
import { VerifyOtpNavigationState } from '../auth.models';
import { maskContact } from '../contact-mask';
>>>>>>> 3f1e9b1 (fix(web): guide staff to password + 2FA, enforce password length, keep contact details out of URLs (FR-3, NFR-6))

@Component({
  selector: 'app-verify-otp',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './verify-otp.html',
})
export class VerifyOtp {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);

  private readonly queryParams = this.route.snapshot.queryParamMap;
  protected readonly challengeId = this.queryParams.get('challengeId');
  protected readonly isRegistration = this.queryParams.get('purpose') === 'Registration';

  /** Where the code went, masked; null when no navigation state carried it (e.g. a hard reload). */
  protected readonly maskedDestination = this.readMaskedDestination();

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(
    this.challengeId ? null : 'Missing verification challenge — start again from login or registration.',
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', Validators.required],
  });

  protected submit(): void {
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
          this.errorMessage.set('Unexpected response from the server.');
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
