import { Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ModalDialog } from '../../shared/modal-dialog/modal-dialog';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteSent } from '../invite-sent/invite-sent';
import { CreateInviteRequest, InviteDto } from '../invite.models';

/**
 * A popup to invite a candidate to one exam from the exam's own candidates page (FR-14): the exam is already chosen, so all that is asked
 * for is the address (and, for staff who may, whether the link signs the student in). It sends the same invitation as the invitation page
 * and tells staff what happened the same way, with the link and code to pass on by hand when nothing could be sent.
 *
 * The page shows it with `@if` and takes it away when {@link closed} is emitted. The candidates it lists are those who have accepted, so a
 * candidate just invited shows on that page only once they accept; the popup says so.
 */
@Component({
  selector: 'app-invite-to-exam',
  imports: [ReactiveFormsModule, ModalDialog, InviteSent, TranslatePipe],
  templateUrl: './invite-to-exam.html',
})
export class InviteToExam {
  private readonly formBuilder = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly inviteApi = inject(InviteApiService);
  private readonly authSession = inject(AuthSessionService);
  private readonly injector = inject(Injector);

  private readonly emailInput = viewChild<ElementRef<HTMLInputElement>>('emailInput');
  private readonly result = viewChild<ElementRef<HTMLElement>>('result');

  /** The exam the candidate is invited to. */
  readonly examId = input.required<string>();

  /** Its name, for the title. */
  readonly examName = input.required<string>();

  /** Emitted once the popup has closed, however it was closed. */
  readonly closed = output<void>();

  /**
   * Whether the signed-in staff member may send a link that signs the student in. Decides only whether the box is offered; the API checks
   * the permission again on every request.
   */
  protected readonly canDirectLink = computed(() => this.authSession.hasPermission(Permission.InviteDirectLink));

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly created = signal<InviteDto | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    directSignIn: [false],
  });

  constructor() {
    // The one thing to do here is to type an address, so the box is ready for it. Not the `autofocus` attribute, which a screen reader user cannot expect.
    this.focusAfterRender(this.emailInput);
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { email, directSignIn } = this.form.getRawValue();
    const examId = this.examId();
    // Only a request that ticks the box says anything about it, so an ordinary invitation is exactly what it was.
    const request: CreateInviteRequest = this.canDirectLink() && directSignIn ? { examId, email: email.trim(), directSignIn: true } : { examId, email: email.trim() };
    this.inviteApi.createInvite(request).subscribe({
      next: (invite) => {
        this.saving.set(false);
        this.created.set(invite);
        this.focusAfterRender(this.result);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Back to an empty form for the next candidate; what was told about the last one goes with it. */
  protected inviteAnother(): void {
    this.created.set(null);
    this.errorMessage.set(null);
    this.form.reset({ email: '', directSignIn: false });
    this.focusAfterRender(this.emailInput);
  }

  /** Moves focus once the view has been drawn: what is to be focused may not be there until it has. */
  private focusAfterRender(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }
}
