import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from '../validators';

@Component({
  selector: 'app-password-reset-confirm',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe],
  templateUrl: './password-reset-confirm.html',
})
export class PasswordResetConfirm {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly i18n = inject(I18nService);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly submitted = signal(false);
  /** Whether the user has pressed submit; field problems are named only from then on. */
  protected readonly attempted = signal(false);
  protected readonly passwordVisible = signal(false);

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

  protected codeError(): MessageKey | null {
    return this.attempted() && this.form.controls.resetCode.invalid ? 'reset.codeRequired' : null;
  }

  protected passwordError(): MessageKey | null {
    const errors = this.form.controls.newPassword.errors;
    if (!this.attempted() || !errors) {
      return null;
    }

    if (errors['required']) {
      return 'reset.passwordRequired';
    }
    return errors['minlength'] ? 'reset.passwordTooShort' : 'reset.passwordTooLong';
  }

  protected togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.form.invalid || this.submitting()) {
      return;
    }

    const { resetCode, newPassword } = this.form.getRawValue();
    // The email carries "<passwordResetTokenId>:<token>" as one string (see RequestPasswordResetHandler),
    // so it is split back apart here. Pasted codes often carry stray spaces around them.
    const code = resetCode.trim();
    const separatorIndex = code.indexOf(':');
    if (separatorIndex < 0) {
      this.errorMessage.set(this.i18n.t('reset.codeMalformed'));
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const passwordResetTokenId = code.slice(0, separatorIndex);
    const token = code.slice(separatorIndex + 1);

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
