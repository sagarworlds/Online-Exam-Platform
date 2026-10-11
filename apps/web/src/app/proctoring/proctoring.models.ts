import type { MessageKey } from '../i18n/messages.en';

/** The views the review queue offers. The API reads the same names; a view is a filter, not a second list. */
export type RiskFlagFilterName = 'open' | 'reviewed' | 'dismissed' | 'all';

/** Where a flag stands: waiting for a reviewer, or decided by one. */
export type RiskFlagStatus = 'Open' | 'Reviewed' | 'Dismissed';

/** One signal of a flag, as it was judged: the value read, the rule it was judged against and the points it added. */
export interface RiskSignal {
  kind: string;
  /** The value read, or null when the signal could not be judged (the pace of an attempt with too few answers). */
  value: number | null;
  threshold: number;
  raisedWhen: 'AtLeast' | 'AtMost';
  weight: number;
  raised: boolean;
  points: number;
}

/** One flagged attempt in the review queue, with every signal that produced its score. */
export interface RiskFlag {
  id: string;
  attemptId: string;
  candidateId: string;
  /** The address the candidate was invited at; null when they are no longer on the roster. */
  candidateEmail: string | null;
  attemptNumber: number;
  score: number;
  maxScore: number;
  status: RiskFlagStatus;
  computedAtUtc: string;
  decidedAtUtc: string | null;
  decisionNote: string | null;
  signals: RiskSignal[];
}

/** One page of an exam's review queue. */
export interface RiskFlagQueue {
  examId: string;
  examName: string;
  filter: string;
  page: number;
  pageSize: number;
  /** How many flags match the filter across every page. */
  total: number;
  items: RiskFlag[];
  /** Whether attempts by candidates under 18 may be scored in this environment. Off until counsel's opinion is on record. */
  minorsScanEnabled: boolean;
  /** How many finished attempts were left out of scoring because the candidate was under 18. Zero when minors may be scanned. */
  excludedUnder18Attempts: number;
}

/** What a scan of an exam did. */
export interface RiskScanResult {
  examId: string;
  scored: number;
  flagged: number;
  keptDecided: number;
  /** How many finished attempts were not scored because the candidate was under 18. */
  excludedUnder18: number;
}

/** The longest note a reviewer may write; the API refuses a longer one. */
export const MAX_RISK_NOTE_LENGTH = 500;

/** The message that names each signal, by the name the API gives it. A name it does not know is shown as the API wrote it. */
export const RISK_SIGNAL_NAMES: Readonly<Record<string, MessageKey>> = {
  FocusDepartures: 'proctoring.signal.name.FocusDepartures',
  ClientChanges: 'proctoring.signal.name.ClientChanges',
  Invalidated: 'proctoring.signal.name.Invalidated',
  FastCompletion: 'proctoring.signal.name.FastCompletion',
  SharedWrongAnswers: 'proctoring.signal.name.SharedWrongAnswers',
};
