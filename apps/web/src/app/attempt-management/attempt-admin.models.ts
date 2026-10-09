import {
  AttemptRequestStatus,
  AttemptSummaryDto,
  AnswerVerdict,
  AttemptStatus,
  DisputeStatus,
  IssueCategory,
  ReviewOptionDto,
} from '../candidate/candidate.models';
import { MessageKey } from '../i18n/messages.en';

/** One enrolled candidate of an exam, with their attempts and whether another can be given, as staff see them. */
export interface ExamCandidateDto {
  candidateId: string;
  email: string;
  /** How many attempts they may make in all: the exam's limit per candidate, plus each extra attempt given. */
  attemptsAllowed: number;
  attemptsUsed: number;
  /**
   * Whether another extra attempt may be given now: they have used exactly the attempts they hold (not more, which only a lowered
   * limit allows), and the exam can still be started.
   */
  canGrant: boolean;
  /** Oldest first. */
  attempts: AttemptSummaryDto[];
  /** What they are allowed at this exam because of a disability or another need (FR-49); null or absent when nothing is. */
  accommodation?: Accommodation | null;
}

/** An alternate format of the exam page an accommodation can give (FR-49). */
export type AccommodationFormat = 'large_text' | 'high_contrast' | 'screen_reader';

/**
 * The formats staff can choose, in the order they are offered, with the message keys of what each is called and what it does, so the
 * choices read in the language the staff member chose.
 */
export const ACCOMMODATION_FORMATS: readonly {
  value: AccommodationFormat;
  label: MessageKey;
  hint: MessageKey;
}[] = [
  {
    value: 'large_text',
    label: 'admin.accommodation.format.large_text',
    hint: 'admin.accommodation.formatHint.large_text',
  },
  {
    value: 'high_contrast',
    label: 'admin.accommodation.format.high_contrast',
    hint: 'admin.accommodation.formatHint.high_contrast',
  },
  {
    value: 'screen_reader',
    label: 'admin.accommodation.format.screen_reader',
    hint: 'admin.accommodation.formatHint.screen_reader',
  },
];

/** The most extra time the API accepts, in minutes. */
export const MAX_ACCOMMODATION_MINUTES = 720;

/** The longest staff note the API accepts. */
export const MAX_ACCOMMODATION_NOTES = 500;

/** What a candidate is allowed at an exam because of a disability or another need, as staff see it (FR-49). */
export interface Accommodation {
  /** Minutes added to the candidate's deadline; 0 for none. */
  extraTimeMinutes: number;
  /** Whether the candidate may use a reader or scribe. */
  readerScribe: boolean;
  alternateFormats: AccommodationFormat[];
  /** The note staff wrote. Staff only: it is never sent to the candidate. */
  notes: string | null;
  updatedAtUtc: string;
}

/** The body of PUT .../candidates/{id}/accommodation: the whole accommodation, not a patch. */
export interface SetAccommodationRequest {
  extraTimeMinutes: number;
  readerScribe: boolean;
  alternateFormats: AccommodationFormat[];
  notes: string | null;
}

/** An exam's enrolled candidates and how each has got on. */
export interface ExamAttemptsDto {
  examId: string;
  examName: string;
  /** Whether nobody can start an attempt any more, so no extra attempt can be given. */
  windowClosed: boolean;
  /** How many attempts every candidate has before any extra is given, as the exam's author set it. */
  attemptsPerCandidate: number;
  candidates: ExamCandidateDto[];
}

/** One question on a candidate's paper; `drawn` means a draw rule picked it for this candidate. `text` is sanitized HTML. */
export interface AttemptPaperQuestionDto {
  id: string;
  text: string | null;
  drawn: boolean;
  /** The options in the order this candidate saw them, with the correct one and the candidate's choice marked; null if the bank no longer has the question. */
  options?: ReviewOptionDto[] | null;
  /** How the answer was marked; null until the attempt is submitted. */
  verdict?: AnswerVerdict | null;
  /** The marks the answer earned, which may be negative; null until the attempt is submitted. */
  marks?: number | null;
  allowsMultiple?: boolean;
  /** Whether the candidate typed the answer; such a question has no options. Absent from a paper from before text questions. */
  isTextAnswer?: boolean;
  /** What the candidate typed for a text question, or null when they typed nothing. */
  answerText?: string | null;
  /** The answers accepted for a text question, so staff can read the typed answer against them. */
  acceptedAnswers?: string[];
}

export interface AttemptPaperSectionDto {
  id: string;
  name: string;
  questions: AttemptPaperQuestionDto[];
}

