import { FormBuilder } from '@angular/forms';
import { createQuestionForm, fillQuestionForm, toExplanation } from './question-form';
import { QUESTION_LIMITS, QuestionDto } from './question.models';

const question = (explanation?: string | null): QuestionDto => ({
  id: 'q1',
  text: '<p>Capital of France?</p>',
  options: [
    { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
  ],
  createdBy: 'author',
  createdAtUtc: '2026-10-09T09:00:00Z',
  chapterId: null,
  chapterTitle: null,
  bookId: null,
  bookName: null,
  classId: null,
  className: null,
  usage: { examCount: 0, examNames: [], answered: false },
  difficulty: null,
  topics: [],
  allowsMultiple: false,
  explanation,
});

/** A blank form, as the author starts a new question with. */
const blankForm = () => createQuestionForm(new FormBuilder());

describe('the explanation of a question (FR-33)', () => {
  it('is sent as null when the author wrote none, so a question has no explanation rather than an empty one', () => {
    expect(toExplanation(blankForm())).toBeNull();
  });

  it('is sent trimmed, and blank whitespace counts as none', () => {
    const form = blankForm();
    form.controls.explanation.setValue('  Paris has been the capital for centuries.\n');
    expect(toExplanation(form)).toBe('Paris has been the capital for centuries.');

    form.controls.explanation.setValue('   ');
    expect(toExplanation(form)).toBeNull();
  });

  it('is loaded into the form when an existing question is edited, so saving it again keeps it', () => {
    const form = blankForm();
    fillQuestionForm(form, new FormBuilder(), question('Because Paris is the seat of government.'));

    expect(form.controls.explanation.value).toBe('Because Paris is the seat of government.');
    expect(toExplanation(form)).toBe('Because Paris is the seat of government.');
  });

  it('loads as blank for a question that has none', () => {
    const form = blankForm();
    fillQuestionForm(form, new FormBuilder(), question(null));

    expect(form.controls.explanation.value).toBe('');
    expect(toExplanation(form)).toBeNull();
  });

  it('is refused by the form when it is longer than the API accepts', () => {
    const form = blankForm();
    form.controls.explanation.setValue('x'.repeat(QUESTION_LIMITS.maxExplanationLength + 1));

    expect(form.controls.explanation.hasError('maxlength')).toBe(true);
  });
});
