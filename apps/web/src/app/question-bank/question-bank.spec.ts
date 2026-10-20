import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FormGroup } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { QuestionBank } from './question-bank';
import { QuestionUsageDto } from './question.models';

// The base URL differs between builds and the test environment, so requests are matched by their path.
const isList = (r: { method: string; url: string }) => r.method === 'GET' && /\/v1\/questions(\?.*)?$/.test(r.url);
const isTopics = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/topics');
const isEncryptionStatus = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/encryption-status');
const isBooks = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/books');

const chapter = (id: string, order: number, title: string, isArchived = false) => ({ id, bookId: 'b1', title, order, isArchived, questionCount: 0 });
const book = (id: string, name: string, chapters: ReturnType<typeof chapter>[], isArchived = false, schoolClass: { id: string; name: string } | null = null) => ({
  id, name, classId: schoolClass?.id ?? null, className: schoolClass?.name ?? null, subject: null, description: null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
});
const UNUSED: QuestionUsageDto = { examCount: 0, examNames: [], answered: false };
const listedQuestion = (id: string, text: string, usage: QuestionUsageDto = UNUSED) => ({
  id, text, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', chapterId: null, chapterTitle: null, bookId: null, bookName: null, classId: null, className: null, usage,
  difficulty: null, topics: [], allowsMultiple: false,
  options: [{ id: `${id}-a`, text: 'A', isCorrect: true, isPinned: false }, { id: `${id}-b`, text: 'B', isCorrect: false, isPinned: false }],
});
const MATHS = book('b1', 'Maths Grade 10', [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry'), chapter('c3', 3, 'Old chapter', true)]);
const OLD_BOOK = book('b2', 'Old Physics', [{ ...chapter('c9', 1, 'Optics'), bookId: 'b2' }], true);

describe('QuestionBank', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionBank],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Saving a question re-reads the topics in use; tests about something else do not need to answer that.
    httpMock.match(isTopics).forEach((request) => request.flush([]));
    // The admin encryption status the page shows (#57) is answered here, so it does not need its own expectation in each test.
    httpMock
      .match(isEncryptionStatus)
      .forEach((request) => request.flush({ contentValues: 0, plaintextValues: 0, complete: true }));
    httpMock.verify();
  });

  function create(books: ReturnType<typeof book>[] = [], topics: string[] = []) {
    const fixture = TestBed.createComponent(QuestionBank);
    fixture.detectChanges();
    httpMock.expectOne(isBooks).flush(books);
    httpMock.expectOne(isTopics).flush(topics);
    return fixture;
  }

  function fill(fixture: ComponentFixture<QuestionBank>, text: string, options: string[]): void {
    const root = fixture.nativeElement as HTMLElement;
    // The question text lives in a rich-text editor, so it is set through the form control the editor is bound to.
    (fixture.componentInstance as unknown as { form: FormGroup }).form.controls['text'].setValue(text);
    const inputs = root.querySelectorAll<HTMLInputElement>('.option-row input[type="text"]');
    options.forEach((value, i) => {
      inputs[i].value = value;
      inputs[i].dispatchEvent(new Event('input'));
    });
  }

  it('lists the newest questions and marks the correct option', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1',
        text: 'What is 2 + 2?',
        options: [
          { id: 'o1', text: '3', isCorrect: false, isPinned: false },
          { id: 'o2', text: '4', isCorrect: true, isPinned: false },
        ],
        createdBy: 'u1',
        createdAtUtc: '2026-10-02T00:00:00Z',
        chapterId: null,
        chapterTitle: null,
        bookId: null,
        bookName: null,
        usage: UNUSED,
        difficulty: null,
        topics: [],
        allowsMultiple: false,
      },
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('What is 2 + 2?');
    expect(root.textContent).toContain('Not filed under a chapter');
    // A line shows the question; its options, with the correct one marked, show once it is opened.
    expect(root.querySelector('.correct-option')).toBeNull();
    (root.querySelector('button[aria-label="Show details"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelector('.correct-option')?.textContent).toContain('4');
  });

  describe('paging', () => {
    const page = (from: number, count: number) => Array.from({ length: count }, (_, i) => listedQuestion(`q${from + i}`, `Question ${from + i}`));
    const items = (fixture: ComponentFixture<QuestionBank>) => (fixture.nativeElement as HTMLElement).querySelectorAll('app-question-row').length;
    const loadMore = (fixture: ComponentFixture<QuestionBank>) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes('more')) as HTMLButtonElement | undefined;

    it('offers no more once a short page has come back', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush(page(1, 3));
      fixture.detectChanges();

      expect(items(fixture)).toBe(3);
      expect(loadMore(fixture)).toBeUndefined();
    });

    it('asks for fifty at a time, from the start', () => {
      const fixture = create();
      const first = httpMock.expectOne(isList);
      expect(first.request.params.get('take')).toBe('50');
      expect(first.request.params.has('skip')).toBe(false);
      first.flush([]);
      fixture.detectChanges();
    });

    it('loads fifty more below the first fifty, skipping what is already shown, until a short page ends it', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush(page(1, 50));
      fixture.detectChanges();
      expect(items(fixture)).toBe(50);
      expect(loadMore(fixture)?.textContent?.trim()).toBe('Load 50 more');

      loadMore(fixture)!.click();
      const next = httpMock.expectOne(isList);
      expect(next.request.params.get('skip')).toBe('50');
      expect(next.request.params.get('take')).toBe('50');
      // The last question of the first page comes back again because one was created meanwhile; it is not shown twice.
      next.flush(page(50, 5));
      fixture.detectChanges();

      expect(items(fixture)).toBe(54);
      expect(loadMore(fixture)).toBeUndefined();
    });
  });

  it('shows which book and chapter a question is filed under', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1', text: 'Solve x', options: [{ id: 'o1', text: '1', isCorrect: true, isPinned: false }, { id: 'o2', text: '2', isCorrect: false, isPinned: false }],
        createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', chapterId: 'c1', chapterTitle: 'Algebra', bookId: 'b1', bookName: 'Maths Grade 10', usage: UNUSED, difficulty: null, topics: [], allowsMultiple: false,
      },
    ]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.question-row__where')?.textContent).toContain('Maths Grade 10');
    expect((fixture.nativeElement as HTMLElement).querySelector('.question-row__where')?.textContent).toContain('Algebra');
  });

  it('shows the formatting of a question in the list, and nothing executable', () => {
    const w = window as unknown as { __ran?: boolean };
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1',
        text: '<p>Water is H<sub>2</sub>O</p><img src="x" onerror="window.__ran = true">',
        options: [
          { id: 'o1', text: 'Yes', isCorrect: true, isPinned: false },
          { id: 'o2', text: 'No', isCorrect: false, isPinned: false },
        ],
        createdBy: 'u1',
        createdAtUtc: '2026-10-02T00:00:00Z',
        usage: UNUSED,
        difficulty: null,
        topics: [],
        allowsMultiple: false,
      },
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.question-row sub')?.textContent).toBe('2');
    expect(root.querySelector('[onerror]')).toBeNull();
    expect(w.__ran).toBeUndefined();
  });

  it('keeps Save disabled until every option has text and a correct option is chosen', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const save = root.querySelector('form button[type="submit"]') as HTMLButtonElement;

    fill(fixture, 'Q?', ['A', 'B']);
    fixture.detectChanges();
    expect(save.disabled).toBe(true);

    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(save.disabled).toBe(false);
  });

  it('posts the options with the chosen one marked correct, then reloads the list', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    fill(fixture, 'Capital of France?', ['Rome', 'Paris']);
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/questions'));
    expect(post.request.body).toEqual({
      text: 'Capital of France?',
      chapterId: null,
      difficulty: null,
      topics: [],
      allowsMultiple: false,
      language: 'en',
      isTextAnswer: false,
      acceptedAnswers: [],
      explanation: null,
      options: [
        { text: 'Rome', isCorrect: false, isPinned: false },
        { text: 'Paris', isCorrect: true, isPinned: false },
      ],
    });
    post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    expect(root.textContent).toContain('Question saved.');
  });

  describe('language (FR-10)', () => {
    it('offers English, Hindi and Marathi for a new question, English first, and sends the one chosen', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const select = root.querySelector('#question-language') as HTMLSelectElement;

      expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual(['English', 'Hindi', 'Marathi']);
      expect(select.value).toBe('en');

      select.value = 'mr';
      select.dispatchEvent(new Event('change'));
      fill(fixture, 'फ्रान्सची राजधानी?', ['पॅरिस', 'रोम']);
      (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
      fixture.detectChanges();
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/questions'));
      expect(post.request.body.language).toBe('mr');
      post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      // An author enters many questions in one language in a row.
      expect((root.querySelector('#question-language') as HTMLSelectElement).value).toBe('mr');
    });

    it('narrows the list to one language, and back to any', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const pick = (value: string) => {
        const select = root.querySelector('#filter-language') as HTMLSelectElement;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      };

      expect(Array.from(root.querySelectorAll('#filter-language option')).map((o) => o.textContent?.trim())).toEqual(['Any language', 'English', 'Hindi', 'Marathi']);

      pick('hi');
      const hindi = httpMock.expectOne(isList);
      expect(hindi.request.params.get('language')).toBe('hi');
      hindi.flush([]);
      fixture.detectChanges();
      expect(root.textContent).toContain('No questions match this filter.');

      pick('');
      const any = httpMock.expectOne(isList);
      expect(any.request.params.has('language')).toBe(false);
      any.flush([]);
    });

    it('reads the list again when a translation is added from a card', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([listedQuestion('q1', 'What is 2 + 2?')]);
      fixture.detectChanges();

      (fixture.componentInstance as unknown as { onTranslationAdded(): void }).onTranslationAdded();

      httpMock.expectOne(isList).flush([]);
    });
  });

  describe('difficulty and topics', () => {
    const field = (root: HTMLElement, id: string) => root.querySelector(`#${id}`) as HTMLInputElement & HTMLSelectElement;

    it('sends the difficulty and the cleaned topics a new question was given', () => {
      const fixture = create([], ['fractions']);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      fill(fixture, 'Q?', ['A', 'B']);
      (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
      field(root, 'question-difficulty').value = 'hard';
      field(root, 'question-difficulty').dispatchEvent(new Event('change'));
      field(root, 'question-topics').value = ' Fractions, ratios ,FRACTIONS,, ';
      field(root, 'question-topics').dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/questions'));
      expect(post.request.body).toMatchObject({ difficulty: 'hard', topics: ['fractions', 'ratios'] });
      post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isList).flush([]);
    });

    it('offers the topics already in use as suggestions, and as a filter', () => {
      const fixture = create([], ['algebra', 'fractions']);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      const suggestions = Array.from(root.querySelectorAll('#question-topic-suggestions option')).map((o) => o.getAttribute('value'));
      const filterOptions = Array.from(root.querySelectorAll('#filter-topic option')).map((o) => o.textContent?.trim());
      expect(suggestions).toEqual(['algebra', 'fractions']);
      expect(filterOptions).toEqual(['Any topic', 'algebra', 'fractions']);
    });

    it('refuses more than five topics before anything is sent', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      field(root, 'question-topics').value = 'a, b, c, d, e, f';
      field(root, 'question-topics').dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(root.textContent).toContain('Use at most 5 topics.');
      expect((root.querySelector('form button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    });

    it('narrows the list by difficulty and by topic, together with the place filter', () => {
      const fixture = create([MATHS], ['fractions']);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const choose = (id: string, value: string) => {
        const select = root.querySelector(`#${id}`) as HTMLSelectElement;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      };

      choose('filter-difficulty', 'hard');
      expect(httpMock.expectOne(isList).request.params.get('difficulty')).toBe('hard');

      choose('filter-topic', 'fractions');
      const both = httpMock.expectOne(isList).request.params;
      expect([both.get('difficulty'), both.get('topic')]).toEqual(['hard', 'fractions']);

      choose('filter-book', 'b1');
      const withBook = httpMock.expectOne(isList).request.params;
      expect([withBook.get('bookId'), withBook.get('difficulty'), withBook.get('topic')]).toEqual(['b1', 'hard', 'fractions']);

      choose('filter-difficulty', '');
      choose('filter-topic', '');
      httpMock.match(isList).forEach((request) => request.flush([]));
      fixture.detectChanges();
      expect(root.textContent).toContain('No questions match this filter.');
    });
  });

  describe('searching', () => {
    const search = (fixture: ComponentFixture<QuestionBank>, text: string, how: 'enter' | 'change' = 'enter') => {
      const box = (fixture.nativeElement as HTMLElement).querySelector('#filter-search') as HTMLInputElement;
      box.value = text;
      box.dispatchEvent(how === 'enter' ? new KeyboardEvent('keydown', { key: 'Enter' }) : new Event('change'));
      fixture.detectChanges();
    };

    it('sends the typed text as q, trimmed, together with the other filters', () => {
      const fixture = create([], ['fractions']);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      search(fixture, '  half of  ');

      expect(httpMock.expectOne(isList).request.params.get('q')).toBe('half of');
    });

    it('does not send the same search twice when Enter and leaving the field both fire', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      search(fixture, 'zebra');
      search(fixture, 'zebra', 'change');

      expect(httpMock.match(isList).length).toBe(1);
    });

    it('lists everything again when the search is cleared', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      search(fixture, 'zebra');
      httpMock.expectOne(isList).flush([]);

      search(fixture, '', 'change');

      expect(httpMock.expectOne(isList).request.params.has('q')).toBe(false);
    });

    it('keeps the search when a later page is loaded, and says so when nothing matches', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      search(fixture, 'zebra');
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('No questions match this filter.');
    });
  });

  it('files a question under a chapter, requires one once a book is chosen, and keeps the choice after saving', () => {
    const fixture = create([MATHS, OLD_BOOK]);
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const choose = (id: string, value: string) => {
      const select = root.querySelector(`#${id}`) as HTMLSelectElement;
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const save = () => root.querySelector('form button[type="submit"]') as HTMLButtonElement;

    // Archived books take nothing new, so they are not offered here.
    const bookOptions = Array.from(root.querySelectorAll('#question-book option')).map((o) => o.textContent?.trim());
    expect(bookOptions).toEqual(['Not filed under a book', 'Maths Grade 10']);

    fill(fixture, 'Solve x + 1 = 2', ['1', '2']);
    (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(save().disabled).toBe(false);

    choose('question-book', 'b1');
    // Only open chapters of that book are offered, and one must be chosen.
    expect(Array.from(root.querySelectorAll('#question-chapter option')).map((o) => o.textContent?.trim())).toEqual(['Choose a chapter…', '1. Algebra', '2. Geometry']);
    expect(save().disabled).toBe(true);

    choose('question-chapter', 'c2');
    expect(save().disabled).toBe(false);
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/questions'));
    expect(post.request.body.chapterId).toBe('c2');
    post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();

    // The next question goes into the same chapter without choosing it again.
    expect((root.querySelector('#question-book') as HTMLSelectElement).value).toBe('b1');
    expect((root.querySelector('#question-chapter') as HTMLSelectElement).value).toBe('c2');
  });

  it('says when a chosen book has no open chapters, and where to add one', () => {
    const fixture = create([book('b3', 'Empty', [])]);
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const select = root.querySelector('#question-book') as HTMLSelectElement;
    select.value = 'b3';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(root.textContent).toContain('This book has no open chapters');
    expect(root.querySelector('a[href="/admin/books/b3"]')).not.toBeNull();
  });

  it('narrows the list by book, then chapter, then to questions not filed, and back to all', () => {
    const fixture = create([MATHS, OLD_BOOK]);
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const pick = (id: string, value: string) => {
      const select = root.querySelector(`#${id}`) as HTMLSelectElement;
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const nextList = () => {
      const req = httpMock.expectOne(isList);
      req.flush([]);
      fixture.detectChanges();
      return req.request;
    };

    // Archived books stay in the filter: their questions still exist.
    expect(Array.from(root.querySelectorAll('#filter-book option')).map((o) => o.textContent?.trim())).toEqual([
      'All questions', 'Not filed under a chapter', 'Maths Grade 10', 'Old Physics (archived)',
    ]);
    expect(root.querySelector('#filter-chapter')).toBeNull();

    pick('filter-book', 'b1');
    expect(nextList().params.get('bookId')).toBe('b1');
    expect(Array.from(root.querySelectorAll('#filter-chapter option')).map((o) => o.textContent?.trim())).toEqual([
      'All chapters', '1. Algebra', '2. Geometry', '3. Old chapter (archived)',
    ]);

    pick('filter-chapter', 'c2');
    const byChapter = nextList();
    expect(byChapter.params.get('chapterId')).toBe('c2');
    expect(byChapter.params.has('bookId')).toBe(false);

    pick('filter-book', 'unfiled');
    const unfiled = nextList();
    expect(unfiled.params.get('unfiled')).toBe('true');
    expect(root.querySelector('#filter-chapter')).toBeNull();

    pick('filter-book', '');
    // Nothing but the page size: an unfiltered list asks for no more than that.
    expect(nextList().params.keys()).toEqual(['take']);
  });

  it('says so when a filter matches no questions', () => {
    const fixture = create([MATHS]);
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const select = (fixture.nativeElement as HTMLElement).querySelector('#filter-book') as HTMLSelectElement;
    select.value = 'b1';
    select.dispatchEvent(new Event('change'));
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No questions match this filter.');
  });

  describe('class', () => {
    const FOURTH = { id: 'k4', name: '4th' };
    const FIFTH = { id: 'k5', name: '5th' };
    const THIRD = { id: 'k3', name: '3rd' };
    const english = (id: string, schoolClass: { id: string; name: string }, chapters: string[], isArchived = false) =>
      book(id, 'English', chapters.map((title, i) => ({ ...chapter(`${id}-c${i + 1}`, i + 1, title), bookId: id })), isArchived, schoolClass);
    const ENGLISH_4 = english('b4', FOURTH, ['Nouns', 'Verbs']);
    const ENGLISH_5 = english('b5', FIFTH, ['Poems']);
    // English exists in two classes, Maths has none, and the only book of the 3rd class is archived (its questions still exist).
    const BOOKS = [MATHS, ENGLISH_5, ENGLISH_4, english('b3', THIRD, [], true)];

    function open(books: ReturnType<typeof book>[] = BOOKS) {
      const fixture = create(books);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const pick = (id: string, value: string) => {
        const select = root.querySelector(`#${id}`) as HTMLSelectElement;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      };
      const texts = (id: string) => Array.from(root.querySelectorAll(`#${id} option`)).map((o) => o.textContent?.trim());
      // Answers the list the page asks for after a filter changes and hands back its query.
      const nextList = () => {
        const req = httpMock.expectOne(isList);
        req.flush([]);
        fixture.detectChanges();
        return req.request.params;
      };
      return { fixture, root, pick, texts, nextList };
    }

    it('offers no class filter, and no class select in the form, while no book has a class', () => {
      const { root } = open([MATHS, OLD_BOOK]);

      expect(root.querySelector('#filter-class')).toBeNull();
      expect(root.querySelector('#question-class')).toBeNull();
    });

    it('offers a class filter before the book filter, with the classes of all books, archived ones too, in natural order', () => {
      const { root, texts } = open([...BOOKS, english('b10', { id: 'k10', name: '10th' }, ['A']), english('b2', { id: 'k2', name: '2nd' }, ['A'])]);

      expect(texts('filter-class')).toEqual(['Any class', '2nd', '3rd', '4th', '5th', '10th']);
      const selects = Array.from(root.querySelectorAll('.list-filter select')).map((s) => s.id);
      expect(selects.indexOf('filter-class')).toBeLessThan(selects.indexOf('filter-book'));
      expect(root.querySelector('label[for="filter-class"]')?.textContent).toBe('Class');
    });

    it('labels the books with their class while any class is chosen', () => {
      const { texts } = open();

      expect(texts('filter-book')).toEqual(['All questions', 'Not filed under a chapter', '3rd · English (archived)', '4th · English', '5th · English', 'Maths Grade 10']);
    });

    it('sends the class chosen as classId, and narrows the books to it', () => {
      const { pick, texts, nextList } = open();

      pick('filter-class', 'k4');
      const params = nextList();

      expect(params.get('classId')).toBe('k4');
      expect(params.has('bookId')).toBe(false);
      expect(texts('filter-book')).toEqual(['All books of this class', 'English']);
    });

    it('combines the class with a book, and with a chapter, in the request', () => {
      const { pick, nextList } = open();
      pick('filter-class', 'k4');
      nextList();

      pick('filter-book', 'b4');
      const withBook = nextList();
      expect([withBook.get('classId'), withBook.get('bookId')]).toEqual(['k4', 'b4']);

      pick('filter-chapter', 'b4-c2');
      const withChapter = nextList();
      expect([withChapter.get('classId'), withChapter.get('chapterId'), withChapter.has('bookId')]).toEqual(['k4', 'b4-c2', false]);
    });

    it('combines the class with the other filters', () => {
      const { pick, nextList } = open();

      pick('filter-difficulty', 'hard');
      nextList();
      pick('filter-class', 'k5');

      const params = nextList();
      expect([params.get('classId'), params.get('difficulty')]).toEqual(['k5', 'hard']);
    });

    it('clears the book and chapter filter when the class changes to one that does not hold the book', () => {
      const { root, pick, texts, nextList } = open();
      pick('filter-class', 'k4');
      nextList();
      pick('filter-book', 'b4');
      nextList();
      pick('filter-chapter', 'b4-c1');
      nextList();

      pick('filter-class', 'k5');
      const params = nextList();

      expect([params.get('classId'), params.has('bookId'), params.has('chapterId')]).toEqual(['k5', false, false]);
      expect((root.querySelector('#filter-book') as HTMLSelectElement).value).toBe('');
      expect(root.querySelector('#filter-chapter')).toBeNull();
      expect(texts('filter-book')).toEqual(['All books of this class', 'English']);
    });

    it('keeps the book and chapter filter when the class changes to the class of that book, or back to any class', () => {
      const { root, pick, nextList } = open();
      pick('filter-book', 'b4');
      nextList();
      pick('filter-chapter', 'b4-c1');
      nextList();

      pick('filter-class', 'k4');
      const same = nextList();
      expect([same.get('classId'), same.get('chapterId')]).toEqual(['k4', 'b4-c1']);
      expect((root.querySelector('#filter-chapter') as HTMLSelectElement).value).toBe('b4-c1');

      pick('filter-class', '');
      const any = nextList();
      expect([any.has('classId'), any.get('chapterId')]).toEqual([false, 'b4-c1']);
    });

    it('does not offer questions not filed under a chapter while a class is chosen, since they have no class', () => {
      const { pick, texts, nextList } = open();

      pick('filter-class', 'k4');
      nextList();

      expect(texts('filter-book')).not.toContain('Not filed under a chapter');
      pick('filter-class', '');
      expect(texts('filter-book')).toContain('Not filed under a chapter');
      nextList();
    });

    it('drops the "not filed" filter when a class is chosen, which no question of a class can match', () => {
      const { root, pick, nextList } = open();
      pick('filter-book', 'unfiled');
      expect(nextList().get('unfiled')).toBe('true');

      pick('filter-class', 'k4');
      const params = nextList();

      expect([params.get('classId'), params.has('unfiled')]).toEqual(['k4', false]);
      expect((root.querySelector('#filter-book') as HTMLSelectElement).value).toBe('');
    });

    it('lists everything again when the class is set back to any class', () => {
      const { pick, nextList } = open();
      pick('filter-class', 'k4');
      nextList();

      pick('filter-class', '');

      expect(nextList().has('classId')).toBe(false);
    });

    it('says so when a class filter matches no questions', () => {
      const { fixture, pick } = open();
      pick('filter-class', 'k4');
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('No questions match this filter.');
    });

    it('puts a class select before the book in the new-question form, and files under the chapter reached through it', () => {
      const { fixture, root, pick, texts } = open();
      expect(Array.from(root.querySelectorAll('form[aria-label="New question"] select')).map((s) => s.id).slice(0, 2)).toEqual(['question-class', 'question-book']);
      expect(texts('question-class')).toEqual(['Any class', '4th', '5th', 'No class']);

      pick('question-class', 'k4');
      expect(texts('question-book')).toEqual(['Not filed under a book', 'English']);
      pick('question-book', 'b4');
      pick('question-chapter', 'b4-c2');
      fill(fixture, 'Which is a noun?', ['Run', 'Dog']);
      (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
      fixture.detectChanges();
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/questions'));
      expect(post.request.body.chapterId).toBe('b4-c2');
      expect(Object.keys(post.request.body)).not.toContain('classId');
      post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isList).flush([]);
    });

    it('shows the class first on the card of a question filed under a book with one', () => {
      const fixture = create(BOOKS);
      httpMock.expectOne(isList).flush([
        { ...listedQuestion('q1', 'Pick the noun'), chapterId: 'b4-c1', chapterTitle: 'Nouns', bookId: 'b4', bookName: 'English', classId: 'k4', className: '4th' },
        { ...listedQuestion('q2', 'Solve x'), chapterId: 'c1', chapterTitle: 'Algebra', bookId: 'b1', bookName: 'Maths Grade 10' },
      ]);
      fixture.detectChanges();

      const where = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.question-row__where')).map((w) => w.textContent?.replace(/\s+/g, ' ').trim());
      expect(where).toEqual(['4th › English › Nouns', 'Maths Grade 10 › Algebra']);
    });
  });

  it('adds options up to six and removes down to two', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const count = () => root.querySelectorAll('.option-row input[type="text"]').length;
    const addButton = () =>
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add option') as HTMLButtonElement;

    expect(count()).toBe(2);
    for (let i = 0; i < 6; i++) {
      addButton().click();
      fixture.detectChanges();
    }
    expect(count()).toBe(6);
    expect(addButton().disabled).toBe(true);

    for (let i = 0; i < 6; i++) {
      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();
      fixture.detectChanges();
    }
    expect(count()).toBe(2);
  });

  it('shows the API error when saving fails', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    fill(fixture, 'Q?', ['A', 'B']);
    (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush({ title: 'invalid_question', detail: 'Exactly one option must be marked correct.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Exactly one option must be marked correct.');
  });

  // The form is long and Save is at its bottom, so what happened to Save has to be shown beside it: at the top of the page it is out of sight.
  describe('what happened to Save is shown under the form', () => {
    const isCreate = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions');
    const isCheck = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/duplicates');

    function press() {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      fill(fixture, 'Capital of France?', ['Paris', 'Rome']);
      (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
      fixture.detectChanges();
      const save = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      const form = root.querySelector('form') as HTMLFormElement;
      save();
      return { fixture, root, form, save };
    }

    it('says the question was saved directly under the Save button, as a status', () => {
      const { fixture, form } = press();

      httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      const saved = form.nextElementSibling as HTMLElement;
      expect(saved.className).toContain('success-message');
      expect(saved.getAttribute('role')).toBe('status');
      expect(saved.textContent).toContain('Question saved.');
    });

    it('shows why a save was refused directly under the Save button and moves focus to it', () => {
      const { fixture, form } = press();

      httpMock.expectOne(isCreate).flush({ title: 'invalid_question', detail: 'The question text must be at most 4000 characters.' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      const refusal = form.nextElementSibling as HTMLElement;
      expect(refusal.className).toContain('error-message');
      expect(refusal.getAttribute('role')).toBe('alert');
      expect(refusal.textContent).toContain('The question text must be at most 4000 characters.');
      expect(document.activeElement).toBe(refusal);
    });

    it('shows the refusal there too when the bank cannot say what the question repeats', () => {
      const { fixture, form } = press();

      httpMock.expectOne(isCreate).flush({ title: 'duplicate_question', detail: 'The question bank already has a question with this wording.' }, { status: 409, statusText: 'Conflict' });
      httpMock.expectOne(isCheck).flush({ title: 'server_error' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      const refusal = form.nextElementSibling as HTMLElement;
      expect(refusal.className).toContain('error-message');
      expect(refusal.textContent).toContain('The question bank already has a question with this wording.');
      expect(document.activeElement).toBe(refusal);
    });

    it('takes the refusal away when the next save is accepted, so the two are never shown together', () => {
      const { fixture, root, form, save } = press();
      httpMock.expectOne(isCreate).flush({ title: 'invalid_question', detail: 'Try again.' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      save();
      httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      expect(root.querySelector('.error-message')).toBeNull();
      expect((form.nextElementSibling as HTMLElement).textContent).toContain('Question saved.');
    });

    it('keeps the reason the page could not load something at the top, above the form', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush({ title: 'server_error', detail: 'The questions could not be read.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      const problem = root.querySelector('.error-message') as HTMLElement;
      const form = root.querySelector('form') as HTMLFormElement;
      expect(problem.textContent).toContain('The questions could not be read.');
      expect(form.compareDocumentPosition(problem) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
    });
  });

  describe('a question the bank already has (FR-9)', () => {
    const isCreate = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions');
    const isCheck = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/duplicates');

    function submitRepeat() {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      fill(fixture, 'Capital of France?', ['Paris', 'Rome']);
      (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
      fixture.detectChanges();
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      httpMock
        .expectOne(isCreate)
        .flush({ title: 'duplicate_question', detail: 'The question bank already has a question with this wording.' }, { status: 409, statusText: 'Conflict' });
      httpMock
        .expectOne(isCheck)
        .flush([{ id: 'q1', preview: 'Capital of France?', sameOptions: true, status: 'approved', chapterId: null }]);
      fixture.detectChanges();
      return { fixture, root };
    }

    it('shows what it repeats instead of an error, and sends nothing more until the author decides', () => {
      const { root } = submitRepeat();

      expect(root.textContent).toContain('This question is already in the bank');
      expect(root.textContent).toContain('Capital of France?');
      expect(root.querySelector('.error-message')).toBeNull();
    });

    it('puts the warning below the Save button, where the author is looking, not above the long form', () => {
      const { root } = submitRepeat();

      const form = root.querySelector('form') as HTMLFormElement;
      const warning = root.querySelector('.question-repeat') as HTMLElement;

      expect(form.compareDocumentPosition(warning) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      // Nothing but the form lies between the form and its warning, so the two are on screen together.
      expect(form.nextElementSibling).toBe(warning);
    });

    it('moves focus to the warning, which brings it into view', () => {
      const { root } = submitRepeat();

      expect(document.activeElement).toBe(root.querySelector('.question-repeat'));
    });

    describe('bringing the warning wholly into view', () => {
      // jsdom has no layout and no scrollIntoView, so the page's geometry is given: where the warning is and how tall the screen is.
      let scrolled: ReturnType<typeof vi.fn>;

      function onAScreen(height: number, warning: { top: number; bottom: number }) {
        scrolled = vi.fn();
        Element.prototype.scrollIntoView = scrolled as unknown as Element['scrollIntoView'];
        vi.spyOn(document.documentElement, 'clientHeight', 'get').mockReturnValue(height);
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ ...warning, left: 0, right: 0, x: 0, y: warning.top, width: 0, height: warning.bottom - warning.top, toJSON: () => ({}) });
      }

      afterEach(() => {
        vi.restoreAllMocks();
        delete (Element.prototype as { scrollIntoView?: unknown }).scrollIntoView;
      });

      it('puts a warning that is only partly on screen in the middle of it, not flush against the edge', () => {
        onAScreen(720, { top: 450, bottom: 721 });

        const { root } = submitRepeat();

        expect(scrolled).toHaveBeenCalledTimes(1);
        expect(scrolled.mock.contexts[0]).toBe(root.querySelector('.question-repeat'));
        expect(scrolled).toHaveBeenCalledWith({ block: 'center' });
      });

      it('does the same for a warning above the top of the screen', () => {
        onAScreen(720, { top: -20, bottom: 250 });

        submitRepeat();

        expect(scrolled).toHaveBeenCalledWith({ block: 'center' });
      });

      it('leaves a warning that is already wholly on screen where it is', () => {
        onAScreen(720, { top: 300, bottom: 600 });

        const { root } = submitRepeat();

        expect(scrolled).not.toHaveBeenCalled();
        expect(document.activeElement).toBe(root.querySelector('.question-repeat'));
      });

      it('shows the top of a warning that is taller than the screen, so its heading is not lost', () => {
        onAScreen(600, { top: 100, bottom: 800 });

        submitRepeat();

        expect(scrolled).toHaveBeenCalledWith({ block: 'start' });
      });
    });

    it('names the accepted answers, and asks the bank about them, when the question is a text question', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      (fixture.componentInstance as unknown as { form: FormGroup }).form.controls['text'].setValue('Capital of France?');
      const type = root.querySelector<HTMLSelectElement>('#question-type') as HTMLSelectElement;
      type.value = 'text';
      type.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      const answer = root.querySelector<HTMLInputElement>('input[aria-label="Accepted answer 1"]') as HTMLInputElement;
      answer.value = 'Paris';
      answer.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      httpMock.expectOne(isCreate).flush({ title: 'duplicate_question' }, { status: 409, statusText: 'Conflict' });

      // A text question has no options, so what the bank compares are its accepted answers; asked about none, it would find nothing.
      const check = httpMock.expectOne(isCheck);
      expect(check.request.body).toEqual({ text: 'Capital of France?', options: ['Paris'] });
      check.flush([{ id: 'q1', preview: 'Capital of France?', sameOptions: true, status: 'approved', chapterId: null }]);
      fixture.detectChanges();

      const warning = root.querySelector('.question-repeat') as HTMLElement;
      expect(warning.textContent).toContain('these accepted answers');
      expect(warning.querySelectorAll('li').length).toBe(1);
    });

    it('asks the bank which questions it repeats, by the wording and the option texts it sent', () => {
      const fixture = create();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      fill(fixture, 'Capital of France?', ['Paris', 'Rome']);
      (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
      fixture.detectChanges();
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      httpMock.expectOne(isCreate).flush({ title: 'duplicate_question' }, { status: 409, statusText: 'Conflict' });

      const check = httpMock.expectOne(isCheck);

      expect(check.request.body).toEqual({ text: 'Capital of France?', options: ['Paris', 'Rome'] });
      check.flush([]);
    });

    it('adds the question when the author says to add it anyway', () => {
      const { fixture, root } = submitRepeat();

      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add it anyway') as HTMLButtonElement).click();

      const again = httpMock.expectOne(isCreate);
      expect(again.request.body.allowDuplicate).toBe(true);
      expect(again.request.body.text).toBe('Capital of France?');
      again.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      expect(root.textContent).toContain('Question saved.');
      expect(root.textContent).not.toContain('This question is already in the bank');
    });

    it('drops the question when the author chooses not to add it, keeping what they typed', () => {
      const { fixture, root } = submitRepeat();

      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === "Don't add it") as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(root.textContent).not.toContain('This question is already in the bank');
      expect(Array.from(root.querySelectorAll<HTMLInputElement>('.option-row input[type="text"]')).map((i) => i.value)).toEqual(['Paris', 'Rome']);
    });
  });

  describe('statistics on a question (FR-9)', () => {
    const isStats = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/q1/statistics');

    function open() {
      const fixture = create();
      httpMock.expectOne(isList).flush([listedQuestion('q1', 'What is 2 + 2?')]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const statistics = () => Array.from(root.querySelectorAll('button')).find((b) => b.getAttribute('aria-label') === 'Statistics') as HTMLButtonElement;
      return { fixture, root, statistics };
    }

    it('loads them when first opened and shows how often each option was chosen', () => {
      const { fixture, root, statistics } = open();

      statistics().click();
      httpMock.expectOne(isStats).flush({
        examCount: 1, examNames: ['Mock'], answered: 3, correct: 2, percentCorrect: 66.7,
        options: [{ id: 'q1-a', text: 'A', isCorrect: true, timesChosen: 2 }, { id: 'q1-b', text: 'B', isCorrect: false, timesChosen: 1 }],
      });
      fixture.detectChanges();

      const text = (root.querySelector('.question-row__statistics')?.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain('Answered 3 times, fully correct 2 (66.7%)');
      expect(text).toContain('A: chosen 2 times (correct)');
      expect(text).toContain('B: chosen 1 time');
    });

    it('says so when nobody has answered it, and does not load them again once they are in', () => {
      const { fixture, root, statistics } = open();

      statistics().click();
      httpMock.expectOne(isStats).flush({ examCount: 0, examNames: [], answered: 0, correct: 0, percentCorrect: null, options: [] });
      fixture.detectChanges();
      expect(root.textContent).toContain('No candidate has answered this question yet.');

      statistics().click();
      statistics().click();
      fixture.detectChanges();
      httpMock.expectNone(isStats);
    });

    it('shows why they could not be loaded on the question itself', () => {
      const { fixture, root, statistics } = open();

      statistics().click();
      httpMock.expectOne(isStats).flush({ title: 'forbidden', detail: 'No access.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();

      expect(root.querySelector('app-question-row .error-message')?.textContent).toContain('No access.');
    });
  });

  describe('deleting a question', () => {
    const isDelete = (id: string) => (r: { method: string; url: string }) => r.method === 'DELETE' && r.url.endsWith(`/v1/questions/${id}`);

    function open(questions: ReturnType<typeof listedQuestion>[]) {
      const fixture = create();
      httpMock.expectOne(isList).flush(questions);
      fixture.detectChanges();
      return { fixture, root: fixture.nativeElement as HTMLElement };
    }
    const cardOf = (root: HTMLElement, text: string) =>
      Array.from(root.querySelectorAll('app-question-row')).find((c) => c.textContent?.includes(text)) as HTMLElement;
    const confirmButtonIn = (card: HTMLElement) => card.querySelector('[role="alertdialog"] button.btn--danger') as HTMLButtonElement;
    const buttonIn = (card: HTMLElement, label: string) =>
      Array.from(card.querySelectorAll('button')).find((b) => (b.getAttribute('aria-label') ?? b.textContent?.trim()) === label) as HTMLButtonElement;

    it('removes the question from the list once the API has deleted it, and says so', () => {
      const { fixture, root } = open([listedQuestion('q1', 'First question'), listedQuestion('q2', 'Second question')]);

      buttonIn(cardOf(root, 'First question'), 'Delete').click();
      fixture.detectChanges();
      confirmButtonIn(cardOf(root, 'First question')).click();
      httpMock.expectOne(isDelete('q1')).flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();

      expect(root.textContent).not.toContain('First question');
      expect(root.textContent).toContain('Second question');
      expect(root.querySelector('[role="status"]')?.textContent).toContain('Question deleted.');
    });

    it('keeps the question and shows the reason on its own card when the API refuses', () => {
      const { fixture, root } = open([listedQuestion('q1', 'First question'), listedQuestion('q2', 'Second question')]);

      buttonIn(cardOf(root, 'Second question'), 'Delete').click();
      fixture.detectChanges();
      confirmButtonIn(cardOf(root, 'Second question')).click();
      httpMock
        .expectOne(isDelete('q2'))
        .flush({ title: 'question_in_use', detail: 'It is part of the exam "Maths mock", so it cannot be deleted.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(cardOf(root, 'Second question').querySelector('[role="alert"]')?.textContent).toContain('Maths mock');
      expect(cardOf(root, 'First question').querySelector('[role="alert"]')).toBeNull();
      expect(root.textContent).toContain('Second question');
    });

    it('does not offer to delete a question that an exam holds', () => {
      const { root } = open([listedQuestion('q1', 'Held question', { examCount: 1, examNames: ['Maths mock'], answered: false })]);

      // Unavailable but reachable, so its tooltip can say why: aria-disabled, and a click asks nothing.
      const icon = buttonIn(cardOf(root, 'Held question'), 'Delete');
      expect(icon.getAttribute('aria-disabled')).toBe('true');
      icon.click();
      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
    });
  });

  describe('filing questions under a chapter', () => {
    const isFile = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/placement');

    function open(questions: ReturnType<typeof listedQuestion>[]) {
      const fixture = create([MATHS, OLD_BOOK]);
      httpMock.expectOne(isList).flush(questions);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const choose = (scope: ParentNode, id: string, value: string) => {
        const select = scope.querySelector(`#${id}`) as HTMLSelectElement;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      };
      const tick = (index: number) => {
        const box = root.querySelectorAll<HTMLInputElement>('app-question-row input[type="checkbox"]')[index];
        box.checked = !box.checked;
        box.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      };
      return { fixture, root, choose, tick };
    }
    const bar = (root: HTMLElement) => root.querySelector('.bulk-bar') as HTMLElement | null;
    const barButton = (root: HTMLElement, label: string) =>
      Array.from((bar(root) as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.trim().startsWith(label)) as HTMLButtonElement;
    const result = { moved: 2, chapterId: 'c2', chapterTitle: 'Geometry', bookId: 'b1', bookName: 'Maths Grade 10' };

    it('shows no bulk bar until a question is ticked, then counts the ticked ones', () => {
      const { root, tick } = open([listedQuestion('q1', 'One'), listedQuestion('q2', 'Two'), listedQuestion('q3', 'Three')]);
      expect(bar(root)).toBeNull();

      tick(0);
      tick(2);

      expect(bar(root)?.textContent).toContain('2 selected');
    });

    it('ticks everything shown, and clears the selection again', () => {
      const { fixture, root } = open([listedQuestion('q1', 'One'), listedQuestion('q2', 'Two')]);

      const all = root.querySelector('.select-all input') as HTMLInputElement;
      all.checked = true;
      all.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(bar(root)?.textContent).toContain('2 selected');
      expect(root.querySelector('.select-all')?.textContent).toContain('Select all 2 shown');

      barButton(root, 'Clear selection').click();
      fixture.detectChanges();
      expect(bar(root)).toBeNull();
    });

    it('files the ticked questions in one request, says how many moved, and reads the list again', () => {
      const { fixture, root, choose, tick } = open([listedQuestion('q1', 'One'), listedQuestion('q2', 'Two'), listedQuestion('q3', 'Three')]);
      tick(0);
      tick(1);
      expect(barButton(root, 'File the 2 questions').disabled).toBe(true); // no chapter chosen yet

      choose(bar(root) as HTMLElement, 'bulk-book', 'b1');
      choose(bar(root) as HTMLElement, 'bulk-chapter', 'c2');
      expect(barButton(root, 'File the 2 questions').disabled).toBe(false);
      barButton(root, 'File the 2 questions').click();

      const post = httpMock.expectOne(isFile);
      expect(post.request.body).toEqual({ questionIds: ['q1', 'q2'], chapterId: 'c2' });
      post.flush(result);
      httpMock.expectOne(isList).flush([listedQuestion('q3', 'Three')]);
      fixture.detectChanges();

      expect(root.querySelector('[role="status"]')?.textContent).toContain('Filed 2 questions under Maths Grade 10 › Geometry.');
      expect(bar(root)).toBeNull();
      expect(root.textContent).not.toContain('One');
    });

    it('keeps the selection and shows the API’s reason when it refuses, so nothing is half done', () => {
      const { fixture, root, choose, tick } = open([listedQuestion('q1', 'One'), listedQuestion('q2', 'Two')]);
      tick(0);
      choose(bar(root) as HTMLElement, 'bulk-book', 'b1');
      choose(bar(root) as HTMLElement, 'bulk-chapter', 'c2');
      barButton(root, 'File the question').click();

      httpMock.expectOne(isFile).flush(
        { title: 'placement_refused', detail: 'The draft exam "Maths mock" only takes questions from other chapters. Nothing was moved.' },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(bar(root)?.querySelector('[role="alert"]')?.textContent).toContain('Nothing was moved.');
      expect(bar(root)?.textContent).toContain('1 selected');
    });

    it('files one question from its own card, and reads the list again', () => {
      const { fixture, root, choose } = open([listedQuestion('q1', 'One'), listedQuestion('q2', 'Two')]);
      const card = root.querySelectorAll('app-question-row')[1] as HTMLElement;

      (Array.from(card.querySelectorAll('button')).find((b) => b.getAttribute('aria-label')?.includes('File under')) as HTMLButtonElement).click();
      fixture.detectChanges();
      choose(card, 'file-q2-book', 'b1');
      choose(card, 'file-q2-chapter', 'c1');
      (Array.from(card.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'File question') as HTMLButtonElement).click();

      const post = httpMock.expectOne(isFile);
      expect(post.request.body).toEqual({ questionIds: ['q2'], chapterId: 'c1' });
      post.flush({ ...result, moved: 1, chapterId: 'c1', chapterTitle: 'Algebra' });
      httpMock.expectOne(isList).flush([listedQuestion('q1', 'One')]);
      fixture.detectChanges();

      expect(root.querySelector('[role="status"]')?.textContent).toContain('Filed 1 question under Maths Grade 10 › Algebra.');
    });

    it('shows a single question’s refusal on its own card', () => {
      const { fixture, root, choose } = open([listedQuestion('q1', 'One'), listedQuestion('q2', 'Two')]);
      const card = root.querySelectorAll('app-question-row')[0] as HTMLElement;
      (Array.from(card.querySelectorAll('button')).find((b) => b.getAttribute('aria-label')?.includes('File under')) as HTMLButtonElement).click();
      fixture.detectChanges();
      choose(card, 'file-q1-book', 'b1');
      choose(card, 'file-q1-chapter', 'c1');
      (Array.from(card.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'File question') as HTMLButtonElement).click();

      httpMock.expectOne(isFile).flush({ title: 'book_archived', detail: 'The chapter "Algebra" is archived.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(card.querySelector('[role="alert"]')?.textContent).toContain('archived');
      expect((root.querySelectorAll('app-question-row')[1] as HTMLElement).querySelector('[role="alert"]')).toBeNull();
    });

    it('says when everything chosen was already in that chapter', () => {
      const { fixture, root, choose, tick } = open([listedQuestion('q1', 'One')]);
      tick(0);
      choose(bar(root) as HTMLElement, 'bulk-book', 'b1');
      choose(bar(root) as HTMLElement, 'bulk-chapter', 'c2');
      barButton(root, 'File the question').click();

      httpMock.expectOne(isFile).flush({ ...result, moved: 0 });
      httpMock.expectOne(isList).flush([listedQuestion('q1', 'One')]);
      fixture.detectChanges();

      expect(root.querySelector('[role="status"]')?.textContent).toContain('already in Maths Grade 10 › Geometry');
    });
  });

  describe('the toolbar, the filters and the new-question form', () => {
    function open(books = [MATHS, OLD_BOOK], topics: string[] = ['fractions']) {
      const fixture = create(books, topics);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      return { fixture, root: fixture.nativeElement as HTMLElement };
    }
    const pick = (fixture: ComponentFixture<QuestionBank>, root: HTMLElement, id: string, value: string) => {
      const select = root.querySelector(`#${id}`) as HTMLSelectElement;
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const nextList = (fixture: ComponentFixture<QuestionBank>) => {
      const req = httpMock.expectOne(isList);
      req.flush([]);
      fixture.detectChanges();
      return req.request;
    };
    const chips = (root: HTMLElement) => Array.from(root.querySelectorAll('.filter-chip')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    const byLabel = (root: HTMLElement, label: string) => root.querySelector(`[aria-label="${label}"]`) as HTMLButtonElement;
    const byText = (root: HTMLElement, text: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;

    it('folds the form and the filter choices away until they are asked for, and shows no chips with no filter on', () => {
      const { root } = open();

      expect((root.querySelector('#new-question-form') as HTMLElement).hidden).toBe(true);
      expect((root.querySelector('#filters-panel') as HTMLElement).hidden).toBe(true);
      expect(root.querySelector('.filter-chip')).toBeNull();
      expect(root.querySelector('.icon-btn__count')).toBeNull();
    });

    it('opens the form from New question, and closes it again from its own icon, keeping what was typed', () => {
      const { fixture, root } = open();
      const form = root.querySelector('#new-question-form') as HTMLElement;

      byText(root, 'New question').click();
      fixture.detectChanges();
      expect(form.hidden).toBe(false);
      expect(byText(root, 'New question').getAttribute('aria-expanded')).toBe('true');

      (fixture.componentInstance as unknown as { form: FormGroup }).form.controls['text'].setValue('Half typed');
      byLabel(root, 'Close the form').click();
      fixture.detectChanges();
      expect(form.hidden).toBe(true);

      byText(root, 'New question').click();
      fixture.detectChanges();
      expect((fixture.componentInstance as unknown as { form: FormGroup }).form.controls['text'].value).toBe('Half typed');
    });

    it('opens the filter choices from the Filters icon, and says it is open', () => {
      const { fixture, root } = open();

      byLabel(root, 'Filters').click();
      fixture.detectChanges();

      expect((root.querySelector('#filters-panel') as HTMLElement).hidden).toBe(false);
      expect(byLabel(root, 'Filters').getAttribute('aria-expanded')).toBe('true');
      expect(byLabel(root, 'Filters').getAttribute('aria-controls')).toBe('filters-panel');
    });

    it('shows the filters that are on as chips, and how many on the Filters icon, even with the choices folded away', () => {
      const { fixture, root } = open();
      pick(fixture, root, 'filter-difficulty', 'hard');
      nextList(fixture);
      pick(fixture, root, 'filter-language', 'hi');
      nextList(fixture);

      expect(chips(root)).toEqual(['Difficulty: Hard', 'Language: Hindi']);
      expect(root.querySelector('.icon-btn__count')?.textContent?.trim()).toBe('2');
      expect((root.querySelector('#filters-panel') as HTMLElement).hidden).toBe(true);
    });

    it('names the book, the chapter, the status, the topic and the unfiled filter in chips', () => {
      const { fixture, root } = open();
      pick(fixture, root, 'filter-book', 'b1');
      nextList(fixture);
      pick(fixture, root, 'filter-chapter', 'c2');
      nextList(fixture);
      pick(fixture, root, 'filter-status', 'approved');
      nextList(fixture);
      pick(fixture, root, 'filter-topic', 'fractions');
      nextList(fixture);
      expect(chips(root)).toEqual(['Book: Maths Grade 10', 'Chapter: 2. Geometry', 'Status: Approved', 'Topic: fractions']);

      pick(fixture, root, 'filter-book', 'unfiled');
      nextList(fixture);
      expect(chips(root)).toEqual(['Not filed under a chapter', 'Status: Approved', 'Topic: fractions']);
    });

    it('takes a filter off from its chip, reading the list again without it', () => {
      const { fixture, root } = open();
      pick(fixture, root, 'filter-difficulty', 'hard');
      nextList(fixture);
      pick(fixture, root, 'filter-language', 'hi');
      nextList(fixture);

      byLabel(root, 'Remove filter: Difficulty: Hard').click();
      const request = nextList(fixture);

      expect(request.params.has('difficulty')).toBe(false);
      expect(request.params.get('language')).toBe('hi');
      expect(chips(root)).toEqual(['Language: Hindi']);
    });

    it('takes the chapter off with its book, as choosing another book does', () => {
      const { fixture, root } = open();
      pick(fixture, root, 'filter-book', 'b1');
      nextList(fixture);
      pick(fixture, root, 'filter-chapter', 'c1');
      nextList(fixture);

      byLabel(root, 'Remove filter: Book: Maths Grade 10').click();
      const request = nextList(fixture);

      expect(request.params.has('bookId')).toBe(false);
      expect(request.params.has('chapterId')).toBe(false);
      expect(chips(root)).toEqual([]);
    });

    it('takes every filter off at once, with one read of the list, and keeps the search', () => {
      const { fixture, root } = open();
      const search = root.querySelector('#filter-search') as HTMLInputElement;
      search.value = 'fraction';
      search.dispatchEvent(new Event('change'));
      nextList(fixture);
      pick(fixture, root, 'filter-difficulty', 'easy');
      nextList(fixture);
      pick(fixture, root, 'filter-language', 'mr');
      nextList(fixture);

      byText(root, 'Clear all').click();
      const request = nextList(fixture);

      expect(request.params.keys().sort()).toEqual(['q', 'take']);
      expect(request.params.get('q')).toBe('fraction');
      expect(chips(root)).toEqual([]);
      expect(root.querySelector('.icon-btn__count')).toBeNull();
    });

    it('offers Clear all only when more than one filter is on', () => {
      const { fixture, root } = open();
      pick(fixture, root, 'filter-difficulty', 'easy');
      nextList(fixture);

      expect(byText(root, 'Clear all')).toBeUndefined();
    });

    it('opens import and export in a dialog from its icon, and closes it again', () => {
      const { fixture, root } = open();
      expect(root.querySelector('app-modal-dialog')).toBeNull();

      byLabel(root, 'Import and export').click();
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog .modal__title')?.textContent?.trim()).toBe('Import and export');
      expect(root.querySelector('app-modal-dialog app-question-transfer')).not.toBeNull();

      (root.querySelector('app-modal-dialog .modal__close') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(root.querySelector('app-modal-dialog')).toBeNull();
    });

    it('gives every icon in the toolbar a name and a tooltip', () => {
      const { root } = open();

      for (const label of ['Filters', 'Import and export']) {
        const icon = byLabel(root, label);
        expect(icon).not.toBeNull();
        icon.dispatchEvent(new Event('focus'));
        expect(Array.from(document.querySelectorAll('.app-tooltip')).map((b) => b.textContent)).toContain(label);
        icon.dispatchEvent(new Event('blur'));
      }
    });
  });
});
