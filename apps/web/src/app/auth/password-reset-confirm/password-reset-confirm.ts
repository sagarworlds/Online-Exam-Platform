import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import {
  MAX_PASSWORD_LENGTH,
  MIN_PASSWORD_LENGTH,
  newPasswordErrorMessage,
  visibleErrorMessage,
} from '../validators';

@Component({
  selector: 'app-password-reset-confirm',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './password-reset-confirm.html',
})
export class PasswordResetConfirm {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly submitted = signal(false);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;
  protected readonly maxPasswordLength = MAX_PASSWORD_LENGTH;

  protected readonly form = this.formBuilder.nonNullable.group({
    resetCode: ['', Validators.required],
    // Only the length rules are checked here; the API's weak_password detail
    // covers the rest (e.g. containing the email) and is shown as-is.
    newPassword: [
      '',
      [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH), Validators.maxLength(MAX_PASSWORD_LENGTH)],
    ],
  });

  /** Inline message for the new password field, once the user has interacted with it. */
  protected newPasswordError(): string | null {
    return visibleErrorMessage(this.form.controls.newPassword, newPasswordErrorMessage);
  }

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    const { resetCode, newPassword } = this.form.getRawValue();
    // The dev console/email link encodes "<passwordResetTokenId>:<token>" as one
    // string (see RequestPasswordResetHandler) - split it back apart here.
    const separatorIndex = resetCode.indexOf(':');
    if (separatorIndex < 0) {
      this.errorMessage.set('Reset code must be in the form "<id>:<token>" (see the console/email).');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const passwordResetTokenId = resetCode.slice(0, separatorIndex);
    const token = resetCode.slice(separatorIndex + 1);

    this.authApi.resetPassword({ passwordResetTokenId, token, newPassword }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.submitted.set(true);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
