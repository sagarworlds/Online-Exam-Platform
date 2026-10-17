import { InviteDto, InviteStatus } from '../invite-management/invite.models';

/** The statuses in the order a group's summary lists them: what still needs staff first, then how the rest ended. */
export const INVITE_STATUS_ORDER: readonly InviteStatus[] = ['Pending', 'Accepted', 'Declined', 'Expired', 'Revoked'];

/** How many of a group's invitations are in one status. */
export interface InviteStatusCount {
  status: InviteStatus;
  count: number;
}

/** Every invitation sent to one student, however many exams that covers. */
export interface InviteGroup {
  /** What the group is identified by: the address without regard to case or surrounding spaces. */
  key: string;
  /** The address as it was written on the group's newest invitation. */
  email: string;
  /** Newest first. */
  invites: InviteDto[];
  /** Only the statuses the group has, in {@link INVITE_STATUS_ORDER}. */
  statusCounts: InviteStatusCount[];
  hasPending: boolean;
}

/** The identity of a student across invitations: an address is the same address whatever its case, as the API treats it. */
export function studentKey(email: string): string {
  return email.trim().toLowerCase();
}

/**
 * Collects each student's invitations into one group, so the page shows a student once however many exams they were invited to.
 * The groups are ordered by their newest invitation and each group's invitations are newest first, whatever order the API listed them in.
 */
export function groupByStudent(invites: readonly InviteDto[]): InviteGroup[] {
  const byKey = new Map<string, InviteDto[]>();
  for (const invite of invites) {
    const key = studentKey(invite.email);
    byKey.set(key, [...(byKey.get(key) ?? []), invite]);
  }

  const groups = [...byKey].map(([key, members]) => {
    // Array.prototype.sort is stable, so two invitations sent at the same instant keep the order the API gave them.
    const newestFirst = [...members].sort((a, b) => sentTime(b) - sentTime(a));
    return {
      key,
      email: newestFirst[0].email,
      invites: newestFirst,
      statusCounts: countByStatus(newestFirst),
      hasPending: newestFirst.some((invite) => invite.status === 'Pending'),
    };
  });

  return groups.sort((a, b) => sentTime(b.invites[0]) - sentTime(a.invites[0]));
}

function countByStatus(invites: readonly InviteDto[]): InviteStatusCount[] {
  return INVITE_STATUS_ORDER.map((status) => ({ status, count: invites.filter((invite) => invite.status === status).length })).filter(
    (entry) => entry.count > 0,
  );
}

// An unreadable date sorts as the oldest, so a malformed row sinks instead of throwing the whole page off.
function sentTime(invite: InviteDto): number {
  const time = Date.parse(invite.sentAt);
  return Number.isNaN(time) ? 0 : time;
}
