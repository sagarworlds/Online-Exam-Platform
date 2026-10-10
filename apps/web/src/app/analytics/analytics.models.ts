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

/** One question's row in an exam's item analysis (FR-37). */
export interface ItemRowDto {
  questionId: string;
  /** Where the question sits in the exam, from 1. */
  position: number;
  /** A plain-text preview of the question, shortened for the table. */
  text: string;
  /** How many counted candidates had the question on their paper. */
  attempts: number;
  correctCount: number;
  /** The share of those candidates who answered correctly, from 0 to 1; null while below the minimum cohort size. */
  difficulty: number | null;
  /** The upper group's share correct less the lower group's, from -1 to 1; null while withheld. */
  discrimination: number | null;
}

/** An exam's item analysis (FR-37): the questions in order and the settings they were worked out under. */
export interface ExamItemAnalysisDto {
  examId: string;
  examName: string;
  /** Whether the author has released the results. While false, no attempt counts and the questions list is empty. */
  resultsReleased: boolean;
  candidateCount: number;
  minimumCohortSize: number;
  groupSize: number;
  questions: ItemRowDto[];
}

/** A candidate's own performance across the exams they sat (FR-36). */
export interface CandidateAnalyticsDto {
  resultCount: number;
  trend: ScorePointDto[];
  /** Weakest accuracy first. */
  sections: SectionPerformanceDto[];
}
