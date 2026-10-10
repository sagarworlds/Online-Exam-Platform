import { AbstractControl, FormArray, FormBuilder, FormControl, ValidationErrors, Validators } from '@angular/forms';
import { QUESTION_LIMITS, QuestionDifficulty, QuestionDto, QuestionLanguage, QuestionType } from './question.models';

/** One option row of the question form. `id` is the existing option's id when editing, and '' for a new option. */
function newOptionGroup(formBuilder: FormBuilder, id = '', text = '', pinned = false, correct = false) {
  // `correct` is the tick of a multiple-answer question; a single-answer one keeps its answer in the form's `correctIndex`.
  return formBuilder.nonNullable.group({ id: [id], text: [text, Validators.required], pinned: [pinned], correct: [correct] });
}

/**
 * One accepted answer of a text question. A blank row is not an error: the API leaves blank answers out, so the form does too, and only
 * the length of a row is checked here.
 */
export function newAcceptedAnswer(formBuilder: FormBuilder, text = ''): FormControl<string> {
  return formBuilder.nonNullable.control(text, [Validators.maxLength(QUESTION_LIMITS.maxAcceptedAnswerLength)]);
}

/** The form shared by creating and editing a question: its text, its type, its options or accepted answers, and which option is correct. */
export function createQuestionForm(formBuilder: FormBuilder) {
  const form = formBuilder.nonNullable.group(
    {
      text: ['', Validators.required],
      // The kind of question: a multiple-choice one (the default) has options, a text one has accepted answers instead.
      questionType: ['choice' as QuestionType],
      // Whether more than one option is correct. Then the options' own `correct` ticks are the answer.
      allowsMultiple: [false],
      // Which option is the right answer of a single-answer question, as a radio value; -1 until the author picks one.
      correctIndex: [-1],
      // '' means no difficulty; the topics are typed as one comma-separated line and split by {@link parseTopics}.
      difficulty: ['' as QuestionDifficulty | ''],
      // The language a new question is written in (FR-10); an edit leaves it alone, as another language is a translation.
      language: ['en' as QuestionLanguage],
      topics: ['', topicsValidator],
      // Why the correct answer is correct, shown to candidates in the answer review after release (FR-33); blank means none.
      explanation: ['', Validators.maxLength(QUESTION_LIMITS.maxExplanationLength)],
      options: formBuilder.array([newOptionGroup(formBuilder), newOptionGroup(formBuilder)]),
      acceptedAnswers: formBuilder.array([newAcceptedAnswer(formBuilder)]),
    },
    { validators: [answerKeyValidator] },
  );

  // Switching the type switches the other kind's rows off, so the hidden ones are neither checked nor sent.
  form.controls.questionType.valueChanges.subscribe(() => applyQuestionType(form));
  applyQuestionType(form);
  return form;
}

/**
 * Turns on the rows of the chosen kind of question and turns off the other kind's. A disabled row keeps what the author typed, so
 * switching back restores it, but it is left out of the validity check and of what is sent.
 */
function applyQuestionType(form: QuestionForm): void {
  const { options, acceptedAnswers } = form.controls;
  if (form.controls.questionType.value === 'text') {
    options.disable({ emitEvent: false });
    acceptedAnswers.enable({ emitEvent: false });
  } else {
    acceptedAnswers.disable({ emitEvent: false });
    options.enable({ emitEvent: false });
  }
  form.updateValueAndValidity({ emitEvent: false });
}

/** The rule about the answer, which depends on the kind of question: the options' correct ones, or the accepted answers. */
function answerKeyValidator(form: AbstractControl): ValidationErrors | null {
  return form.get('questionType')?.value === 'text' ? acceptedAnswersValidator(form) : correctAnswerValidator(form);
}

/**
 * The rules about which options are correct, as the API enforces them: a single-answer question needs one chosen, and a
 * multiple-answer one needs at least one ticked and at least one left unticked (a question nobody can get wrong is no question).
 */
function correctAnswerValidator(form: AbstractControl): ValidationErrors | null {
  if (form.get('allowsMultiple')?.value !== true) {
    return (form.get('correctIndex')?.value ?? -1) >= 0 ? null : { noCorrectAnswer: true };
  }

  const options = (form.get('options') as FormArray).controls;
  const correct = options.filter((option) => option.get('correct')?.value === true).length;
  return correct >= 1 && correct < options.length ? null : { badCorrectSet: true };
}

function acceptedAnswersValidator(form: AbstractControl): ValidationErrors | null {
  return acceptedAnswersErrors(acceptedAnswerValues(form));
}

/**
 * The rules about the accepted answers of a text question, as the API enforces them: at least one, no more than the API keeps, and no
 * two that would match the same typed answer. Blank answers are left out, as the API leaves them out. Shared by the question form and the
 * answer-key correction, so both refuse the same answers.
 */
export function acceptedAnswersErrors(answers: readonly string[]): ValidationErrors | null {
  const kept = answers.map((answer) => answer.trim()).filter((answer) => answer.length > 0);
  if (kept.length === 0) {
    return { noAcceptedAnswer: true };
  }
  if (kept.length > QUESTION_LIMITS.maxAcceptedAnswers) {
    return { tooManyAcceptedAnswers: true };
  }

  const comparable = kept.map(comparableAnswer);
  return new Set(comparable).size === comparable.length ? null : { duplicateAcceptedAnswer: true };
}

/** An answer in the form a typed answer is matched in: trimmed, inner spaces collapsed, lower case (as the API compares them). */
function comparableAnswer(answer: string): string {
  return answer.trim().replace(/\s+/g, ' ').toLowerCase();
}

