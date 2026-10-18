/** What a person asks for under their data rights (mirrors the backend's DataRequestKind, FR-48). */
export type DataRequestKind = 'Access' | 'Correction' | 'Erasure';

/** The kinds in the order the candidate's form offers them. */
export const DATA_REQUEST_KINDS: readonly DataRequestKind[] = ['Access', 'Correction', 'Erasure'];

/** Where a request stands. It is open only while it is `Received`; staff then answer it once, as `Completed` or `Rejected`. */
export type DataRequestStatus = 'Received' | 'Completed' | 'Rejected';

/** The two answers staff can give a request. */
export type DataRequestOutcome = Extract<DataRequestStatus, 'Completed' | 'Rejected'>;

/** The longest details a candidate may give, and the longest note staff may give, after surrounding whitespace is removed (the API's limits). */
export const MAX_DATA_REQUEST_DETAILS = 1000;
export const MAX_DATA_REQUEST_NOTE = 1000;

/** One data request, as the candidate who made it sees it. */
export interface DataRequestDto {
  id: string;
  userId: string;
  kind: DataRequestKind;
  status: DataRequestStatus;
  /** What the candidate wrote, or null. Free text: show it as text, never as markup. */
  details: string | null;
  receivedAtUtc: string;
  /** When staff must have answered by. */
  dueAtUtc: string;
  /** Whether it is still open past its due time. */
  isOverdue: boolean;
  resolvedAtUtc: string | null;
  /** What staff did, or why they refused. */
  resolutionNote: string | null;
}

/**
 * One open request as staff see it: the request, with who made it, so the answer can reach them (a copy is sent to their address, a
 * correction is checked with them). Each of these is null when the account has none, and the three are absent from an API that predates them.
 */
export interface OpenDataRequestDto extends DataRequestDto {
  requesterName?: string | null;
  requesterEmail?: string | null;
  requesterPhoneNumber?: string | null;
}

/** The body of POST /v1/me/data-requests. The account comes from the token. */
export interface RaiseDataRequest {
  kind: DataRequestKind;
  details: string | null;
}

/** The body of POST /v1/data-requests/{id}/resolve. A refusal must carry a note. */
export interface ResolveDataRequest {
  outcome: DataRequestOutcome;
  note: string | null;
}
