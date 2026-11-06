import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { InviteApiService } from '../../invite-management/invite-api.service';
import { InviteHandover } from '../../invite-management/invite-handover/invite-handover';
import { InviteCodeDto, InviteDto, InviteStatus } from '../../invite-management/invite.models';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { StatusBadge, StatusTone } from '../../shared/status-badge/status-badge';
import { inviteStatusTone } from '../invite-status';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { LocaleDatePipe } from '../../i18n/locale-date.pipe';

/**
 * One student's invitations, newest first, each with its exam and status. A pending one can be revoked, and staff can copy a code for it
 * to hand over by hand; each copy asks the API for a new single-use code (a code that was sent is never read back), which is audited.
 */
@Component({
  selector: 'app-student-invitations',
  imports: [LocaleDatePipe, InviteHandover, StatusBadge, TranslatePipe],
  templateUrl: './student-invitations.html',
  styleUrl: './student-invitations.css',
})
export class StudentInvitations {
  private readonly i18n = inject(I18nService);
  private readonly inviteApi = inject(InviteApiService);
  private readonly confirm = inject(ConfirmService);
  private readonly authSession = inject(AuthSessionService);

  /** Whether the staff member may make a link that signs the student in; the API checks again, this only decides whether it is offered. */
  protected readonly canDirectLink = computed(() => this.authSession.hasPermission(Permission.InviteDirectLink));

  /** The student's invitations, newest first. */
  readonly invites = input.required<readonly InviteDto[]>();

  /** Emitted after an invitation was revoked, so the page reads the list again. */
  readonly revoked = output<void>();

  /** The code last made for each invitation (by invite id), shown until hidden or until another is made. */
  protected readonly handovers = signal<Record<string, InviteCodeDto>>({});

  /** The invitation a code is being made for; one at a time, so a double click does not make two. */
  protected readonly generatingFor = signal<string | null>(null);

  protected readonly errorMessage = signal<string | null>(null);

  protected statusLabel(status: InviteStatus): string {
    return this.i18n.t(`invites.status.${status}` as MessageKey);
  }

  protected statusTone(status: InviteStatus): StatusTone {
    return inviteStatusTone(status);
  }

  /** The exam an invitation is to; the API leaves the name out for an exam it can no longer find. */
  protected examLabel(invite: InviteDto): string {
    return invite.examName ?? this.i18n.t('students.invite.examGone');
  }

  protected copyCode(invite: InviteDto): void {
    this.makeCode(invite, false);
  }

  /** Makes a new code whose link signs the student in. Each one is audited, and the list never shows one that was sent before. */
  protected copyDirectLink(invite: InviteDto): void {
    this.makeCode(invite, true);
  }

  private makeCode(invite: InviteDto, directSignIn: boolean): void {
    if (this.generatingFor() !== null) {
      return;
    }

    this.errorMessage.set(null);
    this.generatingFor.set(invite.id);
    // The old code goes while the new one is made, so a panel never shows a code the staff member did not just ask for.
    this.hide(invite.id);
    this.inviteApi.generateCode(invite.id, undefined, directSignIn).subscribe({
      next: (code) => {
        this.generatingFor.set(null);
        this.handovers.update((all) => ({ ...all, [invite.id]: code }));
      },
      error: (error: unknown) => {
        this.generatingFor.set(null);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  protected hide(inviteId: string): void {
    this.handovers.update((all) => {
      const rest = { ...all };
      delete rest[inviteId];
      return rest;
    });
  }

  /** Asks first: a revoked invitation cannot be brought back, only sent again, and its code stops working at once. */
  protected async revoke(invite: InviteDto): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: this.i18n.t('students.invite.revokeTitle'),
      message: this.i18n.t('students.invite.revokeMessage', { exam: this.examLabel(invite) }),
      confirmLabel: this.i18n.t('students.invite.revokeConfirm'),
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.inviteApi.revokeInvite(invite.id).subscribe({
      next: () => this.revoked.emit(),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong'))),
    });
  }
}