/** The accepted-answer rows as they are typed, blank ones included. */
function acceptedAnswerValues(form: AbstractControl): string[] {
  return (form.get('acceptedAnswers') as FormArray<FormControl<string>>).controls.map((control) => control.value);
}

/** Which options are correct, whichever way the form holds it: the ticks of a multiple-answer question, or the one radio choice. */
function isCorrectOption(form: QuestionForm, index: number): boolean {
  const { allowsMultiple, correctIndex, options } = form.getRawValue();
  return allowsMultiple ? options[index].correct : index === correctIndex;
}

/**
 * Splits the typed topics line into topics: trimmed, lower case, no blanks, no repeats. The API applies the same rules, so
 * this only makes the form show what will be stored.
 */
export function parseTopics(line: string): string[] {
  const topics = line
    .split(',')
    .map((topic) => topic.trim().replace(/\s+/g, ' ').toLowerCase())
    .filter((topic) => topic.length > 0);
  return [...new Set(topics)];
}

/** Refuses a topics line the API would refuse, so the author hears about it before saving. */
function topicsValidator(control: AbstractControl<string>): ValidationErrors | null {
  const topics = parseTopics(control.value ?? '');
  if (topics.length > QUESTION_LIMITS.maxTopics) {
    return { tooManyTopics: true };
  }
  return topics.some((topic) => topic.length > QUESTION_LIMITS.maxTopicLength) ? { topicTooLong: true } : null;
}

export type QuestionForm = ReturnType<typeof createQuestionForm>;

/** An empty option row, for the fields component to add. */
export function newOption(formBuilder: FormBuilder) {
  return newOptionGroup(formBuilder);
}

/** Replaces the accepted answers with these, keeping at least one row, which is what an author sees first. */
export function setAcceptedAnswers(form: QuestionForm, formBuilder: FormBuilder, answers: readonly string[] = []): void {
  const rows = form.controls.acceptedAnswers;
  rows.clear();
  (answers.length > 0 ? answers : ['']).forEach((answer) => rows.push(newAcceptedAnswer(formBuilder, answer)));
  applyQuestionType(form);
}

/** Puts an existing question into the form, keeping each option's id so an edit names the options it changes. */
export function fillQuestionForm(form: QuestionForm, formBuilder: FormBuilder, question: QuestionDto): void {
  const options = form.controls.options as FormArray;
  options.clear();
  question.options.forEach((option) =>
    options.push(newOptionGroup(formBuilder, option.id, option.text, option.isPinned ?? false, option.isCorrect)),
  );
  setAcceptedAnswers(form, formBuilder, question.acceptedAnswers ?? []);
  form.controls.text.setValue(question.text);
  form.controls.questionType.setValue(question.isTextAnswer ? 'text' : 'choice');
  form.controls.allowsMultiple.setValue(question.allowsMultiple ?? false);
  form.controls.correctIndex.setValue(question.options.findIndex((option) => option.isCorrect));
  form.controls.difficulty.setValue(question.difficulty ?? '');
  form.controls.language.setValue(question.language ?? 'en');
  form.controls.topics.setValue(question.topics.join(', '));
  form.controls.explanation.setValue(question.explanation ?? '');
  applyQuestionType(form);
}

/** The difficulty and topics as a request sends them. */
export function toLabels(form: QuestionForm): { difficulty: QuestionDifficulty | null; topics: string[] } {
  const { difficulty, topics } = form.getRawValue();
  return { difficulty: difficulty || null, topics: parseTopics(topics) };
}

/** Whether the question is a text question, which the candidate answers by typing rather than by choosing options. */
export function toIsTextAnswer(form: QuestionForm): boolean {
  return form.getRawValue().questionType === 'text';
}

/** The explanation as a request sends it: trimmed, or null when the author wrote none. */
export function toExplanation(form: QuestionForm): string | null {
  const explanation = form.getRawValue().explanation.trim();
  return explanation.length > 0 ? explanation : null;
}

/** The accepted answers as a request sends them: trimmed, with blank ones left out; none for a multiple-choice question. */
export function toAcceptedAnswers(form: QuestionForm): string[] {
  if (!toIsTextAnswer(form)) {
    return [];
  }
  return acceptedAnswerValues(form).map((answer) => answer.trim()).filter((answer) => answer.length > 0);
}

/** The options as a new question sends them: no ids, the chosen one marked correct. A text question has none. */
export function toNewOptions(form: QuestionForm): { text: string; isCorrect: boolean; isPinned: boolean }[] {
  if (toIsTextAnswer(form)) {
    return [];
  }
  return form.getRawValue().options.map((option, index) => ({ text: option.text, isCorrect: isCorrectOption(form, index), isPinned: option.pinned }));
}

/** The options as an edit sends them: an option the question already has is named by its id. A text question has none. */
export function toEditedOptions(form: QuestionForm): { id: string | null; text: string; isCorrect: boolean; isPinned: boolean }[] {
  if (toIsTextAnswer(form)) {
    return [];
  }
  return form.getRawValue().options.map((option, index) => ({
    id: option.id || null,
    text: option.text,
    isCorrect: isCorrectOption(form, index),
    isPinned: option.pinned,
  }));
}

/** Whether the question takes several correct answers, as the form says it. A text question never does. */
export function toAllowsMultiple(form: QuestionForm): boolean {
  return !toIsTextAnswer(form) && form.getRawValue().allowsMultiple;
}
