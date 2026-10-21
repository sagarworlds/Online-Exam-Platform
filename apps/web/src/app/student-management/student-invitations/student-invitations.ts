import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { InviteApiService } from '../../invite-management/invite-api.service';
import { InviteHandover } from '../../invite-management/invite-handover/invite-handover';
import { InviteCodeDto, InviteDto, InviteStatus } from '../../invite-management/invite.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';

/**
 * One student's invitations, newest first, each with its exam and status. A pending one can be revoked, and staff can copy a code for it
 * to hand over by hand; each copy asks the API for a new single-use code (a code that was sent is never read back), which is audited.
 */
@Component({
  selector: 'app-student-invitations',
  imports: [DatePipe, InviteHandover],
  templateUrl: './student-invitations.html',
  styleUrl: './student-invitations.css',
})
export class StudentInvitations {
  private readonly inviteApi = inject(InviteApiService);
  private readonly confirm = inject(ConfirmService);

  /** The student's invitations, newest first. */
  readonly invites = input.required<readonly InviteDto[]>();

  /** Emitted after an invitation was revoked, so the page reads the list again. */
  readonly revoked = output<void>();

  /** The code last made for each invitation (by invite id), shown until hidden or until another is made. */
  protected readonly handovers = signal<Record<string, InviteCodeDto>>({});

  /** The invitation a code is being made for; one at a time, so a double click does not make two. */
  protected readonly generatingFor = signal<string | null>(null);

  protected readonly errorMessage = signal<string | null>(null);

  /** The badge colours of a status: an accepted invitation is done well, a pending one is waiting on someone, the rest are over. */
  protected badgeClass(status: InviteStatus): string {
    switch (status) {
      case 'Accepted':
        return 'badge--active';
      case 'Pending':
        return 'badge--warning';
      default:
        return 'badge--inactive';
    }
  }

  /** The exam an invitation is to; the API leaves the name out for an exam it can no longer find. */
  protected examLabel(invite: InviteDto): string {
    return invite.examName ?? 'Exam not found';
  }

  protected copyCode(invite: InviteDto): void {
    if (this.generatingFor() !== null) {
      return;
    }

    this.errorMessage.set(null);
    this.generatingFor.set(invite.id);
    // The old code goes while the new one is made, so a panel never shows a code the staff member did not just ask for.
    this.hide(invite.id);
    this.inviteApi.generateCode(invite.id).subscribe({
      next: (code) => {
        this.generatingFor.set(null);
        this.handovers.update((all) => ({ ...all, [invite.id]: code }));
      },
      error: (error: unknown) => {
        this.generatingFor.set(null);
        this.errorMessage.set(extractErrorMessage(error));
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
      title: 'Revoke this invitation?',
      message: `The invitation to ${this.examLabel(invite)} stops working, and its code can no longer be used. It cannot be brought back; a new invitation can be sent.`,
      confirmLabel: 'Revoke invitation',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.inviteApi.revokeInvite(invite.id).subscribe({
      next: () => this.revoked.emit(),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }
}
