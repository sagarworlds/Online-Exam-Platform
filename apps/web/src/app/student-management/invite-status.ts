import { InviteStatus } from '../invite-management/invite.models';
import { StatusTone } from '../shared/status-badge/status-badge';

/** How an invitation's status reads: one that was accepted is done well, a pending one is waiting on someone, the rest are over. */
export function inviteStatusTone(status: InviteStatus): StatusTone {
  switch (status) {
    case 'Accepted':
      return 'success';
    case 'Pending':
      return 'warning';
    default:
      return 'neutral';
  }
}
