export type BatchStatus = 'Draft' | 'Active' | 'Closed' | 'Archived';
export type MemberRegistrationStatus = 'Pending' | 'Registered' | 'Verified' | 'Inactive';

export interface BatchDto {
  id: string;
  examId: string;
  name: string;
  description?: string;
  status: BatchStatus;
  maxMembers: number;
  activeMemberCount: number;
  createdBy: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface BatchMemberDto {
  id: string;
  email: string;
  phone?: string;
  status: MemberRegistrationStatus;
}

export interface CreateBatchRequest {
  examId: string;
  name: string;
  description?: string;
  maxMembers: number;
}

export interface AddBatchMemberRequest {
  email: string;
  phone?: string;
}

/** One roster row that was given a seat. */
export interface RosterAddedRow {
  /** The row's number in the file, counting a header as row 1. */
  row: number;
  memberId: string;
}

/** One roster row that was not given a seat. The API names the row by number only, never by address. */
export interface RosterSkippedRow {
  row: number;
  /** A stable name for the reason, such as `invalid_email` or `batch_full`. */
  code: string;
  /** The reason in English, safe to show to whoever sent the file. */
  message: string;
}

/** The report a roster import returns (FR-50): bad rows are skipped and listed, never a reason to refuse the file. */
export interface ImportRosterResult {
  added: RosterAddedRow[];
  skipped: RosterSkippedRow[];
  /** The columns of the file that a roster does not use. */
  ignoredColumns: string[];
}