/** Where an attempt was sat from at a moment (FR-26): staff only. */
export interface AttemptClientDto {
  /** The candidate's IP address as the server saw it; null when it could not be read. */
  ipAddress: string | null;
  /** The device signature the web app sent: a hash, to compare rather than read; null when none was sent. */
  deviceFingerprint: string | null;
  seenAtUtc: string;
  /** Where the attempt began, or a change from where it was. */
  reason: 'Started' | 'Changed';
}

/** The questions one attempt consisted of. */
export interface AttemptPaperDto {
  attemptId: string;
  number: number;
  hasDrawnQuestions: boolean;
  sections: AttemptPaperSectionDto[];
  /** Absent from an API that predates the marked paper. */
  status?: AttemptStatus;
  /** The marks scored and available; null until the attempt is submitted. */
  score?: number | null;
  maxScore?: number | null;
  /** Staff invalidated the attempt (FR-29): the candidate is not shown the score, staff still see what it was. */
  isInvalidated?: boolean;
}

/** The statuses the request queue can be filtered to, as the API spells them in the query. */
export type AttemptRequestFilterStatus = 'pending' | 'approved' | 'declined';

/** A candidate's request for another attempt as staff see it in the queue. */
export interface AttemptRequestRow {
  id: string;
  examId: string;
  /** Null when the exam can no longer be read. */
  examName: string | null;
  candidateId: string;
  /** The address the candidate was invited at; null when they are no longer enrolled. */
  candidateEmail: string | null;
  message: string | null;
  requestedAtUtc: string;
  status: AttemptRequestStatus;
  decidedAtUtc: string | null;
  decisionNote: string | null;
  /**
   * Whether the candidate was e-mailed the answer. Present only on the response to approving or declining; false means no mail
   * server is set up, it refused the message, or the candidate is no longer enrolled, so the administrator should tell them.
   */
  candidateNotified?: boolean | null;
}

/** The statuses the dispute queue can be filtered to, as the API spells them in the query. */
export type DisputeFilterStatus = 'open' | 'accepted' | 'rejected';

/** Which reported problems the staff queue lists (FR-42). */
export type IssueReportFilterStatus = 'open' | 'resolved';

/** The longest note the API accepts when resolving a reported problem. */
export const MAX_ISSUE_RESOLUTION_NOTE = 500;

/** A problem a candidate reported from inside an exam (FR-42), as staff see it in the queue. */
export interface IssueReportRow {
  id: string;
  examId: string;
  /** Null when the exam can no longer be read. */
  examName: string | null;
  attemptId: string;
  /** Which attempt this is for the candidate at the exam; null when it cannot be worked out. */
  attemptNumber: number | null;
  candidateId: string;
  /** The address the candidate was invited at; null when they are no longer enrolled. */
  candidateEmail: string | null;
  /** The question on screen when they reported, or null when the report is not about one. */
  questionId: string | null;
  /** That question as it is now, as sanitized HTML; null when there is none or the bank no longer has it. Show it with `[appMath]`, never as trusted markup. */
  questionText: string | null;
  category: IssueCategory;
  /** What the candidate wrote. */
  message: string;
  reportedAtUtc: string;
  status: IssueReportStatus;
  resolvedAtUtc: string | null;
  /** What staff said they did, when they said anything. */
  resolutionNote: string | null;
}

/** Whether a reported problem is waiting or has been dealt with. */
export type IssueReportStatus = 'Open' | 'Resolved';

/** The longest explanation the API accepts when rejecting a dispute; the candidate is shown it. */
export const MAX_DISPUTE_REJECTION_NOTE = 500;

/** A candidate's dispute of one question's answer key (FR-31), as staff see it in the queue. */
export interface DisputeRow {
  id: string;
  examId: string;
  /** Null when the exam can no longer be read. */
  examName: string | null;
  attemptId: string;
  /** Which attempt this is for the candidate at the exam; null when it cannot be worked out. */
  attemptNumber: number | null;
  candidateId: string;
  /** The address the candidate was invited at; null when they are no longer enrolled. */
  candidateEmail: string | null;
  questionId: string;
  /** The question as it is now, as sanitized HTML; null when the bank no longer has it. Show it with `[appMath]`, never as trusted markup. */
  questionText: string | null;
  /** Why the candidate thinks the answer key is wrong. */
  reason: string;
  raisedAtUtc: string;
  status: DisputeStatus;
  resolvedAtUtc: string | null;
  /** What staff said when rejecting, or the reason the answer key was corrected when the dispute was accepted. */
  resolutionNote: string | null;
}
