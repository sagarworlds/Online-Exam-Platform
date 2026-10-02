import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { AuthSessionService } from '../auth-session.service';
import { landingRoute } from '../landing-route';

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

  private readonly queryParams = this.route.snapshot.queryParamMap;
  protected readonly challengeId = this.queryParams.get('challengeId');
  protected readonly destination = this.queryParams.get('destination');

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
}
