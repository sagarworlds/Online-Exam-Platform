/** Whether a candidate can start an exam right now: not yet, yes, or the window (or its late-entry cutoff) has passed. */
export type MyExamState = 'NotOpen' | 'Open' | 'Closed';

/** An exam the candidate is enrolled in, as listed on their exams page. All instants are UTC. */
export interface MyExamDto {
  examId: string;
  name: string;
  description: string | null;
  startUtc: string;
  endUtc: string;
  lateEntryDeadlineUtc: string | null;
  durationSeconds: number | null;
  questionCount: number;
  state: MyExamState;
}
