export type ExamStatus = 'Draft' | 'Published' | 'Active' | 'Closed' | 'Archived';
export type ResultReleaseMode = 'Automatic' | 'Manual' | 'Scheduled';
export type MarkingScheme = 'Standard' | 'Custom';

export interface ExamConfigDto {
  totalTimeSeconds?: number;
  shuffleQuestions: boolean;
  shuffleOptions: boolean;
  sectionLockEnabled: boolean;
  calculatorAllowed: boolean;
  scratchpadAllowed: boolean;
  maxAttempts: number;
  maxRetakes: number;
  resultReleaseMode: ResultReleaseMode;
  resultReleaseTime?: Date;
  markingScheme: MarkingScheme;
}

export interface ExamDto {
  id: string;
  seriesId?: string;
  name: string;
  description?: string;
  status: ExamStatus;
  config: ExamConfigDto;
  scheduledStartTime: Date;
  scheduledEndTime: Date;
  lateEntryDeadline?: Date;
  timeZone: string;
  createdBy: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateExamRequest {
  /** Null (or omitted) creates a standalone exam; the API rejects an empty string. */
  seriesId?: string | null;
  name: string;
  description?: string;
}

export interface SectionDto {
  id: string;
  examId: string;
  title: string;
  description?: string;
  sequenceNumber: number;
  maxScore: number;
}

export interface ExamQuestionDto {
  id: string;
  sectionId: string;
  text: string;
  type: 'MultipleChoice' | 'ShortAnswer' | 'Essay';
  options?: string[];
  correctAnswer?: string;
  score: number;
  sequenceNumber: number;
}
