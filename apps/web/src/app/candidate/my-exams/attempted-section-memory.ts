/** Where the browser remembers whether the candidate left the attempted exams open. */
export const ATTEMPTED_OPEN_STORAGE_KEY = 'exam-platform.my-exams.attempted-open';

/**
 * Whether the candidate last chose to show or hide their attempted exams on this device.
 *
 * @returns true to show them, false to hide them, or null when the candidate has not chosen (or the browser will not say).
 */
export function rememberedAttemptedOpen(): boolean | null {
  try {
    const stored = localStorage.getItem(ATTEMPTED_OPEN_STORAGE_KEY);
    return stored === '1' ? true : stored === '0' ? false : null;
  } catch {
    // Blocked storage is the same as nothing stored.
    return null;
  }
}

/**
 * Remembers the candidate's choice to show or hide their attempted exams, so the page opens the way they left it.
 *
 * @param open true when they chose to show the attempted exams, false when they chose to hide them.
 */
export function rememberAttemptedOpen(open: boolean): void {
  try {
    localStorage.setItem(ATTEMPTED_OPEN_STORAGE_KEY, open ? '1' : '0');
  } catch {
    // A blocked store only means the choice is not remembered; it still applies to this visit.
  }
}
