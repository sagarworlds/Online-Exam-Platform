import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { OtpChannel, VerifyOtpNavigationState } from '../auth.models';
import {
  MAX_DISPLAY_NAME_LENGTH,
  dateOfBirthErrorMessage,
  dateOfBirthValidator,
  displayNameErrorMessage,
  maxTrimmedLengthValidator,
  notBlankValidator,
  toLocalIsoDate,
  visibleErrorMessage,
} from '../validators';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe],
  templateUrl: './register.html',
})
export class Register {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** Upper bound for the date picker; dateOfBirthValidator is what actually enforces the rule. */
  protected readonly maxDateOfBirth = toLocalIsoDate(new Date());

  protected readonly form = this.formBuilder.nonNullable.group({
    channel: this.formBuilder.nonNullable.control<OtpChannel>('Email'),
    destination: ['', Validators.required],
    displayName: ['', [Validators.required, notBlankValidator, maxTrimmedLengthValidator(MAX_DISPLAY_NAME_LENGTH)]],
    dateOfBirth: ['', dateOfBirthValidator()],
  });

  /** Inline message for the display name field, once the user has interacted with it. */
  protected displayNameError(): string | null {
    return visibleErrorMessage(this.form.controls.displayName, displayNameErrorMessage);
  }

  /** Inline message for the date of birth field, once the user has interacted with it. */
  protected dateOfBirthError(): string | null {
    return visibleErrorMessage(this.form.controls.dateOfBirth, dateOfBirthErrorMessage);
  }

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
        displayName: displayName.trim(),
        dateOfBirth,
        otpChannel: channel,
      })
      .subscribe({
        next: ({ otpChallengeId }) => {
          const state: VerifyOtpNavigationState = { destination };
          this.router.navigate(['/verify-otp'], {
            queryParams: { challengeId: otpChallengeId, purpose: 'Registration', ...this.returnUrlParam() },
            state,
          });
        },
        // Rules only the server can check (contact_channel_mismatch, a duplicate
        // account, ...) and any drift from the client-side checks come back as a
        // ProblemDetails whose detail is already actionable.
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
