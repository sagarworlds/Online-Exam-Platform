import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { Permission } from '../../auth/admin-sections';
import { ExamApiService } from '../../exam-authoring/exam-api.service';
import { ExamDto } from '../../exam-authoring/exam.models';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteHandover } from '../invite-handover/invite-handover';
import { CreateInviteRequest, InviteCodeDto, InviteDto } from '../invite.models';
import { I18nService } from '../../i18n/i18n.service';

/**
 * Staff page: invite an email address to a published exam (FR-14). The invitation email carries a link; when nothing could be sent
 * the API gives the link and its code back so they can be passed on by hand, and when something was, staff can still ask for a code
 * of their own to hand over (read out, pasted into a chat).
 */
@Component({
  selector: 'app-invite-create',
  imports: [ReactiveFormsModule, RouterLink, InviteHandover, TranslatePipe],
  templateUrl: './invite-create.html',
})
export class InviteCreate {
  private readonly formBuilder = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly inviteApi = inject(InviteApiService);
  private readonly examApi = inject(ExamApiService);
  private readonly authSession = inject(AuthSessionService);

  /**
   * Whether the signed-in staff member may send a link that signs the student in. Decides only whether the box is offered; the API
   * checks the permission again on every request.
   */
  protected readonly canDirectLink = computed(() => this.authSession.hasPermission(Permission.InviteDirectLink));

  protected readonly exams = signal<ExamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly created = signal<InviteDto | null>(null);

  /** A code staff asked for to hand over, for the invitation just made. */
  protected readonly handover = signal<InviteCodeDto | null>(null);
  protected readonly generating = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    examId: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    directSignIn: [false],
  });

  constructor() {
    this.examApi.getExams().subscribe({
      next: (exams) => {
        // Only a published exam can be taken, so only those can be invited to.
        this.exams.set(exams.filter((exam) => exam.status === 'Published'));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** The code a direct link carries, to show beside it; the link has it in its fragment. */
  protected codeOf(link: string): string {
    return new URLSearchParams(link.split('#')[1] ?? '').get('code') ?? '';
  }

  protected copyCode(invite: InviteDto): void {
    if (this.generating()) {
      return;
    }

    this.errorMessage.set(null);
    this.generating.set(true);
    this.handover.set(null);
    this.inviteApi.generateCode(invite.id).subscribe({
      next: (code) => {
        this.generating.set(false);
        this.handover.set(code);
      },
      error: (error: unknown) => {
        this.generating.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);
    this.created.set(null);
    this.handover.set(null);

    const { examId, email, directSignIn } = this.form.getRawValue();
    // Only a request that ticks the box says anything about it, so an ordinary invitation is exactly what it was.
    const request: CreateInviteRequest = this.canDirectLink() && directSignIn ? { examId, email, directSignIn: true } : { examId, email };
    this.inviteApi.createInvite(request).subscribe({
      next: (invite) => {
        this.saving.set(false);
        this.created.set(invite);
        this.form.controls.email.reset('');
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }
}
