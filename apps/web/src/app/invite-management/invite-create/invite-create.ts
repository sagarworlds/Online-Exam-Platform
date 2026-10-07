import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ExamApiService } from '../../exam-authoring/exam-api.service';
import { ExamDto } from '../../exam-authoring/exam.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteHandover } from '../invite-handover/invite-handover';
import { InviteCodeDto, InviteDto } from '../invite.models';

/**
 * Staff page: invite an e-mail address to a published exam (FR-14). The invitation e-mail carries a link; when nothing could be sent
 * the API gives the link and its code back so they can be passed on by hand, and when something was, staff can still ask for a code
 * of their own to hand over (read out, pasted into a chat).
 */
@Component({
  selector: 'app-invite-create',
  imports: [ReactiveFormsModule, RouterLink, InviteHandover],
  templateUrl: './invite-create.html',
})
export class InviteCreate {
  private readonly formBuilder = inject(FormBuilder);
  private readonly inviteApi = inject(InviteApiService);
  private readonly examApi = inject(ExamApiService);

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
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
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
        this.errorMessage.set(extractErrorMessage(error));
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

    this.inviteApi.createInvite(this.form.getRawValue()).subscribe({
      next: (invite) => {
        this.saving.set(false);
        this.created.set(invite);
        this.form.controls.email.reset('');
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
