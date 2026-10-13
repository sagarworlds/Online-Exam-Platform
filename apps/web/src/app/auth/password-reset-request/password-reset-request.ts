import { Component, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { focusFirstInvalidFieldAfterRender } from '../../shared/focus-first-invalid-field';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AuthApiService } from '../auth-api.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { MessageKey } from '../../i18n/messages.en';

@Component({
  selector: 'app-password-reset-request',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe],
  templateUrl: './password-reset-request.html',
})
export class PasswordResetRequest {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  /** Disabled while the request runs, which drops focus, so focus is put back on it when the request is refused. */
  private readonly submitButton = viewChild<ElementRef<HTMLButtonElement>>('submitButton');

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly submitted = signal(false);
  /** Whether the user has pressed submit; a problem with the email is named only from then on. */
  protected readonly attempted = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected emailError(): MessageKey | null {
    const control = this.form.controls.email;
    if (!this.attempted() || control.valid) {
      return null;
    }

    return control.hasError('required') ? 'login.emailRequired' : 'login.emailInvalid';
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.form.invalid) {
      focusFirstInvalidFieldAfterRender(this.host, this.injector);
      return;
    }
    if (this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    const { email } = this.form.getRawValue();

    // Deliberately reports success even for an unknown email (see
    // RequestPasswordResetHandler on the backend) - never reveal enumeration.
    this.authApi.requestPasswordReset({ email }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.submitted.set(true);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
        afterNextRender(() => this.submitButton()?.nativeElement.focus(), { injector: this.injector });
      },
    });
  }
}
