import { AbstractControl, FormArray, FormBuilder, ValidationErrors, Validators } from '@angular/forms';
import { QUESTION_LIMITS, QuestionDifficulty, QuestionDto } from './question.models';

/** One option row of the question form. `id` is the existing option's id when editing, and '' for a new option. */
function newOptionGroup(formBuilder: FormBuilder, id = '', text = '', pinned = false, correct = false) {
  // `correct` is the tick of a multiple-answer question; a single-answer one keeps its answer in the form's `correctIndex`.
  return formBuilder.nonNullable.group({ id: [id], text: [text, Validators.required], pinned: [pinned], correct: [correct] });
}

/** The form shared by creating and editing a question: its text, its options and which option is correct. */
export function createQuestionForm(formBuilder: FormBuilder) {
  return formBuilder.nonNullable.group(
    {
      text: ['', Validators.required],
      // Whether more than one option is correct. Then the options' own `correct` ticks are the answer.
      allowsMultiple: [false],
      // Which option is the right answer of a single-answer question, as a radio value; -1 until the author picks one.
      correctIndex: [-1],
      // '' means no difficulty; the topics are typed as one comma-separated line and split by {@link parseTopics}.
      difficulty: ['' as QuestionDifficulty | ''],
      topics: ['', topicsValidator],
      options: formBuilder.array([newOptionGroup(formBuilder), newOptionGroup(formBuilder)]),
    },
    { validators: [correctAnswerValidator] },
  );
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

/** Puts an existing question into the form, keeping each option's id so an edit names the options it changes. */
export function fillQuestionForm(form: QuestionForm, formBuilder: FormBuilder, question: QuestionDto): void {
  const options = form.controls.options as FormArray;
  options.clear();
  question.options.forEach((option) =>
    options.push(newOptionGroup(formBuilder, option.id, option.text, option.isPinned ?? false, option.isCorrect)),
  );
  form.controls.text.setValue(question.text);
  form.controls.allowsMultiple.setValue(question.allowsMultiple ?? false);
  form.controls.correctIndex.setValue(question.options.findIndex((option) => option.isCorrect));
  form.controls.difficulty.setValue(question.difficulty ?? '');
  form.controls.topics.setValue(question.topics.join(', '));
}

/** The difficulty and topics as a request sends them. */
export function toLabels(form: QuestionForm): { difficulty: QuestionDifficulty | null; topics: string[] } {
  const { difficulty, topics } = form.getRawValue();
  return { difficulty: difficulty || null, topics: parseTopics(topics) };
}

/** The options as a new question sends them: no ids, the chosen one marked correct. */
export function toNewOptions(form: QuestionForm): { text: string; isCorrect: boolean; isPinned: boolean }[] {
  return form.getRawValue().options.map((option, index) => ({ text: option.text, isCorrect: isCorrectOption(form, index), isPinned: option.pinned }));
}

/** The options as an edit sends them: an option the question already has is named by its id. */
export function toEditedOptions(form: QuestionForm): { id: string | null; text: string; isCorrect: boolean; isPinned: boolean }[] {
  return form.getRawValue().options.map((option, index) => ({
    id: option.id || null,
    text: option.text,
    isCorrect: isCorrectOption(form, index),
    isPinned: option.pinned,
  }));
}

/** Whether the question takes several correct answers, as the form says it. */
export function toAllowsMultiple(form: QuestionForm): boolean {
  return form.getRawValue().allowsMultiple;
}
