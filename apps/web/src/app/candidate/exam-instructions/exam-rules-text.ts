import { Words, englishWords } from '../../i18n/i18n.service';
import { ExamRulesDto, MyExamDto } from '../candidate.models';

/** "1 mark", "2 marks", "0.5 marks": the number as the exam sets it, without a spurious ".0". */
export function marks(value: number, words: Words = englishWords): string {
  return words.plural('marks', Math.abs(value));
}

/** What a wrong or unanswered question does to the score, in a sentence that says so plainly. */
function effect(kind: 'wrong' | 'skipped', value: number, words: Words): string {
  if (value === 0) {
    return words.t(`rules.${kind}.zero`);
  }

  return words.t(value < 0 ? `rules.${kind}.cost` : `rules.${kind}.earn`, { marks: marks(value, words) });
}

/** The sentences that explain how an exam is marked. */
export function markingRules(rules: ExamRulesDto, words: Words = englishWords): string[] {
  const lines = [
    words.t('rules.correct', { marks: marks(rules.correctMarks, words) }),
    effect('wrong', rules.incorrectMarks, words),
    effect('skipped', rules.unattemptedMarks, words),
  ];

  if (rules.partialCredit) {
    lines.push(words.t('rules.partial'));
  }

  return lines;
}

/** How long the candidate has, in the words the instructions use. */
export function timeAllowed(exam: Pick<MyExamDto, 'durationSeconds'>, words: Words = englishWords): string {
  if (exam.durationSeconds === null) {
    return words.t('time.untilClose');
  }

  return words.plural('time.minutes', Math.round(exam.durationSeconds / 60));
}

/**
 * Everything a candidate is told before starting, as short sentences in the order they matter. Only rules that are true of this
 * exam are stated: marking and section lock come from the exam's own settings.
 */
export function instructionLines(exam: MyExamDto, words: Words = englishWords): string[] {
  const lines: string[] = [];

  lines.push(exam.durationSeconds === null ? words.t('rules.untilClose') : words.t('rules.timeLimit', { time: timeAllowed(exam, words) }));
  lines.push(words.t('rules.serverTimer'));
  lines.push(words.t('rules.autosave'));

  if (exam.rules !== null) {
    lines.push(...markingRules(exam.rules, words));
    lines.push(exam.rules.sectionLock ? words.plural('rules.sectionLock', exam.rules.sectionCount) : words.t('rules.freeMove'));
  }

  // What is turned off, recorded and watched is worded by the server from the exam's own settings (FR-46), so the candidate is told
  // exactly what is collected and the wording cannot drift from it. An older API that sends none says nothing here.
  lines.push(...(exam.rules?.proctoringNotice ?? []));

  lines.push(words.t('rules.sameDevice'));
  lines.push(words.t('rules.submitEarly'));

  if (exam.attemptsAllowed > 1) {
    lines.push(words.t('rules.attempts', { allowed: exam.attemptsAllowed, used: exam.attemptsUsed, next: exam.attemptsUsed + 1 }));
  }

  return lines;
}
