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
  let pickedMany: string[][];

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
    pickedMany = [];
    fixture.componentInstance.picked.subscribe((id) => picked.push(id));
    fixture.componentInstance.pickedMany.subscribe((ids) => pickedMany.push(ids));
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

  describe('when the exam’s scope changes while the picker is open', () => {
    it('asks the bank again for the new book, and lists that book’s questions, not the old page filtered', () => {
      show({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths', className: null, chapters: [] } });
      answer([question('q1', 'Solve x', { bookId: 'b1' })]);

      fixture.componentRef.setInput('scope', { type: 'Book', bookId: 'b2', bookName: 'Physics', className: null, chapters: [] });
      fixture.detectChanges();
      const request = answer([question('q9', 'Optics', { bookId: 'b2' })]);

      expect(request.params.get('bookId')).toBe('b2');
      expect(stems()).toEqual(['Optics']);
    });

    it('asks for every question when the exam is no longer limited to a book', () => {
      show({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths', className: null, chapters: [] } });
      answer([]);

      fixture.componentRef.setInput('scope', { type: 'Independent', bookId: null, bookName: null, className: null, chapters: [] });
      fixture.detectChanges();

      expect(answer([question('q1', 'Anything')]).params.has('bookId')).toBe(false);
    });

    it('does not ask again when the scope is set to what it was', () => {
      show({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths', className: null, chapters: [] } });
      answer([]);

      fixture.componentRef.setInput('scope', { type: 'Book', bookId: 'b1', bookName: 'Maths (renamed)', className: null, chapters: [] });
      fixture.detectChanges();

      http.expectNone(isRead);
    });
  });

  describe('paging against a changing search', () => {
    const page = (from: number) => Array.from({ length: 20 }, (_, i) => question(`q${from + i}`, `Question ${from + i}`));
    const showMore = () => (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show more') as HTMLButtonElement).click();

    it('drops a page that was still on its way when the difficulty changed, so old rows never land in the new list', () => {
      show();
      answer(page(0));
      showMore();
      const late = http.expectOne(isRead);

      const select = root.querySelector('select') as HTMLSelectElement;
      select.value = 'hard';
      select.dispatchEvent(new Event('change'));
      const fresh = http.expectOne(isRead);
      fresh.flush([question('h1', 'A hard one')]);
      fixture.detectChanges();

      expect(late.cancelled).toBe(true);
      expect(stems()).toEqual(['A hard one']);
    });

    it('leaves out a question that a shifted page repeats, so no row is listed twice', () => {
      show();
      answer(page(0));
      showMore();

      // A question added to the bank in between pushes the last one of the first page onto the second.
      answer([question('q19', 'Question 19'), question('q20', 'Question 20')]);

      expect(stems()).toHaveLength(21);
      expect(new Set(stems()).size).toBe(21);
    });

    it('keeps the list and says so when a later page cannot be read, with Show more still there to try again', () => {
      show();
      answer(page(0));
      showMore();

      answer('fail');

      expect(stems()).toHaveLength(20);
      expect(root.querySelector('[role="alert"]')?.textContent).toContain('The questions could not be loaded.');
      expect(Array.from(root.querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Show more')).toBe(true);
    });
  });

  describe('focus after an Add', () => {
    it('goes to the row that takes the place of the one added, once the exam has been read again', async () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second'), question('q3', 'Third')]);
      document.body.appendChild(root);
      const buttons = () => Array.from(root.querySelectorAll<HTMLButtonElement>('.picker__row button'));
      buttons()[0].focus();

      // The page is busy with the request, then reads the exam again, which holds the question now.
      buttons()[0].click();
      fixture.componentRef.setInput('busy', true);
      fixture.detectChanges();
      fixture.componentRef.setInput('excluded', new Set(['q1']));
      fixture.componentRef.setInput('busy', false);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(picked).toEqual(['q1']);
      expect(document.activeElement).toBe(buttons()[0]);
      expect(stems()).toEqual(['Second', 'Third']);
      root.remove();
    });

    it('goes to the search box when the list is empty after it', async () => {
      show();
      answer([question('q1', 'Only one')]);
      document.body.appendChild(root);
      (root.querySelector('.picker__row button') as HTMLButtonElement).focus();

      (root.querySelector('.picker__row button') as HTMLButtonElement).click();
      fixture.componentRef.setInput('excluded', new Set(['q1']));
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(root.querySelector('input[type="search"]'));
      root.remove();
    });

    it('does not move focus that the person had put somewhere else', async () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);
      document.body.appendChild(root);
      const search = root.querySelector('input[type="search"]') as HTMLInputElement;
      (root.querySelector('.picker__row button') as HTMLButtonElement).click();
      search.focus();

      fixture.componentRef.setInput('excluded', new Set(['q1']));
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(search);
      root.remove();
    });
  });

  describe('choosing several at once', () => {
    const boxes = () => Array.from(root.querySelectorAll<HTMLInputElement>('.picker__check'));
    const tick = (index: number, checked = true) => {
      const box = boxes()[index];
      box.checked = checked;
      box.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const toolbarButtons = () => Array.from(root.querySelectorAll<HTMLButtonElement>('.picker__toolbar button'));
    const selectAll = () => toolbarButtons()[0];
    const addSelected = () => toolbarButtons()[1];
    const ticked = () => boxes().map((box) => box.checked);
    const page = (from: number, count = 20) => Array.from({ length: count }, (_, i) => question(`q${from + i}`, `Question ${from + i}`));

    it('puts a checkbox before each question, named for it, with Select all on the left and Add selected on the right above the list', () => {
      show();
      answer([question('q1', 'What is 2 + 2?'), question('q2', 'Capital of France?')]);

      expect(boxes()).toHaveLength(2);
      expect(boxes()[0].getAttribute('aria-label')).toBe('Select “What is 2 + 2?”');
      expect(ticked()).toEqual([false, false]);
      expect(selectAll().textContent?.trim()).toBe('Select all');
      expect(selectAll().classList.contains('btn--link')).toBe(true);
      expect(addSelected().textContent?.trim()).toBe('Add selected (0)');
      expect(addSelected().getAttribute('aria-label')).toBe('Add the selected questions (0) to Algebra');
      const toolbar = root.querySelector('.picker__toolbar') as HTMLElement;
      const list = root.querySelector('.picker__list') as HTMLElement;
      expect(toolbar.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('counts what is ticked and names it for a screen reader', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);

      tick(1);

      expect(root.querySelector('.picker__selected')?.textContent?.trim()).toBe('1 selected');
      expect(addSelected().textContent?.trim()).toBe('Add selected (1)');
      expect(root.querySelector('.picker__selected')?.getAttribute('role')).toBe('status');
    });

    it('cannot add with nothing ticked, or while the page is making a request about the exam', () => {
      show();
      answer([question('q1', 'First')]);
      expect(addSelected().disabled).toBe(true);

      tick(0);
      expect(addSelected().disabled).toBe(false);

      fixture.componentRef.setInput('busy', true);
      fixture.detectChanges();
      expect(addSelected().disabled).toBe(true);
    });

    it('hands the page the ticked ids in the order they are listed, not the order they were ticked', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second'), question('q3', 'Third')]);

      tick(2);
      tick(0);
      addSelected().click();

      expect(pickedMany).toEqual([['q1', 'q3']]);
      expect(picked).toEqual([]);
    });

    it('leaves the single Add button working as before', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);

      tick(0);
      (root.querySelectorAll('.picker__row button')[1] as HTMLButtonElement).click();

      expect(picked).toEqual(['q2']);
      expect(pickedMany).toEqual([]);
    });

    it('ticks every listed question with Select all, which then reads Clear selection and unticks them', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);

      selectAll().click();
      fixture.detectChanges();
      expect(ticked()).toEqual([true, true]);
      expect(selectAll().textContent?.trim()).toBe('Clear selection');
      expect(addSelected().textContent?.trim()).toBe('Add selected (2)');

      selectAll().click();
      fixture.detectChanges();
      expect(ticked()).toEqual([false, false]);
      expect(selectAll().textContent?.trim()).toBe('Select all');
    });

    it('reads Select all again once a ticked question is unticked by hand', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);
      selectAll().click();
      fixture.detectChanges();

      tick(0, false);

      expect(selectAll().textContent?.trim()).toBe('Select all');
      expect(addSelected().textContent?.trim()).toBe('Add selected (1)');
    });

    it('no longer ticks a question that leaves the list, so it is never handed over once the exam holds it', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);
      selectAll().click();
      fixture.detectChanges();

      fixture.componentRef.setInput('excluded', new Set(['q1']));
      fixture.detectChanges();
      addSelected().click();

      expect(pickedMany).toEqual([['q2']]);
    });

    it('keeps the ticks when the page refuses, so the same questions can be tried again', () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);
      selectAll().click();
      fixture.detectChanges();

      addSelected().click();
      fixture.detectChanges();

      expect(ticked()).toEqual([true, true]);
      expect(addSelected().disabled).toBe(false);
    });

    it('clears the ticks when the list starts afresh, so nothing ticked out of sight is added', () => {
      vi.useFakeTimers();
      show();
      answer([question('q1', 'First'), question('q2', 'Second')]);
      selectAll().click();
      fixture.detectChanges();

      search('sec');
      vi.advanceTimersByTime(301);
      answer([question('q2', 'Second')]);

      expect(ticked()).toEqual([false]);
      expect(addSelected().textContent?.trim()).toBe('Add selected (0)');
    });

    it('keeps the ticks when "Show more" adds a page', () => {
      show();
      answer(page(0));
      tick(3);

      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show more') as HTMLButtonElement).click();
      answer(page(20, 5));

      expect(ticked()[3]).toBe(true);
      expect(addSelected().textContent?.trim()).toBe('Add selected (1)');
    });

    it('ticks at most 100 with Select all, and offers no more boxes once 100 are ticked', () => {
      show();
      // More than 100 are listed after a few "Show more" presses; one big answer stands in for them, so the list is drawn once.
      answer(page(0, 101));
      expect(boxes()).toHaveLength(101);

      selectAll().click();
      fixture.detectChanges();
      addSelected().click();

      expect(addSelected().textContent?.trim()).toBe('Add selected (100)');
      expect(selectAll().textContent?.trim()).toBe('Clear selection');
      expect(boxes()[99].disabled).toBe(false);
      expect(boxes()[100].disabled).toBe(true);
      expect(pickedMany[0]).toHaveLength(100);
      expect(pickedMany[0][99]).toBe('q99');
    }, 30_000);

    it('has no toolbar while there is nothing to list', () => {
      show();
      answer([]);

      expect(root.querySelector('.picker__toolbar')).toBeNull();
    });

    it('puts focus on Select all once the ticked questions have gone, since Add selected has nothing left to add', async () => {
      show();
      answer([question('q1', 'First'), question('q2', 'Second'), question('q3', 'Third')]);
      document.body.appendChild(root);
      tick(0);
      tick(1);
      addSelected().focus();

      addSelected().click();
      fixture.componentRef.setInput('busy', true);
      fixture.detectChanges();
      fixture.componentRef.setInput('excluded', new Set(['q1', 'q2']));
      fixture.componentRef.setInput('busy', false);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(pickedMany).toEqual([['q1', 'q2']]);
      expect(document.activeElement).toBe(selectAll());
      root.remove();
    });

    it('puts focus on the search box when every question listed was added', async () => {
      show();
      answer([question('q1', 'Only one')]);
      document.body.appendChild(root);
      tick(0);
      addSelected().focus();

      addSelected().click();
      fixture.componentRef.setInput('excluded', new Set(['q1']));
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(root.querySelector('input[type="search"]'));
      root.remove();
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
