import { AttemptRequestStatus, AttemptSummaryDto } from '../candidate/candidate.models';

/** One enrolled candidate of an exam, with their attempts and whether another can be given, as staff see them. */
export interface ExamCandidateDto {
  candidateId: string;
  email: string;
  /** How many attempts they may make in all: the exam's limit per candidate, plus each extra attempt given. */
  attemptsAllowed: number;
  attemptsUsed: number;
  /**
   * Whether another extra attempt may be given now: they have used exactly the attempts they hold (not more, which only a lowered
   * limit allows), and the exam can still be started.
   */
  canGrant: boolean;
  /** Oldest first. */
  attempts: AttemptSummaryDto[];
}

/** An exam's enrolled candidates and how each has got on. */
export interface ExamAttemptsDto {
  examId: string;
  examName: string;
  /** Whether nobody can start an attempt any more, so no extra attempt can be given. */
  windowClosed: boolean;
  /** How many attempts every candidate has before any extra is given, as the exam's author set it. */
  attemptsPerCandidate: number;
  candidates: ExamCandidateDto[];
}

/** The statuses the request queue can be filtered to, as the API spells them in the query. */
export type AttemptRequestFilterStatus = 'pending' | 'approved' | 'declined';

/** A candidate's request for another attempt as staff see it in the queue. */
export interface AttemptRequestRow {
  id: string;
  examId: string;
  /** Null when the exam can no longer be read. */
  examName: string | null;
  candidateId: string;
  /** The address the candidate was invited at; null when they are no longer enrolled. */
  candidateEmail: string | null;
  message: string | null;
  requestedAtUtc: string;
  status: AttemptRequestStatus;
  decidedAtUtc: string | null;
  decisionNote: string | null;
  /**
   * Whether the candidate was e-mailed the answer. Present only on the response to approving or declining; false means no mail
   * server is set up, it refused the message, or the candidate is no longer enrolled, so the administrator should tell them.
   */
  candidateNotified?: boolean | null;
}
