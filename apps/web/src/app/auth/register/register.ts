import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { OtpChannel } from '../auth.models';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
})
export class Register {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    channel: this.formBuilder.nonNullable.control<OtpChannel>('Email'),
    destination: ['', Validators.required],
    displayName: ['', Validators.required],
    dateOfBirth: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const { channel, destination, displayName, dateOfBirth } = this.form.getRawValue();

    this.authApi
      .register({
        email: channel === 'Email' ? destination : null,
        phoneNumber: channel === 'Sms' ? destination : null,
        displayName,
        dateOfBirth,
        otpChannel: channel,
      })
      .subscribe({
        next: ({ otpChallengeId }) =>
          this.router.navigate(['/verify-otp'], {
            queryParams: { challengeId: otpChallengeId, purpose: 'Registration', destination, ...this.returnUrlParam() },
          }),
        error: (error: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(extractErrorMessage(error));
        },
      });
  }

  // Where the user was heading before being asked to sign in (e.g. an invitation link), so registering does not lose it.
  private returnUrlParam(): Record<string, string> {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    return returnUrl ? { returnUrl } : {};
  }
}
