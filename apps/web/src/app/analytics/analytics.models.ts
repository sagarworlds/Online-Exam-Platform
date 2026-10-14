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
  /** How long the counted candidates spent on the question, and whether its average is shown (FR-37). */
  time: ItemTimeDto;
  /** How far the counted candidates got to the question, and how many stopped at it (FR-37). */
  dropOff: ItemDropOffDto;
}

/**
 * Why a question's average time is shown or withheld (FR-37). The values are the API's enum names, as the API writes them: a withheld
 * average is never read as zero.
 */
export type ItemTimeStatus = 'Shown' | 'BelowMinimumCohort' | 'NoTimes';

/** How long the counted candidates spent on one question (FR-37). */
export interface ItemTimeDto {
  /** How many counted candidates have a reported time for the question; a candidate with none is not counted as zero. */
  candidatesTimed: number;
  /** The average time in seconds, to one decimal place; null unless the status is Shown. */
  averageSeconds: number | null;
  status: ItemTimeStatus;
}

/** How far the counted candidates got to one question, and where they stopped (FR-37 drop-off points). */
export interface ItemDropOffDto {
  /** How many counted candidates got as far as the question: they answered it or one after it. */
  reached: number;
  /** How many of them stopped at this question: the clock ended their attempt with later questions still unanswered. */
  droppedOff: number;
  /** The share of those who reached it and stopped here, from 0 to 1; null while fewer than the minimum cohort size reached it. */
  rate: number | null;
}

/** The average time per question across an exam's counted candidates (FR-37). */
export interface ExamTimeSummaryDto {
  /** How many question answers across the counted candidates have a reported time. */
  questionsTimed: number;
  /** The average over those timed answers, in seconds, to one decimal place; null when none was timed. */
  averageSeconds: number | null;
}

/** One band of an exam's score distribution (FR-37), in percent of the marks on each paper. */
export interface ScoreBandDto {
  /** The lowest percentage the band holds, inclusive; null for the bottom band, which also takes every score below ten percent. */
  fromPercent: number | null;
  /** The percentage the band stops below, exclusive; null for the top band, which also takes full marks and anything above. */
  toPercent: number | null;
  /** How many candidates' best results fall in the band. */
  count: number;
}

/** An exam's candidate scores (FR-37): each candidate's best result as a percentage of their paper's marks, with the mean, median and distribution. */
export interface ExamScoreSummaryDto {
  /** How many candidates' results the figures were worked from. */
  count: number;
  /** The mean percentage, to two decimals; null when no candidate had marks to earn. */
  meanPercentOfMarks: number | null;
  /** The median percentage, to two decimals; null when no candidate had marks to earn. */
  medianPercentOfMarks: number | null;
  /** The bands from the lowest; empty while the results are held. */
  distribution: ScoreBandDto[];
}

/** An exam's item analysis (FR-37): the questions in order, the settings they were worked out under, and the candidates' scores. */
export interface ExamItemAnalysisDto {
  examId: string;
  examName: string;
  /** Whether the author has released the results. While false, no attempt counts and the questions list is empty. */
  resultsReleased: boolean;
  candidateCount: number;
  minimumCohortSize: number;
  groupSize: number;
  questions: ItemRowDto[];
  scores: ExamScoreSummaryDto;
  /** The average time per question across the counted candidates (FR-37); with no figures while the results are held. */
  time: ExamTimeSummaryDto;
}

/** How the candidate's answers fell under one topic label, across every released result (FR-36). */
export interface TopicPerformanceDto {
  name: string;
  /** How many questions the candidate sat that carry the label; a question with two labels counts under both. */
  questionCount: number;
  correctCount: number;
  wrongCount: number;
  partialCount: number;
  unansweredCount: number;
  /** The share of answered questions under the topic that were fully correct, in percent; null when none was answered. */
  accuracy: number | null;
}

/** How long the candidate spent on the questions of one section name (FR-36). */
export interface SectionTimeDto {
  name: string;
  questionsTimed: number;
  /** The average time per timed question, in seconds; null when none was timed. */
  averageSeconds: number | null;
}

/** How long the candidate spent per question, overall and by section (FR-36). */
export interface TimePerQuestionDto {
  questionsTimed: number;
  /** The average time per timed question, in seconds; null when no question was timed. */
  averageSeconds: number | null;
  sections: SectionTimeDto[];
}

/** A candidate's own performance across the exams they sat (FR-36). */
export interface CandidateAnalyticsDto {
  resultCount: number;
  trend: ScorePointDto[];
  /** Weakest accuracy first. */
  sections: SectionPerformanceDto[];
  /** Weakest accuracy first. */
  topics: TopicPerformanceDto[];
  /** Null when the time per question is withheld from the candidate (FR-36, section 7): no figure is shown, not even a zero. */
  time: TimePerQuestionDto | null;
}
