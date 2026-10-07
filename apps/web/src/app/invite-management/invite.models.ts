export type InviteStatus = 'Pending' | 'Accepted' | 'Declined' | 'Expired' | 'Revoked';

/**
 * An invite as the inviting side sees it. `emailSent`, `whatsAppSent` and `inviteLink` are only set when an invite is
 * created: `whatsAppSent` says the exam code was also handed to WhatsApp for the invited person's registered phone, and the
 * link is given back only when neither went out, so the inviter can pass it on by hand.
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
  whatsAppSent: boolean;
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
