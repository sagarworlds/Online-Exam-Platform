import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { InviteApiService } from '../../invite-management/invite-api.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { MessageKey } from '../../i18n/messages.en';
import { extractErrorMessage } from '../../shared/problem-details';

/**
 * Where an invitation link lands (FR-14): the signed-in candidate accepts the invitation, which enrolls them in the
 * exam. The API only accepts it from the account that holds the e-mail address the invitation was sent to.
 */
@Component({
  selector: 'app-invite-accept',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './invite-accept.html',
})
export class InviteAccept {
  private readonly inviteApi = inject(InviteApiService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  /** The code from the link, or null when the page was opened without one and the candidate must type it. */
  protected readonly linkCode = inject(ActivatedRoute).snapshot.queryParamMap.get('code')?.trim() || null;

  protected readonly accepting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether the candidate has pressed Accept with the code typed in; a blank code is named only from then on. */
  protected readonly attempted = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', Validators.required],
  });

  /** A code made only of spaces passes `Validators.required`, so it is judged on its trimmed value. */
  protected codeError(): MessageKey | null {
    return this.attempted() && this.form.controls.code.value.trim() === '' ? 'invite.codeRequired' : null;
  }

  /** Pressing Accept on the typed code: names a blank code on the field, and otherwise accepts what was typed. */
  protected submitCode(): void {
    this.attempted.set(true);
    this.accept(null);
  }

  protected accept(code: string | null): void {
    const value = (code ?? this.form.getRawValue().code).trim();
    if (!value || this.accepting()) {
      return;
    }

    this.accepting.set(true);
    this.errorMessage.set(null);
    this.inviteApi.acceptInvite(value).subscribe({
      next: () => {
        this.accepting.set(false);
        void this.router.navigateByUrl('/my-exams');
      },
      error: (error: unknown) => {
        this.accepting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
