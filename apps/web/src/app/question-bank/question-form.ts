import { AbstractControl, FormArray, FormBuilder, ValidationErrors, Validators } from '@angular/forms';
import { QUESTION_LIMITS, QuestionDifficulty, QuestionDto } from './question.models';

/** One option row of the question form. `id` is the existing option's id when editing, and '' for a new option. */
function newOptionGroup(formBuilder: FormBuilder, id = '', text = '', pinned = false) {
  return formBuilder.nonNullable.group({ id: [id], text: [text, Validators.required], pinned: [pinned] });
}

/** The form shared by creating and editing a question: its text, its options and which option is correct. */
export function createQuestionForm(formBuilder: FormBuilder) {
  return formBuilder.nonNullable.group({
    text: ['', Validators.required],
    // Which option is the right answer, as a radio value; -1 until the author picks one.
    correctIndex: [-1, Validators.min(0)],
    // '' means no difficulty; the topics are typed as one comma-separated line and split by {@link parseTopics}.
    difficulty: ['' as QuestionDifficulty | ''],
    topics: ['', topicsValidator],
    options: formBuilder.array([newOptionGroup(formBuilder), newOptionGroup(formBuilder)]),
  });
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
  question.options.forEach((option) => options.push(newOptionGroup(formBuilder, option.id, option.text, option.isPinned ?? false)));
  form.controls.text.setValue(question.text);
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
  const { options, correctIndex } = form.getRawValue();
  return options.map((option, index) => ({ text: option.text, isCorrect: index === correctIndex, isPinned: option.pinned }));
}

/** The options as an edit sends them: an option the question already has is named by its id. */
export function toEditedOptions(form: QuestionForm): { id: string | null; text: string; isCorrect: boolean; isPinned: boolean }[] {
  const { options, correctIndex } = form.getRawValue();
  return options.map((option, index) => ({
    id: option.id || null,
    text: option.text,
    isCorrect: index === correctIndex,
    isPinned: option.pinned,
  }));
}
