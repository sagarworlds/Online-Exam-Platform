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
  /** The code the link carries; given back together with it, when nothing was delivered. */
  inviteCode: string | null;
  /**
   * Whether a direct sign-in link was made for this invitation, whatever has become of it since. Absent from an API that predates them.
   * On a newly created invite with the box ticked, `inviteLink` is that link even when it was delivered, so the inviter can pass it on.
   */
  hasDirectLink?: boolean;
}

export interface InviteCodeDto {
  id: string;
  code: string;
  expiresAt: string;
  usedAt: string | null;
  revokedAt: string | null;
  /** The same code as the link the invitation email carries; set when a code is generated, to hand over as a link. */
  link: string | null;
  /** Whether `link` signs the invited person in when it is opened. Absent from an API that predates direct sign-in. */
  directSignIn?: boolean;
}

/** The inviter is the caller, so the body carries no user id. */
export interface CreateInviteRequest {
  examId: string;
  email: string;
  /** Send a link that signs the student in and takes them to the exam. Needs the `invite.direct-link` permission. */
  directSignIn?: boolean;
}

export interface GenerateInviteCodeRequest {
  expiryHours?: number;
  /** Make a code whose link signs the invited person in. Needs the `invite.direct-link` permission. */
  directSignIn?: boolean;
}

/** The body of POST /v1/invites/direct-sign-in: the code from a direct sign-in link. */
export interface DirectSignInRequest {
  code: string;
}

/** What opening a direct sign-in link returns: the new session, and the exam the invitation was for. */
export interface DirectSignInResult {
  accessToken: string;
  sessionId: string;
  examId: string;
}

/** The body of POST /v1/invites/accept: the code from the invitation link. */
export interface AcceptInviteRequest {
  code: string;
}

/** The body of POST /v1/invites/change-email: moves every invitation sent to one address to another. */
export interface ChangeInviteEmailRequest {
  currentEmail: string;
  newEmail: string;
}

/** How many invitations moved. */
export interface ChangeInviteEmailResult {
  changed: number;
}
