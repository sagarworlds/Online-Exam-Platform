import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { QuestionDto } from '../../question-bank/question.models';
import { ExamScopeDto } from '../exam.models';
import { isInScope } from './in-scope';
import { QuestionPicker } from './question-picker';

const question = (id: string, text: string, over: Partial<QuestionDto> = {}): QuestionDto => ({
  id,
  text: `<p>${text}</p>`,
  createdBy: 'u1',
  createdAtUtc: '2026-10-02T00:00:00Z',
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
  options: [
    { id: `${id}-a`, text: 'A', isCorrect: true, isPinned: false },
    { id: `${id}-b`, text: 'B', isCorrect: false, isPinned: false },
  ],
  ...over,
});

const isRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions');

describe('QuestionPicker', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<QuestionPicker>;
  let root: HTMLElement;
  let picked: string[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QuestionPicker], providers: [provideHttpClient(), provideHttpClientTesting()] }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function show(inputs: { scope?: ExamScopeDto; excluded?: ReadonlySet<string>; busy?: boolean; section?: string } = {}) {
    fixture = TestBed.createComponent(QuestionPicker);
    if (inputs.scope) fixture.componentRef.setInput('scope', inputs.scope);
    if (inputs.excluded) fixture.componentRef.setInput('excluded', inputs.excluded);
    if (inputs.busy) fixture.componentRef.setInput('busy', inputs.busy);
    fixture.componentRef.setInput('sectionName', inputs.section ?? 'Algebra');
    picked = [];
    fixture.componentInstance.picked.subscribe((id) => picked.push(id));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  /** Answers the read the picker makes, and returns it so a test can check what it asked with. */
  function answer(page: QuestionDto[] | 'fail') {
    const request = http.expectOne(isRead);
    if (page === 'fail') {
      request.flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      request.flush(page);
    }
    fixture.detectChanges();
    return request.request;
  }

  const stems = () => Array.from(root.querySelectorAll('.picker__stem')).map((e) => e.textContent?.trim());
  const search = (text: string) => {
    const input = root.querySelector('input[type="search"]') as HTMLInputElement;
    input.value = text;
    input.dispatchEvent(new Event('input'));
  };

  it('reads the first page of the bank, the newest first, 20 at a time, and lists each question with an Add button named for it', () => {
    show();
    expect(root.textContent).toContain('Searching…');

    const request = answer([question('q1', 'What is 2 + 2?'), question('q2', 'Capital of France?')]);

    expect(request.params.get('take')).toBe('20');
    expect(request.params.has('skip')).toBe(false);
    expect(stems()).toEqual(['What is 2 + 2?', 'Capital of France?']);
    expect(root.querySelector('.picker__count')?.textContent).toContain('2 questions');
    expect(root.querySelector('button[aria-label="Add “What is 2 + 2?” to Algebra"]')?.textContent?.trim()).toBe('Add');
  });

  it('says how hard a question is, and its topics and chapter, so one can be chosen without opening it', () => {
    show();

    answer([question('q1', 'Solve x', { difficulty: 'hard', topics: ['equations', 'algebra'], chapterTitle: 'Algebra' })]);

    expect(root.querySelector('.picker__meta')?.textContent?.trim()).toBe('Hard · equations, algebra · Algebra');
  });

  it('hands the page the id of the question chosen', () => {
    show();
    answer([question('q1', 'What is 2 + 2?')]);

    (root.querySelector('.picker__row button') as HTMLButtonElement).click();

    expect(picked).toEqual(['q1']);
  });

  it('cannot add while the page is making a request about the exam', () => {
    show({ busy: true });
    answer([question('q1', 'What is 2 + 2?')]);

    expect((root.querySelector('.picker__row button') as HTMLButtonElement).disabled).toBe(true);
  });

  describe('what the exam will accept', () => {
    it('leaves out a question that is already in the exam, since a question goes in once', () => {
      show({ excluded: new Set(['q1']) });

      answer([question('q1', 'What is 2 + 2?'), question('q2', 'Capital of France?')]);

      expect(stems()).toEqual(['Capital of France?']);
    });

    it('drops a question from the list once the exam holds it', () => {
      show();
      answer([question('q1', 'What is 2 + 2?'), question('q2', 'Capital of France?')]);

      fixture.componentRef.setInput('excluded', new Set(['q1']));
      fixture.detectChanges();

      expect(stems()).toEqual(['Capital of France?']);
    });

    it('asks the bank for the exam’s own book, and lists only the chapters the exam is limited to', () => {
      show({ scope: { type: 'Chapters', bookId: 'b1', bookName: 'Maths', className: null, chapters: [{ id: 'c1', title: 'Algebra' }] } });

      const request = answer([
        question('q1', 'Solve x', { bookId: 'b1', chapterId: 'c1' }),
        question('q2', 'Angles', { bookId: 'b1', chapterId: 'c2' }),
        question('q3', 'Optics', { bookId: 'b2', chapterId: 'c9' }),
        question('q4', 'Unfiled'),
      ]);

      expect(request.params.get('bookId')).toBe('b1');
      expect(stems()).toEqual(['Solve x']);
      expect(root.textContent).toContain("Only questions from this exam's chapters are offered.");
    });

    it('offers every question of the book to a whole-book exam', () => {
      show({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths', className: null, chapters: [] } });

      answer([question('q1', 'Solve x', { bookId: 'b1', chapterId: 'c1' }), question('q2', 'Angles', { bookId: 'b1', chapterId: 'c2' }), question('q3', 'Optics', { bookId: 'b2' })]);

      expect(stems()).toEqual(['Solve x', 'Angles']);
      expect(root.textContent).toContain("Only questions from this exam's book are offered.");
    });

    it('says nothing about a limit for an exam without one, and does not filter by book', () => {
      show();

      const request = answer([question('q1', 'Solve x')]);

      expect(request.params.has('bookId')).toBe(false);
      expect(root.textContent).not.toContain('are offered');
    });
  });

  describe('searching', () => {
    it('asks the bank for the words typed, after a pause in typing, not at every key', () => {
      vi.useFakeTimers();
      show();
      answer([question('q1', 'What is 2 + 2?')]);

      search('fr');
      search('france');
      vi.advanceTimersByTime(299);
      http.expectNone(isRead);
      vi.advanceTimersByTime(2);

      const request = answer([question('q2', 'Capital of France?')]);
      expect(request.params.get('q')).toBe('france');
      expect(stems()).toEqual(['Capital of France?']);
    });

    it('asks again for a difficulty at once, and "Any" asks for all', () => {
      show();
      answer([]);
      const select = root.querySelector('select') as HTMLSelectElement;

      select.value = 'hard';
      select.dispatchEvent(new Event('change'));
      expect(answer([question('q1', 'Hard one')]).params.get('difficulty')).toBe('hard');

      select.value = '';
      select.dispatchEvent(new Event('change'));
      expect(answer([]).params.has('difficulty')).toBe(false);
    });

    it('lets a newer search replace one still on its way, so a slow old answer cannot win', () => {
      vi.useFakeTimers();
      show();
      const first = http.expectOne(isRead);

      search('a');
      vi.advanceTimersByTime(301);
      const second = http.expectOne(isRead);
      second.flush([question('q2', 'Newer')]);
      fixture.detectChanges();

      expect(first.cancelled).toBe(true);
      expect(stems()).toEqual(['Newer']);
    });

    it('says nothing matches a search, and not that the bank is empty', () => {
      vi.useFakeTimers();
      show();
      answer([question('q1', 'What is 2 + 2?')]);

      search('zzz');
      vi.advanceTimersByTime(301);
      answer([]);

      expect(root.textContent).toContain('No question matches the search.');
      expect(root.querySelector('.picker__list')).toBeNull();
    });

    it('says there is nothing left to take when the bank has nothing for the exam', () => {
      show();

      answer([]);

      expect(root.textContent).toContain('There are no more questions in the bank to add.');
    });

    it('says what to do when a book’s exam has none left', () => {
      show({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths', className: null, chapters: [] } });

      answer([]);

      expect(root.textContent).toContain('There are none left to add: file more questions under its chapters');
    });
  });

  describe('more than a page', () => {
    const page = (from: number) => Array.from({ length: 20 }, (_, i) => question(`q${from + i}`, `Question ${from + i}`));

    it('offers "Show more" after a full page, which asks for the next one and adds it to the list', () => {
      show();
      answer(page(0));
      const more = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show more') as HTMLButtonElement;

      more.click();
      const request = answer([question('q20', 'Question 20')]);

      expect(request.params.get('skip')).toBe('20');
      expect(stems()).toHaveLength(21);
      expect(Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show more')).toBeUndefined();
    });

    it('counts the questions fetched, not the ones shown, to reach the next page when some are left out', () => {
      show({ excluded: new Set(['q0', 'q1']) });
      answer(page(0));

      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show more') as HTMLButtonElement).click();

      const next = http.expectOne(isRead);
      expect(next.request.params.get('skip')).toBe('20');
      next.flush([]);
    });

    it('does not offer "Show more" after a page that was not full', () => {
      show();
      answer(page(0).slice(0, 5));

      expect(Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show more')).toBeUndefined();
    });
  });

  describe('when the bank cannot be read', () => {
    it('says so and offers to try again, which reads it once more', () => {
      show();

      answer('fail');
      expect(root.querySelector('[role="alert"]')?.textContent).toContain('The questions could not be loaded.');

      (root.querySelector('[role="alert"] button') as HTMLButtonElement).click();
      answer([question('q1', 'What is 2 + 2?')]);

      expect(root.querySelector('[role="alert"]')).toBeNull();
      expect(stems()).toEqual(['What is 2 + 2?']);
    });
  });
});

describe('isInScope', () => {
  const inChapter = question('q1', 'x', { bookId: 'b1', chapterId: 'c1' });

  it('accepts anything for an exam with no limit', () => {
    expect(isInScope(question('q', 'x'), undefined)).toBe(true);
    expect(isInScope(question('q', 'x'), { type: 'Independent', bookId: null, bookName: null, className: null, chapters: [] })).toBe(true);
  });

  it('accepts only the exam’s own book for a whole-book exam', () => {
    const scope: ExamScopeDto = { type: 'Book', bookId: 'b1', bookName: 'M', className: null, chapters: [] };

    expect(isInScope(inChapter, scope)).toBe(true);
    expect(isInScope(question('q', 'x', { bookId: 'b2' }), scope)).toBe(false);
    expect(isInScope(question('q', 'x'), scope)).toBe(false);
  });

  it('accepts only the exam’s chapters for a chapter-wise exam', () => {
    const scope: ExamScopeDto = { type: 'Chapters', bookId: 'b1', bookName: 'M', className: null, chapters: [{ id: 'c1', title: 'A' }] };

    expect(isInScope(inChapter, scope)).toBe(true);
    expect(isInScope(question('q', 'x', { bookId: 'b1', chapterId: 'c2' }), scope)).toBe(false);
    expect(isInScope(question('q', 'x'), scope)).toBe(false);
  });
});
