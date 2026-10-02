export type InviteStatus = 'Pending' | 'Accepted' | 'Declined' | 'Expired' | 'Revoked';

/**
 * An invite as the inviting side sees it. `emailSent` and `inviteLink` are only set when an invite is created:
 * the link is given back only when the e-mail could not be sent, so the inviter can pass it on by hand.
 */
export interface InviteDto {
  id: string;
  examId: string;
  examName: string | null;
  batchMemberId: string | null;
  email: string;
  status: InviteStatus;
  sentAt: string;
  acceptedAt: string | null;
  declinedAt: string | null;
  createdAt: string;
  updatedAt: string;
  emailSent: boolean;
  inviteLink: string | null;
}

export interface InviteCodeDto {
  id: string;
  code: string;
  expiresAt: string;
  usedAt: string | null;
  revokedAt: string | null;
}

/** The inviter is the caller, so the body carries no user id. */
export interface CreateInviteRequest {
  examId: string;
  email: string;
}

export interface GenerateInviteCodeRequest {
  expiryHours?: number;
}

/** The body of POST /v1/invites/accept: the code from the invitation link. */
export interface AcceptInviteRequest {
  code: string;
}
