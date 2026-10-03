import { FormArray, FormBuilder, Validators } from '@angular/forms';
import { QuestionDto } from './question.models';

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
    options: formBuilder.array([newOptionGroup(formBuilder), newOptionGroup(formBuilder)]),
  });
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
