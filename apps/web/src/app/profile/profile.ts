import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthApiService } from '../auth/auth-api.service';
import { UserProfileDto } from '../auth/auth.models';
import {
  MAX_DISPLAY_NAME_LENGTH,
  displayNameErrorMessage,
  notBlankValidator,
  visibleErrorMessage,
} from '../auth/validators';
import { extractErrorMessage } from '../shared/problem-details';

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule],
  templateUrl: './profile.html',
})
export class Profile {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly profile = signal<UserProfileDto | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, notBlankValidator, Validators.maxLength(MAX_DISPLAY_NAME_LENGTH)]],
  });

  constructor() {
    this.authApi.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.form.patchValue({ displayName: profile.displayName });
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  /** Inline message for the display name field, once the user has interacted with it. */
  protected displayNameError(): string | null {
    return visibleErrorMessage(this.form.controls.displayName, displayNameErrorMessage);
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);
    // The API stores the trimmed name; send it trimmed so the local copy below matches.
    const displayName = this.form.getRawValue().displayName.trim();

    this.authApi.updateProfile({ displayName }).subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.set(true);
        const current = this.profile();
        if (current) {
          this.profile.set({ ...current, displayName });
        }
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
