import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FormGroup } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { QuestionBank } from './question-bank';
import { QuestionUsageDto } from './question.models';

// The base URL differs between builds and the test environment, so requests are matched by their path.
const isList = (r: { method: string; url: string }) => r.method === 'GET' && /\/v1\/questions(\?.*)?$/.test(r.url);
const isBooks = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/books');

const chapter = (id: string, order: number, title: string, isArchived = false) => ({ id, bookId: 'b1', title, order, isArchived, questionCount: 0 });
const book = (id: string, name: string, chapters: ReturnType<typeof chapter>[], isArchived = false) => ({
  id, name, subject: null, description: null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
});
const UNUSED: QuestionUsageDto = { examCount: 0, examNames: [], answered: false };
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

  afterEach(() => httpMock.verify());

  function create(books: ReturnType<typeof book>[] = []) {
    const fixture = TestBed.createComponent(QuestionBank);
    fixture.detectChanges();
    httpMock.expectOne(isBooks).flush(books);
    return fixture;
  }

  function fill(fixture: ComponentFixture<QuestionBank>, text: string, options: string[]): void {
    const root = fixture.nativeElement as HTMLElement;
    // The question text lives in a rich-text editor, so it is set through the form control the editor is bound to.
    (fixture.componentInstance as unknown as { form: FormGroup }).form.controls['text'].setValue(text);
    const inputs = root.querySelectorAll<HTMLInputElement>('input[type="text"]');
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
          { id: 'o1', text: '3', isCorrect: false },
          { id: 'o2', text: '4', isCorrect: true },
        ],
        createdBy: 'u1',
        createdAtUtc: '2026-10-02T00:00:00Z',
        chapterId: null,
        chapterTitle: null,
        bookId: null,
        bookName: null,
        usage: UNUSED,
      },
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('What is 2 + 2?');
    expect(text).toContain('correct');
    expect(text).toContain('Not filed under a chapter');
  });

  it('shows which book and chapter a question is filed under', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1', text: 'Solve x', options: [{ id: 'o1', text: '1', isCorrect: true }, { id: 'o2', text: '2', isCorrect: false }],
        createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', chapterId: 'c1', chapterTitle: 'Algebra', bookId: 'b1', bookName: 'Maths Grade 10', usage: UNUSED,
      },
    ]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.question-card__where')?.textContent).toContain('Maths Grade 10');
    expect((fixture.nativeElement as HTMLElement).querySelector('.question-card__where')?.textContent).toContain('Algebra');
  });

  it('shows the formatting of a question in the list, and nothing executable', () => {
    const w = window as unknown as { __ran?: boolean };
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1',
        text: '<p>Water is H<sub>2</sub>O</p><img src="x" onerror="window.__ran = true">',
        options: [
          { id: 'o1', text: 'Yes', isCorrect: true },
          { id: 'o2', text: 'No', isCorrect: false },
        ],
        createdBy: 'u1',
        createdAtUtc: '2026-10-02T00:00:00Z',
        usage: UNUSED,
      },
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.question-card sub')?.textContent).toBe('2');
    expect(root.querySelector('[onerror]')).toBeNull();
    expect(w.__ran).toBeUndefined();
  });

  it('keeps Save disabled until every option has text and a correct option is chosen', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const save = root.querySelector('button.primary') as HTMLButtonElement;

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
      options: [
        { text: 'Rome', isCorrect: false },
        { text: 'Paris', isCorrect: true },
      ],
    });
    post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    expect(root.textContent).toContain('Question saved.');
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
    const save = () => root.querySelector('button.primary') as HTMLButtonElement;

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
    expect(nextList().params.keys()).toEqual([]);
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

  it('adds options up to six and removes down to two', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const count = () => root.querySelectorAll('input[type="text"]').length;
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

  describe('deleting a question', () => {
    const listed = (id: string, text: string, usage = UNUSED) => ({
      id, text, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', chapterId: null, chapterTitle: null, bookId: null, bookName: null, usage,
      options: [{ id: `${id}-a`, text: 'A', isCorrect: true }, { id: `${id}-b`, text: 'B', isCorrect: false }],
    });
    const isDelete = (id: string) => (r: { method: string; url: string }) => r.method === 'DELETE' && r.url.endsWith(`/v1/questions/${id}`);

    function open(questions: ReturnType<typeof listed>[]) {
      const fixture = create();
      httpMock.expectOne(isList).flush(questions);
      fixture.detectChanges();
      return { fixture, root: fixture.nativeElement as HTMLElement };
    }
    const cardOf = (root: HTMLElement, text: string) =>
      Array.from(root.querySelectorAll('app-question-card')).find((c) => c.textContent?.includes(text)) as HTMLElement;
    const buttonIn = (card: HTMLElement, label: string) =>
      Array.from(card.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

    it('removes the question from the list once the API has deleted it, and says so', () => {
      const { fixture, root } = open([listed('q1', 'First question'), listed('q2', 'Second question')]);

      buttonIn(cardOf(root, 'First question'), 'Delete').click();
      fixture.detectChanges();
      buttonIn(cardOf(root, 'First question'), 'Delete').click(); // the confirming one
      httpMock.expectOne(isDelete('q1')).flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();

      expect(root.textContent).not.toContain('First question');
      expect(root.textContent).toContain('Second question');
      expect(root.querySelector('[role="status"]')?.textContent).toContain('Question deleted.');
    });

    it('keeps the question and shows the reason on its own card when the API refuses', () => {
      const { fixture, root } = open([listed('q1', 'First question'), listed('q2', 'Second question')]);

      buttonIn(cardOf(root, 'Second question'), 'Delete').click();
      fixture.detectChanges();
      buttonIn(cardOf(root, 'Second question'), 'Delete').click();
      httpMock
        .expectOne(isDelete('q2'))
        .flush({ title: 'question_in_use', detail: 'It is part of the exam "Maths mock", so it cannot be deleted.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(cardOf(root, 'Second question').querySelector('[role="alert"]')?.textContent).toContain('Maths mock');
      expect(cardOf(root, 'First question').querySelector('[role="alert"]')).toBeNull();
      expect(root.textContent).toContain('Second question');
    });

    it('does not offer to delete a question that an exam holds', () => {
      const { root } = open([listed('q1', 'Held question', { examCount: 1, examNames: ['Maths mock'], answered: false })]);

      expect(buttonIn(cardOf(root, 'Held question'), 'Delete').disabled).toBe(true);
    });
  });
});
