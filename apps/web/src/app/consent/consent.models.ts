/** What a consent record or notice covers (mirrors the backend's Consent.Contracts.ConsentPurpose). */
export type ConsentPurpose = 'TermsOfService' | 'PrivacyNotice' | 'ProctoringDataProcessing';

export interface ConsentStatusDto {
  subjectId: string;
  purpose: ConsentPurpose;
  isActive: boolean;
  consentRecordId: string | null;
  currentNoticeVersionId: string | null;
}

export interface ConsentRecordDto {
  consentRecordId: string;
  subjectId: string;
  purpose: ConsentPurpose;
  noticeVersionId: string;
  grantedAtUtc: string;
  withdrawnAtUtc: string | null;
}

export interface RecordConsentRequest {
  subjectId: string;
  purpose: ConsentPurpose;
  noticeVersionId: string;
}
