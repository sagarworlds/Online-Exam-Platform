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

/** The pending link, and whether the guardian was e-mailed the request to confirm it. */
export interface LinkCandidateResponse {
  link: GuardianLinkDto;
  consentRequestSent: boolean;
  /** Handed back only when no e-mail went out, so staff can pass the confirmation link on. */
  consentLink?: string;
}
