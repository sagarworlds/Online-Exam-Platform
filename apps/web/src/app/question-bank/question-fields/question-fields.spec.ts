import { Component, inject, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { createQuestionForm, fillQuestionForm, toEditedOptions, toNewOptions } from '../question-form';
import { QuestionDto } from '../question.models';
import { QuestionFields } from './question-fields';

@Component({
  imports: [ReactiveFormsModule, QuestionFields],
  template: `<form [formGroup]="form"><app-question-fields [wordingOnly]="wordingOnly()" /></form>`,
})
class Host {
  readonly formBuilder = inject(FormBuilder);
  readonly form = createQuestionForm(this.formBuilder);
  readonly wordingOnly = signal(false);
}

const QUESTION: QuestionDto = {
  id: 'q1', text: '<p>Capital of France?</p>', createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapterId: null, chapterTitle: null, bookId: null, bookName: null,
  usage: { examCount: 0, examNames: [], answered: false },
  options: [
    { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
    { id: 'o3', text: 'Oslo', isCorrect: false, isPinned: false },
  ],
};

describe('QuestionFields', () => {
  let fixture: ComponentFixture<Host>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  });

  const texts = () => Array.from(root.querySelectorAll<HTMLInputElement>('input[type="text"]')).map((input) => input.value);
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const removeButtons = () => Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement[];

  it('starts with two empty options and no correct one chosen', () => {
    expect(texts()).toEqual(['', '']);
    expect(fixture.componentInstance.form.controls.correctIndex.value).toBe(-1);
  });

  it('lets the author keep an option in place, and sends that with the options', () => {
    const inputs = root.querySelectorAll<HTMLInputElement>('input[type="text"]');
    inputs[0].value = 'Rome';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = 'None of the above';
    inputs[1].dispatchEvent(new Event('input'));

    const pins = root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    expect(pins).toHaveLength(2);
    pins[1].click();

    expect(toNewOptions(fixture.componentInstance.form).map((o) => o.isPinned)).toEqual([false, true]);
  });

  it('works on the page’s own form: what is typed and chosen is in it', () => {
    const inputs = root.querySelectorAll<HTMLInputElement>('input[type="text"]');
    inputs[0].value = 'Rome';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = 'Paris';
    inputs[1].dispatchEvent(new Event('input'));
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));

    expect(toNewOptions(fixture.componentInstance.form)).toEqual([
      { text: 'Rome', isCorrect: false, isPinned: false },
      { text: 'Paris', isCorrect: true, isPinned: false },
    ]);
  });

  it('adds options up to six and removes them down to two', () => {
    for (let i = 0; i < 6; i++) {
      button('Add option').click();
      fixture.detectChanges();
    }
    expect(texts().length).toBe(6);
    expect(button('Add option').disabled).toBe(true);

    for (let i = 0; i < 6; i++) {
      removeButtons()[0].click();
      fixture.detectChanges();
    }
    expect(texts().length).toBe(2);
    expect(removeButtons().every((b) => b.disabled)).toBe(true);
  });

  it('keeps the chosen answer on the same option when an earlier option is removed, and clears it when that option goes', () => {
    // Four options, so there is room to remove two and still stay at the minimum of two.
    for (let i = 0; i < 2; i++) {
      button('Add option').click();
      fixture.detectChanges();
    }
    const { correctIndex } = fixture.componentInstance.form.controls;

    correctIndex.setValue(2);
    removeButtons()[0].click();
    fixture.detectChanges();
    expect(correctIndex.value).toBe(1);

    removeButtons()[1].click();
    fixture.detectChanges();
    expect(correctIndex.value).toBe(-1);
  });

  describe('when candidates have answered the question', () => {
    beforeEach(() => {
      fillQuestionForm(fixture.componentInstance.form, fixture.componentInstance.formBuilder, QUESTION);
      fixture.componentInstance.wordingOnly.set(true);
      fixture.detectChanges();
    });

    it('locks the correct option and the option list, and says why', () => {
      expect(Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).every((radio) => radio.disabled)).toBe(true);
      expect(button('Add option').disabled).toBe(true);
      expect(removeButtons().every((b) => b.disabled)).toBe(true);
      expect(root.querySelector('.question-lock-note')?.textContent).toContain('only its wording can change');
    });

    it('still lets the wording of every option be corrected', () => {
      const first = root.querySelector<HTMLInputElement>('input[type="text"]') as HTMLInputElement;
      expect(first.disabled).toBe(false);
      first.value = 'Paris, France';
      first.dispatchEvent(new Event('input'));

      expect(toEditedOptions(fixture.componentInstance.form)[0]).toEqual({ id: 'o1', text: 'Paris, France', isCorrect: true, isPinned: false });
    });

    it('does not show the lock note for a question nobody has answered', () => {
      fixture.componentInstance.wordingOnly.set(false);
      fixture.detectChanges();

      expect(root.querySelector('.question-lock-note')).toBeNull();
      expect(button('Add option').disabled).toBe(false);
    });
  });
});

describe('question form helpers', () => {
  const formBuilder = new FormBuilder();

  it('fills the form from a question, keeping every option id and the correct choice', () => {
    const form = createQuestionForm(formBuilder);

    fillQuestionForm(form, formBuilder, QUESTION);

    expect(form.getRawValue().text).toBe('<p>Capital of France?</p>');
    expect(form.getRawValue().correctIndex).toBe(0);
    expect(toEditedOptions(form)).toEqual([
      { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
      { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
      { id: 'o3', text: 'Oslo', isCorrect: false, isPinned: false },
    ]);
  });

  it('sends an option that was added while editing without an id, and a new question’s options without ids at all', () => {
    const form = createQuestionForm(formBuilder);
    fillQuestionForm(form, formBuilder, QUESTION);
    (form.controls.options as unknown as { push(c: unknown): void }).push(formBuilder.nonNullable.group({ id: [''], text: ['Madrid'], pinned: [false] }));

    expect(toEditedOptions(form)[3]).toEqual({ id: null, text: 'Madrid', isCorrect: false, isPinned: false });
    expect(toNewOptions(form)[0]).toEqual({ text: 'Paris', isCorrect: true, isPinned: false });
  });
});
