import { AttemptSummaryDto } from './candidate.models';

/**
 * Which of a candidate's attempts to mark as the best: the submitted one with the highest score, the earlier one when two tie.
 * Null when fewer than two attempts are submitted, because calling the only result "best" would say nothing. Nothing else uses
 * a single official score yet, so this is only for display; no result is stored as the exam's.
 */
export function bestAttemptId(attempts: readonly AttemptSummaryDto[]): string | null {
  const scored = attempts
    .filter((attempt) => attempt.status === 'Submitted' && attempt.score !== null)
    .sort((a, b) => a.number - b.number);
  if (scored.length < 2) {
    return null;
  }

  // Strictly greater, so on a tie the earlier attempt (first in the list) keeps the badge.
  return scored.reduce((best, attempt) => ((attempt.score ?? 0) > (best.score ?? 0) ? attempt : best)).id;
}
