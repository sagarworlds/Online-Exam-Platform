/** One released result on the score trend (FR-36), oldest first. */
export interface ScorePointDto {
  attemptId: string;
  examId: string;
  examName: string;
  /** When the attempt was submitted, as an ISO-8601 UTC string. */
  submittedAtUtc: string;
  score: number;
  maxScore: number;
  /** The score as a share of the marks available, in percent; null when the exam had no marks to earn. */
  percentOfMarks: number | null;
}

/** How the candidate's answers fell in one section name, across every released result that had it (FR-36). */
export interface SectionPerformanceDto {
  name: string;
  resultCount: number;
  correctCount: number;
  wrongCount: number;
  partialCount: number;
  unansweredCount: number;
  /** The share of answered questions that were fully correct, in percent; null when nothing in the section was answered. */
  accuracy: number | null;
}

/** A candidate's own performance across the exams they sat (FR-36). */
export interface CandidateAnalyticsDto {
  resultCount: number;
  trend: ScorePointDto[];
  /** Weakest accuracy first. */
  sections: SectionPerformanceDto[];
}
