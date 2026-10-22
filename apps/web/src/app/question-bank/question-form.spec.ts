import { FormBuilder } from '@angular/forms';
import {
  answersToCompare,
  createQuestionForm,
  fillQuestionForm,
  setAcceptedAnswers,
  toAcceptedAnswers,
  toAllowsMultiple,
  toEditedOptions,
  toIsTextAnswer,
  toNewOptions,
} from './question-form';
import { CreateQuestionRequest, QUESTION_LIMITS, QuestionDto } from './question.models';

const base: QuestionDto = {
  id: 'q1',
  text: '<p>Capital of France?</p>',
  options: [],
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
};

/** A text question with the given accepted answers, filled into a fresh form the way the editor loads one. */
const textQuestion = (acceptedAnswers: string[]): QuestionDto => ({
  ...base,
  isTextAnswer: true,
  acceptedAnswers,
});

/** A form holding a text question with the given accepted answers, as the author would enter it. */
function textForm(answers: string[]) {
  const formBuilder = new FormBuilder();
  const form = createQuestionForm(formBuilder);
  form.controls.text.setValue('<p>Capital of France?</p>');
  form.controls.questionType.setValue('text');
  setAcceptedAnswers(form, formBuilder, answers);
  return form;
}

describe('question form: the length of an option', () => {
  /** The first option's text control, holding that many characters. */
  const optionOfLength = (length: number) => {
    const form = createQuestionForm(new FormBuilder());
    const text = form.controls.options.controls[0].controls.text;
    text.setValue('x'.repeat(length));
    return text;
  };

  it('takes an option of exactly the longest the API keeps', () => {
    expect(optionOfLength(QUESTION_LIMITS.maxOptionTextLength).valid).toBe(true);
  });

  it('refuses an option longer than the API keeps', () => {
    expect(optionOfLength(QUESTION_LIMITS.maxOptionTextLength + 1).hasError('maxlength')).toBe(true);
  });
});

describe('question form: the kind of question', () => {
  it('starts as a multiple-choice question, with its options and no accepted answers', () => {
    const form = createQuestionForm(new FormBuilder());

    expect(form.controls.questionType.value).toBe('choice');
    expect(form.controls.options.enabled).toBe(true);
    expect(form.controls.acceptedAnswers.disabled).toBe(true);
    expect(toIsTextAnswer(form)).toBe(false);
    expect(toAcceptedAnswers(form)).toEqual([]);
  });

  it('switches to accepted answers for a text question, and back to options with the options kept', () => {
    const form = createQuestionForm(new FormBuilder());
    form.controls.options.controls[0].controls.text.setValue('Paris');

    form.controls.questionType.setValue('text');
    expect(form.controls.options.disabled).toBe(true);
    expect(form.controls.acceptedAnswers.enabled).toBe(true);
    expect(toNewOptions(form)).toEqual([]);
    expect(toEditedOptions(form)).toEqual([]);

    form.controls.questionType.setValue('choice');
    expect(form.controls.options.enabled).toBe(true);
    expect(form.controls.options.controls[0].controls.text.value).toBe('Paris');
  });

  it('never marks a text question as several-answers', () => {
    const form = textForm(['Paris']);
    form.controls.allowsMultiple.setValue(true);

    expect(toAllowsMultiple(form)).toBe(false);
  });
});

describe('question form: accepted answers', () => {
  it('needs at least one accepted answer, and ignores blank rows once there is one', () => {
    const form = textForm([]);
    expect(form.errors).toEqual({ noAcceptedAnswer: true });

    setAcceptedAnswers(form, new FormBuilder(), ['  Paris ', '   ']);

    expect(form.valid).toBe(true);
    expect(toAcceptedAnswers(form)).toEqual(['Paris']);
  });

  it('refuses two accepted answers that match the same typed answer', () => {
    expect(textForm(['Paris', '  paris ']).errors).toEqual({ duplicateAcceptedAnswer: true });
  });

  it('refuses more accepted answers than the API keeps', () => {
    const answers = Array.from(
      { length: QUESTION_LIMITS.maxAcceptedAnswers + 1 },
      (_, i) => `answer ${i}`,
    );

    expect(textForm(answers).errors).toEqual({ tooManyAcceptedAnswers: true });
  });

  it('refuses an accepted answer longer than the API keeps, row by row', () => {
    const form = textForm(['x'.repeat(QUESTION_LIMITS.maxAcceptedAnswerLength + 1)]);

    expect(form.controls.acceptedAnswers.controls[0].hasError('maxlength')).toBe(true);
    expect(form.valid).toBe(false);
  });

  it('takes an accepted answer of exactly the longest the API keeps', () => {
    const form = textForm(['x'.repeat(QUESTION_LIMITS.maxAcceptedAnswerLength)]);

    expect(form.controls.acceptedAnswers.controls[0].valid).toBe(true);
    expect(form.valid).toBe(true);
  });

  it('sends the accepted answers trimmed, with the blank ones left out', () => {
    expect(toAcceptedAnswers(textForm(['  Paris  ', '', 'City of Paris']))).toEqual([
      'Paris',
      'City of Paris',
    ]);
  });
});

describe('question form: loading a question for editing', () => {
  it('loads a text question with its accepted answers and no options', () => {
    const form = createQuestionForm(new FormBuilder());

    fillQuestionForm(form, new FormBuilder(), textQuestion(['Paris', 'City of Paris']));

    expect(toIsTextAnswer(form)).toBe(true);
    expect(toAcceptedAnswers(form)).toEqual(['Paris', 'City of Paris']);
    expect(form.controls.options.disabled).toBe(true);
    expect(form.valid).toBe(true);
  });

  it('loads a multiple-choice question with its options and no accepted answers', () => {
    const form = createQuestionForm(new FormBuilder());
    const multiple: QuestionDto = {
      ...base,
      options: [
        { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
        { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
      ],
    };

    fillQuestionForm(form, new FormBuilder(), multiple);

    expect(toIsTextAnswer(form)).toBe(false);
    expect(toAcceptedAnswers(form)).toEqual([]);
    expect(toNewOptions(form).map((option) => option.text)).toEqual(['Paris', 'Rome']);
  });

  describe('the answers the bank compares for a repeated question (FR-9)', () => {
    const request = (extra: Partial<CreateQuestionRequest>): CreateQuestionRequest => ({
      text: 'Capital of France?', options: [], chapterId: null, difficulty: null, topics: [], allowsMultiple: false, ...extra,
    });

    it('are the option texts of a multiple-choice question', () => {
      const asked = request({ options: [{ text: 'Paris', isCorrect: true, isPinned: false }, { text: 'Rome', isCorrect: false, isPinned: false }] });

      expect(answersToCompare(asked)).toEqual(['Paris', 'Rome']);
    });

    it('are the accepted answers of a text question, which has no options', () => {
      expect(answersToCompare(request({ isTextAnswer: true, acceptedAnswers: ['Paris', 'paris france'] }))).toEqual(['Paris', 'paris france']);
    });

    it('are none for a text question that sent no accepted answers', () => {
      expect(answersToCompare(request({ isTextAnswer: true }))).toEqual([]);
    });
  });
});
