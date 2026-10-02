import { ExamConfigDto, ResultReleaseMode, ResultReleaseRequest } from '../exam.models';

/** What the author has chosen for the answer review while the form is open. */
export interface ReleaseSelection {
  mode: ResultReleaseMode;
  /** A `datetime-local` value (the browser's local time, no offset); only used for Scheduled. */
  localTime: string;
}

/** Answers right after submitting, which is what every exam did before the author could choose. */
export const INSTANT_RELEASE: ReleaseSelection = { mode: 'Instant', localTime: '' };

function isValidLocalTime(localTime: string): boolean {
  return localTime !== '' && !Number.isNaN(new Date(localTime).getTime());
}

/** Whether the choice can be saved: a scheduled release needs a time that reads as a date. */
export function isReleaseComplete(selection: ReleaseSelection): boolean {
  return selection.mode !== 'Scheduled' || isValidLocalTime(selection.localTime);
}

/** The body to send; a time is sent only for Scheduled, so a stale one is never posted with another mode. */
export function toReleaseRequest(selection: ReleaseSelection): ResultReleaseRequest {
  return {
    mode: selection.mode,
    // A datetime-local field holds the browser's local time with no offset; the API takes UTC instants.
    releaseTime: selection.mode === 'Scheduled' ? new Date(selection.localTime).toISOString() : null,
  };
}

/** `2026-10-05T14:30` in the browser's local time, the form a datetime-local field takes. */
function toLocalInput(utcIso: string): string {
  const at = new Date(utcIso);
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}T${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

/** The form's starting point: what the exam is set to now. */
export function selectionOfRelease(config: ExamConfigDto | undefined): ReleaseSelection {
  if (config?.resultReleaseMode === 'Scheduled' && config.resultReleaseTime) {
    return { mode: 'Scheduled', localTime: toLocalInput(config.resultReleaseTime) };
  }
  return { mode: config?.resultReleaseMode ?? 'Instant', localTime: '' };
}
