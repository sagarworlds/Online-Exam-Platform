import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteDto } from '../invite.models';

/** Staff page: the newest invitations and their status, with a way to revoke one that is still open. */
@Component({
  selector: 'app-invite-list',
  imports: [RouterLink, DatePipe],
  templateUrl: './invite-list.html',
})
export class InviteList {
  private readonly inviteApi = inject(InviteApiService);

  protected readonly invites = signal<InviteDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    this.load();
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
