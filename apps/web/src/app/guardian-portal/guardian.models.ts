export type GuardianLinkStatus = 'Pending' | 'Verified' | 'Revoked';

export interface GuardianDto {
  id: string;
  email: string;
  phone?: string;
  fullName: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface GuardianLinkDto {
  id: string;
  guardianId: string;
  candidateId: string;
  candidateEmail: string;
  status: GuardianLinkStatus;
  verifiedAt?: Date;
  revokedAt?: Date;
}

export interface CreateGuardianRequest {
  email: string;
  fullName: string;
  phone?: string;
}

export interface LinkCandidateRequest {
  candidateId: string;
  candidateEmail: string;
}
