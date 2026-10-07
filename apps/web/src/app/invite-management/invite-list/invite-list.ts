import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteHandover } from '../invite-handover/invite-handover';
import { InviteCodeDto, InviteDto } from '../invite.models';

/**
 * Staff page: the newest invitations and their status, with a way to revoke one that is still open and to copy a code for it to hand
 * over by hand. Each copy asks the API for a new single-use code (a code that was sent is never read back), which is audited.
 */
@Component({
  selector: 'app-invite-list',
  imports: [RouterLink, DatePipe, InviteHandover],
  templateUrl: './invite-list.html',
})
export class InviteList {
  private readonly inviteApi = inject(InviteApiService);

  protected readonly invites = signal<InviteDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  /** The code last made for each invitation (by invite id), shown until hidden or until another is made. */
  protected readonly handovers = signal<Record<string, InviteCodeDto>>({});

  /** The invitation a code is being made for; one at a time, so a double click does not make two. */
  protected readonly generatingFor = signal<string | null>(null);

  constructor() {
    this.load();
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

  protected revoke(invite: InviteDto): void {
    this.errorMessage.set(null);
    this.inviteApi.revokeInvite(invite.id).subscribe({
      next: () => this.load(),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  private load(): void {
    this.inviteApi.getInvites().subscribe({
      next: (invites) => {
        this.invites.set(invites);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
