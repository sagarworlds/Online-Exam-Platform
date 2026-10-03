import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { QuestionDto } from '../question.models';
import { QuestionEdit } from './question-edit';

const isGet = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/q1');
const isPut = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/questions/q1');

const question = (overrides: Partial<QuestionDto> = {}): QuestionDto => ({
  id: 'q1', text: '<p>Capital of France?</p>', createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapterId: 'c1', chapterTitle: 'Algebra', bookId: 'b1', bookName: 'Maths',
  usage: { examCount: 0, examNames: [], answered: false },
  options: [
    { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
  ],
  ...overrides,
});

describe('QuestionEdit', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<QuestionEdit>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionEdit],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'q1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(loaded: QuestionDto = question()) {
    fixture = TestBed.createComponent(QuestionEdit);
    fixture.detectChanges();
    httpMock.expectOne(isGet).flush(loaded);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const texts = () => Array.from(root.querySelectorAll<HTMLInputElement>('input[type="text"]')).map((input) => input.value);
  const type = (index: number, value: string) => {
    const input = root.querySelectorAll<HTMLInputElement>('input[type="text"]')[index];
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const save = () => root.querySelector('button.primary') as HTMLButtonElement;
  const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

  it('shows the question as it is stored, with where it is filed', () => {
    open();

    expect(texts()).toEqual(['Paris', 'Rome']);
    expect(root.querySelector('.question-card__where')?.textContent).toContain('Maths');
    expect(root.querySelector('.question-card__where')?.textContent).toContain('Algebra');
    expect(root.querySelector('.question-lock-note')).toBeNull();
  });

  it('sends the whole edit, naming the options it keeps by id and the new one without an id', () => {
    open();
    type(0, 'Paris, France');
    (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add option') as HTMLButtonElement).click();
    fixture.detectChanges();
    type(2, 'Madrid');

    submit();

    const put = httpMock.expectOne(isPut);
    expect(put.request.body).toEqual({
      text: '<p>Capital of France?</p>',
      options: [
        { id: 'o1', text: 'Paris, France', isCorrect: true, isPinned: false },
        { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
        { id: null, text: 'Madrid', isCorrect: false, isPinned: false },
      ],
    });
    put.flush(question({ options: [
      { id: 'o1', text: 'Paris, France', isCorrect: true, isPinned: false },
      { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
      { id: 'o9', text: 'Madrid', isCorrect: false, isPinned: false },
    ] }));
    fixture.detectChanges();

    expect(root.textContent).toContain('Question saved.');
    expect(texts()).toEqual(['Paris, France', 'Rome', 'Madrid']);
  });

  it('can change which option is correct before anyone has answered', () => {
    open();
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));

    submit();

    expect(httpMock.expectOne(isPut).request.body.options.map((o: { isCorrect: boolean }) => o.isCorrect)).toEqual([false, true]);
  });

  it('keeps Save disabled while an option is empty', () => {
    open();
    type(1, '');

    expect(save().disabled).toBe(true);
  });

  it('locks the answer key and the option list once candidates have answered, but not the wording', () => {
    open(question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: true } }));

    expect(root.querySelector('.question-lock-note')?.textContent).toContain('only its wording can change');
    expect(Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).every((radio) => radio.disabled)).toBe(true);
    expect(Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add option')?.hasAttribute('disabled')).toBe(true);
    expect(root.textContent).toContain('In the exam Maths mock');

    type(0, 'Paris!');
    submit();
    expect(httpMock.expectOne(isPut).request.body.options[0]).toEqual({ id: 'o1', text: 'Paris!', isCorrect: true, isPinned: false });
  });

  it('says how many exams hold the question', () => {
    open(question({ usage: { examCount: 7, examNames: ['A', 'B', 'C', 'D', 'E'], answered: false } }));

    expect(root.textContent).toContain('In 7 exams');
    expect(root.textContent).toContain('and others');
  });

  it('shows the API’s reason when a candidate answered while the page was open, locks the controls and keeps the typing', () => {
    open();
    type(0, 'Paris!');
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    submit();

    httpMock.expectOne(isPut).flush(
      { title: 'question_locked', detail: 'Candidates have already answered this question, so only its wording can change.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(root.querySelector('.error-message')?.textContent).toContain('only its wording can change');
    expect(root.querySelector('.question-lock-note')).not.toBeNull();
    expect(texts()[0]).toBe('Paris!');
  });

  it('shows any other API error and leaves the form as it was', () => {
    open();
    submit();

    httpMock.expectOne(isPut).flush({ title: 'invalid_question', detail: 'Every option needs text.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.querySelector('.error-message')?.textContent).toContain('Every option needs text.');
    expect(root.querySelector('.question-lock-note')).toBeNull();
  });

  it('discards unsaved changes by reading the question again', () => {
    open();
    type(0, 'Something else');
    const discard = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Discard changes') as HTMLButtonElement;
    expect(discard.disabled).toBe(false);

    discard.click();
    httpMock.expectOne(isGet).flush(question());
    fixture.detectChanges();

    expect(texts()).toEqual(['Paris', 'Rome']);
  });

  it('says so when the question cannot be loaded', () => {
    fixture = TestBed.createComponent(QuestionEdit);
    fixture.detectChanges();
    httpMock.expectOne(isGet).flush({ title: 'question_not_found', detail: 'No question matches the given id.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No question matches the given id.');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });
});
