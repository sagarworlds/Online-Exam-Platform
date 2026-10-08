import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AnswerKeyCorrectionResult, QuestionDto, QuestionOptionDto } from '../question.models';
import { AnswerKeyCorrection } from './answer-key-correction';

const option = (id: string, text: string, isCorrect: boolean): QuestionOptionDto => ({ id, text, isCorrect, isPinned: false });

// "Rome" is the key as it stands; the dispute says it should be "Paris".
const question = (overrides: Partial<QuestionDto> = {}): QuestionDto => ({
  id: 'q1',
  text: '<p>Capital of France?</p>',
  options: [option('o1', 'Rome', true), option('o2', 'Paris', false), option('o3', 'Lyon', false)],
  createdBy: 'author',
  createdAtUtc: '2026-10-01T09:00:00Z',
  chapterId: null,
  chapterTitle: null,
  bookId: null,
  bookName: null,
  classId: null,
  className: null,
  usage: { examCount: 1, examNames: ['Maths Final'], answered: true },
  difficulty: null,
  topics: [],
  allowsMultiple: false,
  ...overrides,
});

const multiple = (): QuestionDto =>
  question({
    allowsMultiple: true,
    options: [option('o1', 'Paris', true), option('o2', 'Rome', false), option('o3', 'Lyon', true), option('o4', 'Oslo', false)],
  });

