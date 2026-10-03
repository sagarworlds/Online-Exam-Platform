/** Where an attempt's visited questions are remembered. One key per attempt, so attempts never share state. */
const storageKey = (attemptId: string): string => `exam.visited.${attemptId}`;

/**
 * Reads the ids of the questions a candidate has already had on screen in an attempt.
 * Browser-only and best effort: a blocked store or damaged value reads as "none visited", which only
 * costs the candidate the "not visited" colour, never an answer.
 *
 * @param attemptId The attempt being sat.
 * @returns The remembered question ids; empty when nothing usable is stored.
 */
export function loadVisitedQuestions(attemptId: string): ReadonlySet<string> {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey(attemptId)) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : []);
  } catch (error) {
    console.warn('Could not read the visited questions; treating every question as not visited.', error);
    return new Set();
  }
}

/**
 * Remembers the visited questions of an attempt. A failure (store full or blocked) is logged and swallowed:
 * visits are a comfort aid, so they must never interrupt the exam.
 *
 * @param attemptId The attempt being sat.
 * @param visited The question ids to remember.
 */
export function saveVisitedQuestions(attemptId: string, visited: ReadonlySet<string>): void {
  try {
    localStorage.setItem(storageKey(attemptId), JSON.stringify([...visited]));
  } catch (error) {
    console.warn('Could not remember the visited questions.', error);
  }
}
