import { AttemptSummaryDto } from '../candidate/candidate.models';

/** One enrolled candidate of an exam, with their attempts and whether another can be given, as staff see them. */
export interface ExamCandidateDto {
  candidateId: string;
  email: string;
  /** How many attempts they may make in all: one, plus each extra attempt given. */
  attemptsAllowed: number;
  attemptsUsed: number;
  /** Whether another extra attempt may be given now: they have used every attempt they hold, and the exam can still be started. */
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
  candidates: ExamCandidateDto[];
}
