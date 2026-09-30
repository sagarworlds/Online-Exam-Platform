export type InviteStatus = 'Pending' | 'Sent' | 'Accepted' | 'Declined' | 'Revoked' | 'Expired';

export interface InviteDto {
  id: string;
  examId: string;
  batchMemberId: string;
  email: string;
  status: InviteStatus;
  sentAt: Date;
  acceptedAt?: Date;
  declinedAt?: Date;
  createdAt: Date;
  updatedAt: Date;
}

export interface InviteCodeDto {
  id: string;
  code: string;
  expiresAt: Date;
  usedAt?: Date;
  revokedAt?: Date;
}

export interface CreateInviteRequest {
  examId: string;
  batchMemberId: string;
  email: string;
}

export interface GenerateInviteCodeRequest {
  expiryHours?: number;
}

export interface AcceptInviteRequest {
  inviteCodeId: string;
}
