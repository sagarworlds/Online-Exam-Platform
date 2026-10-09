import { Words } from '../../i18n/i18n.service';
import { ExamConfigDto } from '../exam.models';

/**
 * One short line per exam setting, shown on the editor's closed setting rows (FR-11), so an author can read what the exam is set to
 * without opening any of them. Each line is worded from the exam's settings as they are now; nothing here changes an exam.
 * The lines stay in the editor, because the rows that show them belong to the editor page.
 */
export class ExamSettingsSummary {
  constructor(private readonly words: Words) {}

  /** When candidates see which answers were right: straight after submitting, from a set time, or held back. */
  release(config: ExamConfigDto): string {
    if (config.resultReleaseMode === 'Scheduled') {
      return this.words.t('exams.summary.release.scheduled');
    }

    if (config.resultReleaseMode === 'Manual') {
      // A manual release with a time has been released already; without one it is still held back.
      return this.words.t(
        config.resultReleaseTime ? 'exams.summary.release.released' : 'exams.summary.release.held',
      );
    }

    return this.words.t('exams.summary.release.instant');
  }

  /** Which of the two shuffles are on. The order an attempt shows is fixed by these, so a row says which parts move. */
  shuffle(config: ExamConfigDto): string {
    if (config.shuffleQuestions && config.shuffleOptions) {
      return this.words.t('exams.summary.shuffle.both');
    }

    if (config.shuffleQuestions) {
      return this.words.t('exams.summary.shuffle.questions');
    }

    if (config.shuffleOptions) {
      return this.words.t('exams.summary.shuffle.options');
    }

    return this.words.t('exams.summary.shuffle.none');
  }

  /** The three marks at once, in the order the marking card asks for them. */
  marks(config: ExamConfigDto): string {
    const scheme = config.markingScheme;
    return this.words.t('exams.summary.marks', {
      correct: scheme.correctMarks,
      incorrect: scheme.incorrectMarks,
      unattempted: scheme.unattemptedMarks,
    });
  }

  /** How many attempts each candidate has, in words. */
  attempts(config: ExamConfigDto): string {
    return this.words.plural('exams.summary.attempts', config.maxAttempts);
  }

  /** Whether copying and printing are turned off; the exam keeps protection on unless it says otherwise, as the API does. */
  copying(config: ExamConfigDto): string {
    return config.contentProtection === false
      ? this.words.t('exams.summary.copying.allowed')
      : this.words.t('exams.summary.copying.blocked');
  }

  /** How many times a candidate may leave the exam page before the attempt ends, or that the page is not watched. */
  leaving(config: ExamConfigDto): string {
    const limit = config.focusViolationLimit ?? 0;
    return limit > 0
      ? this.words.plural('exams.summary.leave', limit)
      : this.words.t('exams.summary.leaveNone');
  }
}