describe('AnswerKeyCorrection', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<AnswerKeyCorrection>;
  let root: HTMLElement;
  let corrected: AnswerKeyCorrectionResult[];
  let cancelled: number;

  const isGet = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/q1');
  const isCorrection = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/q1/correct-answer-key');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnswerKeyCorrection],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function show(body: QuestionDto | 'error' | null = question()) {
    fixture = TestBed.createComponent(AnswerKeyCorrection);
    fixture.componentRef.setInput('questionId', 'q1');
    corrected = [];
    cancelled = 0;
    fixture.componentInstance.corrected.subscribe((result) => corrected.push(result));
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
    if (body === null) {
      return;
    }

    const request = httpMock.expectOne(isGet);
    if (body === 'error') {
      request.flush({ title: 'question_not_found', detail: 'There is no such question.' }, { status: 404, statusText: 'Not Found' });
    } else {
      request.flush(body);
    }
    fixture.detectChanges();
  }

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const inputs = () => Array.from(root.querySelectorAll<HTMLInputElement>('fieldset input'));
  const labels = () => Array.from(root.querySelectorAll('fieldset label')).map((l) => (l.textContent ?? '').trim());
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const typeReason = (value: string) => {
    const box = root.querySelector('textarea') as HTMLTextAreaElement;
    box.value = value;
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const pick = (index: number) => {
    inputs()[index].click();
    fixture.detectChanges();
  };

  it('reads the question once it is shown, and waits for it', () => {
    show(null);

    expect(text()).toContain('Loading…');
    httpMock.expectOne(isGet).flush(question());
    fixture.detectChanges();

    expect(text()).not.toContain('Loading…');
  });

  it('lists the options in order with their letters', () => {
    show();

    expect(labels()).toEqual(['A. Rome', 'B. Paris', 'C. Lyon']);
  });

  it('offers radio buttons for a question with one correct option, starting from the current key', () => {
    show();

    expect(inputs().map((i) => i.type)).toEqual(['radio', 'radio', 'radio']);
    expect(inputs().map((i) => i.checked)).toEqual([true, false, false]);
    expect(root.querySelector('legend')?.textContent).toBe('Correct option');
  });

  it('offers checkboxes for a multiple-answer question, with every current correct option ticked', () => {
    show(multiple());

    expect(inputs().map((i) => i.type)).toEqual(['checkbox', 'checkbox', 'checkbox', 'checkbox']);
    expect(inputs().map((i) => i.checked)).toEqual([true, false, true, false]);
    expect(root.querySelector('legend')?.textContent).toContain('tick every one');
  });

  it('warns that every candidate who answered is rescored and shown the reason', () => {
    show();

    expect(root.querySelector('.warning-note')?.textContent).toContain('scores are worked out again');
    expect(root.querySelector('.warning-note')?.textContent).toContain('each of them is shown your reason');
  });

  it('cannot be sent without a reason, nor with one of only spaces', () => {
    show();
    pick(1);

    expect(button('Correct the answer key').disabled).toBe(true);
    typeReason('   ');
    expect(button('Correct the answer key').disabled).toBe(true);

    typeReason('The key marked Rome');
    expect(button('Correct the answer key').disabled).toBe(false);
  });

  it('limits the reason to what the API accepts', () => {
    show();

    expect((root.querySelector('textarea') as HTMLTextAreaElement).maxLength).toBe(500);
  });

  it('sends the chosen option and the trimmed reason, and reports what the API did', () => {
    show();
    pick(1);
    typeReason('  The key marked Rome  ');

    button('Correct the answer key').click();
    fixture.detectChanges();

    const post = httpMock.expectOne(isCorrection);
    expect(post.request.body).toEqual({ correctOptionIds: ['o2'], reason: 'The key marked Rome' });
    post.flush({ keyChanged: true, attemptsRescored: 3 });

    expect(corrected).toEqual([{ keyChanged: true, attemptsRescored: 3 }]);
  });

  it('reports a key that was already as asked, which changed nothing', () => {
    show();
    typeReason('Checked again');

    button('Correct the answer key').click();

    const post = httpMock.expectOne(isCorrection);
    expect(post.request.body).toEqual({ correctOptionIds: ['o1'], reason: 'Checked again' });
    post.flush({ keyChanged: false, attemptsRescored: 0 });

    expect(corrected).toEqual([{ keyChanged: false, attemptsRescored: 0 }]);
  });

  it('sends the options of a multiple-answer question in their own order, however they were ticked', () => {
    show(multiple());
    pick(3);
    pick(1);
    pick(0);
    typeReason('Oslo and Rome are right too');

    button('Correct the answer key').click();

    const post = httpMock.expectOne(isCorrection);
    expect(post.request.body).toEqual({ correctOptionIds: ['o2', 'o3', 'o4'], reason: 'Oslo and Rome are right too' });
    post.flush({ keyChanged: true, attemptsRescored: 1 });
  });

  it('asks for at least one correct option in a multiple-answer question', () => {
    show(multiple());
    typeReason('A reason');
    pick(0);
    pick(2);

    expect(text()).toContain('Tick at least one correct option.');
    expect(button('Correct the answer key').disabled).toBe(true);
  });

  it('needs one option left incorrect in a multiple-answer question', () => {
    show(multiple());
    typeReason('A reason');
    pick(1);
    pick(3);

    expect(text()).toContain('At least one option must stay incorrect.');
    expect(button('Correct the answer key').disabled).toBe(true);

    pick(3);
    expect(root.querySelector('.field-error')).toBeNull();
    expect(button('Correct the answer key').disabled).toBe(false);
  });

  it('shows the API’s reason when the correction is refused, and lets staff send it again', () => {
    show();
    pick(1);
    typeReason('The key marked Rome');
    button('Correct the answer key').click();
    fixture.detectChanges();

    httpMock.expectOne(isCorrection).flush({ title: 'invalid_question', detail: 'A correct option must belong to this question.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('A correct option must belong to this question.');
    expect(corrected).toEqual([]);
    expect(button('Correct the answer key').disabled).toBe(false);

    button('Correct the answer key').click();
    httpMock.expectOne(isCorrection).flush({ keyChanged: true, attemptsRescored: 2 });
    expect(corrected).toHaveLength(1);
  });

  it('locks the panel while the correction is sent, so one click sends one correction', () => {
    show();
    typeReason('The key marked Rome');

    const send = button('Correct the answer key');
    send.click();
    fixture.detectChanges();
    send.click();

    expect(send.disabled).toBe(true);
    expect(button('Cancel').disabled).toBe(true);
    expect(inputs().every((i) => i.disabled)).toBe(true);
    httpMock.expectOne(isCorrection).flush({ keyChanged: false, attemptsRescored: 0 });
  });

  it('says Cancel and sends nothing', () => {
    show();
    pick(1);
    typeReason('A reason');

    button('Cancel').click();

    expect(cancelled).toBe(1);
    httpMock.expectNone(isCorrection);
  });

  it('shows why the question could not be read, and can be closed', () => {
    show('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('There is no such question.');
    expect(root.querySelector('fieldset')).toBeNull();

    button('Close').click();
    expect(cancelled).toBe(1);
  });
});
