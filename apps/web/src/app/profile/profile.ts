import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthApiService } from '../auth/auth-api.service';
import { UserProfileDto } from '../auth/auth.models';
import { MAX_DISPLAY_NAME_LENGTH, maxTrimmedLengthValidator, notBlankValidator } from '../auth/validators';
import { I18nService } from '../i18n/i18n.service';
import { MessageKey } from '../i18n/messages.en';
import { TranslatePipe } from '../i18n/translate.pipe';
import { extractErrorMessage } from '../shared/problem-details';

/**
 * The server's user statuses, each with the key that names it here. A status the server adds later is shown as the server sends it,
 * until it has a key.
 */
const STATUS_KEYS: Readonly<Partial<Record<string, MessageKey>>> = {
  PendingVerification: 'profile.state.PendingVerification',
  Active: 'profile.state.Active',
  Suspended: 'profile.state.Suspended',
  Deactivated: 'profile.state.Deactivated',
};

/** The server's role names, each with the key that names it here. A role the server adds later is shown as the server sends it. */
const ROLE_KEYS: Readonly<Partial<Record<string, MessageKey>>> = {
  SuperAdmin: 'profile.role.SuperAdmin',
  ExamAdmin: 'profile.role.ExamAdmin',
  ContentAuthor: 'profile.role.ContentAuthor',
  Reviewer: 'profile.role.Reviewer',
  Proctor: 'profile.role.Proctor',
  InstituteTeacher: 'profile.role.InstituteTeacher',
  Candidate: 'profile.role.Candidate',
  Guardian: 'profile.role.Guardian',
};

/**
 * The candidate's account. The name is the one thing this page changes, so it comes first and carries the only action; the account
 * facts below it are read only. The API stores the display name trimmed, so the form does the same before it sends.
 */
@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './profile.html',
  styleUrl: './profile.css',
})
export class Profile {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authApi = inject(AuthApiService);
  private readonly i18n = inject(I18nService);

  protected readonly maxNameLength = MAX_DISPLAY_NAME_LENGTH;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  /** Whether the candidate has pressed Save; the name is judged only from then on, as on the other forms. */
  protected readonly attempted = signal(false);
  /** A problem loading the account; the name form is not shown then. */
  protected readonly errorMessage = signal<string | null>(null);
  /** A problem saving the name, shown above Save. */
  protected readonly saveError = signal<string | null>(null);
  protected readonly profile = signal<UserProfileDto | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, notBlankValidator, maxTrimmedLengthValidator(MAX_DISPLAY_NAME_LENGTH)]],
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

  /** The name's problem, named once Save has been pressed. A blank name is named the same whether it is empty or only spaces. */
  protected nameError(): MessageKey | null {
    const errors = this.form.controls.displayName.errors;
    if (!this.attempted() || errors === null) {
      return null;
    }

    if (errors['required'] || errors['blank']) {
      return 'profile.nameRequired';
    }
    return errors['maxlength'] ? 'profile.nameTooLong' : null;
  }

  /** The account's status, in the candidate's language where this page has a name for it. */
  protected statusLabel(status: string): string {
    const key = STATUS_KEYS[status];
    return key ? this.i18n.t(key) : status;
  }

  /** The account's roles, in the candidate's language where this page has a name for them, separated by commas. */
  protected roleLabels(roles: readonly string[]): string {
    return roles
      .map((role) => {
        const key = ROLE_KEYS[role];
        return key ? this.i18n.t(key) : role;
      })
      .join(', ');
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.saved.set(false);
    this.saveError.set(null);
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
        this.saveError.set(extractErrorMessage(error));
      },
    });
  }
}
