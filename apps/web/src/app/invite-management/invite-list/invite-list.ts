import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteHandover } from '../invite-handover/invite-handover';
import { InviteCodeDto, InviteDto, InviteStatus } from '../invite.models';
import { InviteGroup, groupByStudent } from './invite-groups';

/**
 * Staff page: the newest invitations and their status, with a way to revoke one that is still open and to copy a code for it to hand
 * over by hand. Each copy asks the API for a new single-use code (a code that was sent is never read back), which is audited.
 *
 * A student invited to several exams appears once, as a group that folds away: the invitations were scattered down the page, one card
 * each, so finding everything sent to one person meant scanning the whole list.
 */
@Component({
  selector: 'app-invite-list',
  imports: [RouterLink, DatePipe, InviteHandover],
  templateUrl: './invite-list.html',
  styleUrl: './invite-list.css',
})
export class InviteList {
  private readonly inviteApi = inject(InviteApiService);

  protected readonly invites = signal<InviteDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly groups = computed(() => groupByStudent(this.invites()));

  /** How much the page holds, above the groups: "3 students · 7 invitations". */
  protected readonly summary = computed(() => {
    const students = this.groups().length;
    const invitations = this.invites().length;
    return `${students} ${students === 1 ? 'student' : 'students'} · ${invitations} ${invitations === 1 ? 'invitation' : 'invitations'}`;
  });

  /** The groups that are open, by {@link InviteGroup.key}. Set from the first listing, then only the staff member's choices change it. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());
  private expandedChosen = false;

  /** The code last made for each invitation (by invite id), shown until hidden or until another is made. */
  protected readonly handovers = signal<Record<string, InviteCodeDto>>({});

  /** The invitation a code is being made for; one at a time, so a double click does not make two. */
  protected readonly generatingFor = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected isOpen(group: InviteGroup): boolean {
    return this.expanded().has(group.key);
  }

  protected toggle(group: InviteGroup): void {
    this.expanded.update((open) => {
      const next = new Set(open);
      if (!next.delete(group.key)) {
        next.add(group.key);
      }
      return next;
    });
  }

  protected expandAll(): void {
    this.expanded.set(new Set(this.groups().map((group) => group.key)));
  }

  protected collapseAll(): void {
    this.expanded.set(new Set());
  }

  /** The words for a count of invitations, so the header does not say "1 invitations". */
  protected invitationCount(group: InviteGroup): string {
    return group.invites.length === 1 ? '1 invitation' : `${group.invites.length} invitations`;
  }

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
        this.openGroupsWithWorkToDo();
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  /**
   * On the first listing, opens the groups that have an invitation still pending, since those are the ones with something to do (copy a
   * code, revoke) and the rest are history. Later listings, such as the one after a revoke, leave the choice alone: the group just acted in
   * has nothing pending now and would otherwise fold up under the staff member's cursor.
   */
  private openGroupsWithWorkToDo(): void {
    if (this.expandedChosen) {
      return;
    }

    this.expandedChosen = true;
    this.expanded.set(new Set(this.groups().filter((group) => group.hasPending).map((group) => group.key)));
  }
}
