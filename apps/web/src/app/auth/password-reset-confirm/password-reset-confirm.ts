import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';

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

  protected readonly form = this.formBuilder.nonNullable.group({
    resetCode: ['', Validators.required],
    newPassword: ['', Validators.required],
  });

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
