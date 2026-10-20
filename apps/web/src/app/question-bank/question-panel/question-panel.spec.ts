import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { beforeAll, vi } from 'vitest';
import { BookDto } from '../../book-management/book.models';
import { NO_PLACEMENT, Placement } from '../book-chapter-picker/book-chapter-picker';
import { QuestionDto, QuestionLanguage, QuestionUsageDto } from '../question.models';
import { QuestionCreated, QuestionPanel } from './question-panel';

// The base URL differs between builds and the test environment, so requests are matched by their path.
const isCreate = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions');
const isCheck = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/duplicates');
const isGet = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/q1');
const isPut = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/questions/q1');

const chapter = (id: string, order: number, title: string, bookId = 'b1', isArchived = false) => ({ id, bookId, title, order, isArchived, questionCount: 0 });
const book = (id: string, name: string, chapters: ReturnType<typeof chapter>[], isArchived = false, schoolClass: { id: string; name: string } | null = null): BookDto => ({
  id, name, classId: schoolClass?.id ?? null, className: schoolClass?.name ?? null, subject: null, description: null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
});
const MATHS = book('b1', 'Maths Grade 10', [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry'), chapter('c3', 3, 'Old chapter', 'b1', true)]);
const OLD_BOOK = book('b2', 'Old Physics', [chapter('c9', 1, 'Optics', 'b2')], true);

const UNUSED: QuestionUsageDto = { examCount: 0, examNames: [], answered: false };
const question = (overrides: Partial<QuestionDto> = {}): QuestionDto => ({
  id: 'q1', text: '<p>Capital of France?</p>', createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapterId: 'c1', chapterTitle: 'Algebra', bookId: 'b1', bookName: 'Maths', classId: null, className: null,
  usage: UNUSED,
  difficulty: null,
  topics: [],
  allowsMultiple: false,
  options: [
    { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
  ],
  ...overrides,
});

/** The page around the panel: it shows the panel with `@if` and takes it away when it closes, as the question bank does. */
@Component({
  imports: [QuestionPanel],
  template: `
    @if (open()) {
      <app-question-panel
        [questionId]="questionId()"
        [books]="books()"
        [topics]="topics()"
        [(placement)]="placement"
        [(language)]="language"
        (created)="made.push($event)"
        (saved)="edited.push($event)"
        (closed)="open.set(false)"
      />
    }
  `,
})
class Host {
  readonly open = signal(true);
  readonly questionId = signal<string | null>(null);
  readonly books = signal<BookDto[]>([]);
  readonly topics = signal<string[]>([]);
  readonly placement = signal<Placement>(NO_PLACEMENT);
  readonly language = signal<QuestionLanguage>('en');
  readonly made: QuestionCreated[] = [];
  readonly edited: QuestionDto[] = [];
}

describe('QuestionPanel', () => {
  let httpMock: HttpTestingController;

  // jsdom has no layout, which ProseMirror asks about when it moves the cursor into view, as it does once the panel puts the cursor in the text.
  beforeAll(() => {
    const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, right: 0, bottom: 0, left: 0, toJSON: () => ({}) } as DOMRect;
    Range.prototype.getBoundingClientRect = () => rect;
    Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList;
    document.elementFromPoint = () => null;
  });
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  /** Opens the panel for a new question, or for the edit of `q1` when `edit` is given (answering the read of it). */
  function show(options: { books?: BookDto[]; topics?: string[]; language?: QuestionLanguage; edit?: QuestionDto | 'missing' } = {}) {
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.books.set(options.books ?? []);
    host.topics.set(options.topics ?? []);
    host.language.set(options.language ?? 'en');
    if (options.edit !== undefined) {
      host.questionId.set('q1');
    }
    fixture.detectChanges();
    if (options.edit === 'missing') {
      httpMock.expectOne(isGet).flush({ title: 'question_not_found', detail: 'No question matches the given id.' }, { status: 404, statusText: 'Not Found' });
    } else if (options.edit !== undefined) {
      httpMock.expectOne(isGet).flush(options.edit);
    }
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, host, root, ...helpers(fixture, root) };
  }

  function helpers(fixture: ComponentFixture<Host>, root: HTMLElement) {
    const form = () => (fixture.debugElement.query(By.directive(QuestionPanel)).componentInstance as unknown as { form: FormGroup }).form;
    const buttons = () => Array.from(root.querySelectorAll('button'));
    const button = (label: string) => buttons().find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
    const texts = () => Array.from(root.querySelectorAll<HTMLInputElement>('.option-row input[type="text"]')).map((input) => input.value);
    const type = (index: number, value: string) => {
      const input = root.querySelectorAll<HTMLInputElement>('.option-row input[type="text"]')[index];
      input.value = value;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const choose = (id: string, value: string) => {
      const select = root.querySelector(`#${id}`) as HTMLSelectElement;
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const pickCorrect = (index: number) => {
      (root.querySelectorAll('input[type="radio"]')[index] as HTMLInputElement).dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    /** Fills a multiple-choice question, through the controls the way an author would: the text through its form control, as it is a rich-text editor. */
    const fill = (text = 'Capital of France?', options = ['Paris', 'Rome'], correct = 0) => {
      form().controls['text'].setValue(text);
      const inputs = root.querySelectorAll<HTMLInputElement>('.option-row input[type="text"]');
      options.forEach((value, i) => {
        inputs[i].value = value;
        inputs[i].dispatchEvent(new Event('input'));
      });
      pickCorrect(correct);
    };
    const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const footer = () => root.querySelector('.modal__footer') as HTMLElement;
    return { form, button, buttons, texts, type, choose, pickCorrect, fill, submit, footer };
  }

  describe('adding a question', () => {
    it('is a sheet down the side named New question, with the form in it', () => {
      const { root } = show();

      const dialog = root.querySelector('dialog') as HTMLDialogElement;
      expect(dialog.classList.contains('modal--side')).toBe(true);
      expect(root.querySelector('.modal__title')?.textContent?.trim()).toBe('New question');
      expect(dialog.getAttribute('aria-labelledby')).toBe(root.querySelector('.modal__title')?.id);
      expect(root.querySelector('.modal__body form[aria-label="New question"]')).not.toBeNull();
      expect(root.querySelector('.modal__footer')?.textContent).toContain('Save and add another');
    });

    it('puts the cursor in the question’s text when it opens', () => {
      const focused = vi.spyOn(HTMLElement.prototype, 'focus');

      show();

      expect(focused.mock.contexts.some((element) => (element as HTMLElement).id === 'question-text')).toBe(true);
    });

    it('keeps Save disabled until every option has text and a correct option is chosen', () => {
      const { form, type, pickCorrect, button } = show();
      form().controls['text'].setValue('Q?');

      type(0, 'A');
      type(1, 'B');
      // No correct option chosen yet.
      expect(button('Save').disabled).toBe(true);

      pickCorrect(1);
      expect(button('Save').disabled).toBe(false);
      expect(button('Save and add another').disabled).toBe(false);

      type(1, '');
      expect(button('Save').disabled).toBe(true);
      expect(button('Save and add another').disabled).toBe(true);
    });

    it('posts the options with the chosen one marked correct, closes, and says a question was added', () => {
      const { fixture, host, root, fill, submit } = show();

      fill('Capital of France?', ['Rome', 'Paris'], 1);
      submit();

      const post = httpMock.expectOne(isCreate);
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
      fixture.detectChanges();

      expect(host.made).toEqual([{ another: false }]);
      expect(root.querySelector('app-question-panel')).toBeNull();
    });

    it('can be saved with its Save button, which is in the footer and so outside the form it belongs to', () => {
      const { fill, button, root } = show();
      fill();
      const submitted = vi.fn((event: Event) => event.preventDefault());
      (root.querySelector('form') as HTMLFormElement).addEventListener('submit', submitted);

      button('Save').click();

      expect(submitted).toHaveBeenCalledTimes(1);
      httpMock.expectOne(isCreate);
    });

    describe('Save and add another', () => {
      function saved() {
        const view = show({ books: [MATHS], language: 'hi' });
        view.choose('question-book', 'b1');
        view.choose('question-chapter', 'c2');
        view.fill('प्रश्न?', ['एक', 'दो'], 1);
        view.button('Save and add another').click();
        httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        view.fixture.detectChanges();
        return view;
      }

      it('adds the question and stays open for the next, which starts empty', () => {
        const { host, root, form, texts, button } = saved();

        expect(host.made).toEqual([{ another: true }]);
        expect(root.querySelector('app-question-panel')).not.toBeNull();
        expect(form().controls['text'].value).toBe('');
        expect(texts()).toEqual(['', '']);
        expect(button('Save').disabled).toBe(true);
      });

      it('says the question was saved, in the footer beside the buttons, as a status', () => {
        const { footer } = saved();

        const status = footer().querySelector('.success-message') as HTMLElement;
        expect(status.getAttribute('role')).toBe('status');
        expect(status.textContent).toContain('Question saved.');
      });

      it('keeps the language and the chapter for the next question, here and in the page that opened the panel', () => {
        const { host, root, form } = saved();

        expect(form().controls['language'].value).toBe('hi');
        expect((root.querySelector('#question-language') as HTMLSelectElement).value).toBe('hi');
        expect((root.querySelector('#question-chapter') as HTMLSelectElement).value).toBe('c2');
        expect(host.language()).toBe('hi');
        expect(host.placement()).toEqual({ bookId: 'b1', chapterId: 'c2' });
      });

      it('puts the cursor back in the question’s text', () => {
        const focused = vi.spyOn(HTMLElement.prototype, 'focus');

        saved();

        expect(focused.mock.contexts.filter((element) => (element as HTMLElement).id === 'question-text').length).toBeGreaterThanOrEqual(2);
      });

      it('takes a refusal away once the next save is accepted, so the two are never shown together', () => {
        const { fixture, root, fill, button, footer } = show();
        fill();
        button('Save and add another').click();
        httpMock.expectOne(isCreate).flush({ title: 'invalid_question', detail: 'Try again.' }, { status: 400, statusText: 'Bad Request' });
        fixture.detectChanges();
        expect(footer().querySelector('.error-message')).not.toBeNull();

        button('Save and add another').click();
        httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        fixture.detectChanges();

        expect(root.querySelector('.error-message')).toBeNull();
        expect(footer().textContent).toContain('Question saved.');
      });

      it('does not offer Save and add another when editing', () => {
        const { button } = show({ edit: question() });

        expect(button('Save and add another')).toBeUndefined();
        expect(button('Save changes')).toBeDefined();
      });
    });

    describe('language (FR-10)', () => {
      it('offers English, Hindi and Marathi for a new question, English first, and sends the one chosen', () => {
        const { host, root, fill, submit, fixture } = show();
        const select = root.querySelector('#question-language') as HTMLSelectElement;

        expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual(['English', 'Hindi', 'Marathi']);
        expect(select.value).toBe('en');

        select.value = 'mr';
        select.dispatchEvent(new Event('change'));
        fill('फ्रान्सची राजधानी?', ['पॅरिस', 'रोम'], 0);
        submit();

        const post = httpMock.expectOne(isCreate);
        expect(post.request.body.language).toBe('mr');
        post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        fixture.detectChanges();

        // An author enters many questions in one language in a row: the page keeps it for the next panel.
        expect(host.language()).toBe('mr');
      });

      it('starts in the language the page has kept', () => {
        const { root } = show({ language: 'mr' });

        expect((root.querySelector('#question-language') as HTMLSelectElement).value).toBe('mr');
      });
    });

    describe('difficulty and topics', () => {
      const field = (root: HTMLElement, id: string) => root.querySelector(`#${id}`) as HTMLInputElement & HTMLSelectElement;

      it('sends the difficulty and the cleaned topics a new question was given', () => {
        const { fixture, root, fill, submit } = show({ topics: ['fractions'] });

        fill('Q?', ['A', 'B'], 0);
        field(root, 'question-difficulty').value = 'hard';
        field(root, 'question-difficulty').dispatchEvent(new Event('change'));
        field(root, 'question-topics').value = ' Fractions, ratios ,FRACTIONS,, ';
        field(root, 'question-topics').dispatchEvent(new Event('input'));
        fixture.detectChanges();
        submit();

        const post = httpMock.expectOne(isCreate);
        expect(post.request.body).toMatchObject({ difficulty: 'hard', topics: ['fractions', 'ratios'] });
      });

      it('offers the topics already in use as suggestions', () => {
        const { root } = show({ topics: ['algebra', 'fractions'] });

        expect(Array.from(root.querySelectorAll('#question-topic-suggestions option')).map((o) => o.getAttribute('value'))).toEqual(['algebra', 'fractions']);
      });

      it('refuses more than five topics before anything is sent, and shows why although the details are folded', () => {
        const { fixture, root, fill, button } = show();
        fill();

        field(root, 'question-topics').value = 'a, b, c, d, e, f';
        field(root, 'question-topics').dispatchEvent(new Event('input'));
        fixture.detectChanges();

        expect(root.textContent).toContain('Use at most 5 topics.');
        expect((root.querySelector('#question-more') as HTMLElement).hidden).toBe(false);
        expect(button('Save').disabled).toBe(true);
      });
    });

    describe('where it is filed', () => {
      it('files a question under a chapter, and requires one once a book is chosen', () => {
        const { fixture, root, choose, fill, button, submit } = show({ books: [MATHS, OLD_BOOK] });

        // Archived books take nothing new, so they are not offered here.
        expect(Array.from(root.querySelectorAll('#question-book option')).map((o) => o.textContent?.trim())).toEqual(['Not filed under a book', 'Maths Grade 10']);

        fill('Solve x + 1 = 2', ['1', '2'], 0);
        expect(button('Save').disabled).toBe(false);

        choose('question-book', 'b1');
        // Only open chapters of that book are offered, and one must be chosen.
        expect(Array.from(root.querySelectorAll('#question-chapter option')).map((o) => o.textContent?.trim())).toEqual(['Choose a chapter…', '1. Algebra', '2. Geometry']);
        expect(button('Save').disabled).toBe(true);
        expect(button('Save and add another').disabled).toBe(true);

        choose('question-chapter', 'c2');
        expect(button('Save').disabled).toBe(false);
        submit();
        const post = httpMock.expectOne(isCreate);
        expect(post.request.body.chapterId).toBe('c2');
        post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        fixture.detectChanges();
      });

      it('keeps the choice in the page, so the next panel files into the same chapter without choosing it again', () => {
        const { fixture, host, choose, fill, submit } = show({ books: [MATHS] });
        choose('question-book', 'b1');
        choose('question-chapter', 'c2');
        fill();
        submit();
        httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        fixture.detectChanges();
        expect(host.placement()).toEqual({ bookId: 'b1', chapterId: 'c2' });

        host.open.set(true);
        fixture.detectChanges();

        const root = fixture.nativeElement as HTMLElement;
        expect((root.querySelector('#question-book') as HTMLSelectElement).value).toBe('b1');
        expect((root.querySelector('#question-chapter') as HTMLSelectElement).value).toBe('c2');
      });

      it('says when a chosen book has no open chapters, and where to add one', () => {
        const { root, choose } = show({ books: [book('b3', 'Empty', [])] });

        choose('question-book', 'b3');

        expect(root.textContent).toContain('This book has no open chapters');
        expect(root.querySelector('a[href="/admin/books/b3"]')).not.toBeNull();
      });

      describe('with classes', () => {
        const FOURTH = { id: 'k4', name: '4th' };
        const FIFTH = { id: 'k5', name: '5th' };
        const english = (id: string, schoolClass: { id: string; name: string }, chapters: string[]) =>
          book(id, 'English', chapters.map((title, i) => chapter(`${id}-c${i + 1}`, i + 1, title, id)), false, schoolClass);
        const BOOKS = [MATHS, english('b5', FIFTH, ['Poems']), english('b4', FOURTH, ['Nouns', 'Verbs'])];
        const optionTexts = (root: HTMLElement, id: string) => Array.from(root.querySelectorAll(`#${id} option`)).map((o) => o.textContent?.trim());

        it('offers no class select while no book has a class', () => {
          const { root } = show({ books: [MATHS, OLD_BOOK] });

          expect(root.querySelector('#question-class')).toBeNull();
        });

        it('puts a class select before the book, and files under the chapter reached through it', () => {
          const { root, choose, fill, submit } = show({ books: BOOKS });
          expect(Array.from(root.querySelectorAll('form[aria-label="New question"] select')).map((s) => s.id).slice(0, 2)).toEqual(['question-class', 'question-book']);
          expect(optionTexts(root, 'question-class')).toEqual(['Any class', '4th', '5th', 'No class']);

          choose('question-class', 'k4');
          expect(optionTexts(root, 'question-book')).toEqual(['Not filed under a book', 'English']);
          choose('question-book', 'b4');
          choose('question-chapter', 'b4-c2');
          fill('Which is a noun?', ['Run', 'Dog'], 1);
          submit();

          const post = httpMock.expectOne(isCreate);
          expect(post.request.body.chapterId).toBe('b4-c2');
          expect(Object.keys(post.request.body)).not.toContain('classId');
        });
      });
    });

    describe('what happened to Save is shown in the footer', () => {
      it('shows why a save was refused beside the buttons, and moves focus to it', () => {
        const { fixture, fill, submit, footer } = show();
        fill();
        submit();

        httpMock.expectOne(isCreate).flush({ title: 'invalid_question', detail: 'The question text must be at most 4000 characters.' }, { status: 400, statusText: 'Bad Request' });
        fixture.detectChanges();

        const refusal = footer().querySelector('.error-message') as HTMLElement;
        expect(refusal.getAttribute('role')).toBe('alert');
        expect(refusal.textContent).toContain('The question text must be at most 4000 characters.');
        expect(document.activeElement).toBe(refusal);
      });

      it('shows the refusal there too when the bank cannot say what the question repeats', () => {
        const { fixture, fill, submit, footer } = show();
        fill();
        submit();

        httpMock.expectOne(isCreate).flush({ title: 'duplicate_question', detail: 'The question bank already has a question with this wording.' }, { status: 409, statusText: 'Conflict' });
        httpMock.expectOne(isCheck).flush({ title: 'server_error' }, { status: 500, statusText: 'Server Error' });
        fixture.detectChanges();

        const refusal = footer().querySelector('.error-message') as HTMLElement;
        expect(refusal.textContent).toContain('The question bank already has a question with this wording.');
        expect(document.activeElement).toBe(refusal);
      });

      it('keeps what the author typed when a save is refused, and the panel open', () => {
        const { fixture, root, fill, submit, texts, host } = show();
        fill();
        submit();

        httpMock.expectOne(isCreate).flush({ title: 'invalid_question', detail: 'No.' }, { status: 400, statusText: 'Bad Request' });
        fixture.detectChanges();

        expect(texts()).toEqual(['Paris', 'Rome']);
        expect(root.querySelector('app-question-panel')).not.toBeNull();
        expect(host.made).toEqual([]);
      });
    });

    describe('a question the bank already has (FR-9)', () => {
      function submitRepeat(another = false) {
        const view = show();
        view.fill();
        if (another) {
          view.button('Save and add another').click();
        } else {
          view.submit();
        }
        httpMock
          .expectOne(isCreate)
          .flush({ title: 'duplicate_question', detail: 'The question bank already has a question with this wording.' }, { status: 409, statusText: 'Conflict' });
        httpMock
          .expectOne(isCheck)
          .flush([{ id: 'q1', preview: 'Capital of France?', sameOptions: true, status: 'approved', chapterId: null }]);
        view.fixture.detectChanges();
        return view;
      }

      it('shows what it repeats instead of an error, and sends nothing more until the author decides', () => {
        const { root } = submitRepeat();

        expect(root.textContent).toContain('This question is already in the bank');
        expect(root.textContent).toContain('Capital of France?');
        expect(root.querySelector('.error-message')).toBeNull();
      });

      it('puts the warning in the footer beside the buttons, where the author is looking, not above the long form', () => {
        const { root, footer } = submitRepeat();

        const warning = root.querySelector('.question-repeat') as HTMLElement;
        expect(footer().contains(warning)).toBe(true);
        expect((root.querySelector('form') as HTMLFormElement).compareDocumentPosition(warning) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      });

      it('moves focus to the warning, which brings it into view', () => {
        const { root } = submitRepeat();

        expect(document.activeElement).toBe(root.querySelector('.question-repeat'));
      });

      describe('bringing the warning wholly into view', () => {
        // jsdom has no layout and no scrollIntoView, so the page's geometry is given: where the warning is, how tall the screen is, and where
        // the footer it sits in (a box that scrolls) shows it, which is the whole screen unless a test says it is smaller.
        let scrolled: ReturnType<typeof vi.fn>;

        function onAScreen(height: number, warning: { top: number; bottom: number }, footer: { top: number; bottom: number } = { top: 0, bottom: height }) {
          scrolled = vi.fn();
          Element.prototype.scrollIntoView = scrolled as unknown as Element['scrollIntoView'];
          vi.spyOn(document.documentElement, 'clientHeight', 'get').mockReturnValue(height);
          vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
            const box = this.classList.contains('question-repeat') ? warning : footer;
            return { ...box, left: 0, right: 0, x: 0, y: box.top, width: 0, height: box.bottom - box.top, toJSON: () => ({}) };
          });
        }

        afterEach(() => {
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

        describe('when the footer it is in is smaller than the screen and scrolls', () => {
          it('brings in a warning that is on the screen but cut off by the footer’s edge', () => {
            onAScreen(720, { top: 450, bottom: 560 }, { top: 300, bottom: 500 });

            submitRepeat();

            expect(scrolled).toHaveBeenCalledWith({ block: 'center' });
          });

          it('leaves a warning that is wholly inside what the footer shows', () => {
            onAScreen(720, { top: 320, bottom: 480 }, { top: 300, bottom: 500 });

            submitRepeat();

            expect(scrolled).not.toHaveBeenCalled();
          });

          it('shows the top of a warning that is taller than what the footer shows', () => {
            onAScreen(720, { top: 280, bottom: 560 }, { top: 300, bottom: 500 });

            submitRepeat();

            expect(scrolled).toHaveBeenCalledWith({ block: 'start' });
          });
        });
      });
      it('names the accepted answers, and asks the bank about them, when the question is a text question', () => {
        const { fixture, root, form, submit } = show();
        form().controls['text'].setValue('Capital of France?');
        const type = root.querySelector<HTMLSelectElement>('#question-type') as HTMLSelectElement;
        type.value = 'text';
        type.dispatchEvent(new Event('change'));
        fixture.detectChanges();
        const answer = root.querySelector<HTMLInputElement>('input[aria-label="Accepted answer 1"]') as HTMLInputElement;
        answer.value = 'Paris';
        answer.dispatchEvent(new Event('input'));
        fixture.detectChanges();
        submit();
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
        const { fill, submit } = show();
        fill();
        submit();
        httpMock.expectOne(isCreate).flush({ title: 'duplicate_question' }, { status: 409, statusText: 'Conflict' });

        const check = httpMock.expectOne(isCheck);

        expect(check.request.body).toEqual({ text: 'Capital of France?', options: ['Paris', 'Rome'] });
        check.flush([]);
      });

      it('adds the question when the author says to add it anyway, and closes', () => {
        const { fixture, host, root, button } = submitRepeat();

        button('Add it anyway').click();

        const again = httpMock.expectOne(isCreate);
        expect(again.request.body.allowDuplicate).toBe(true);
        expect(again.request.body.text).toBe('Capital of France?');
        again.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        fixture.detectChanges();

        expect(host.made).toEqual([{ another: false }]);
        expect(root.querySelector('app-question-panel')).toBeNull();
      });

      it('stays open for the next question when it was added with Save and add another', () => {
        const { fixture, host, root, button, footer } = submitRepeat(true);

        button('Add it anyway').click();
        httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
        fixture.detectChanges();

        expect(host.made).toEqual([{ another: true }]);
        expect(root.querySelector('app-question-panel')).not.toBeNull();
        expect(footer().textContent).toContain('Question saved.');
        expect(footer().textContent).not.toContain('This question is already in the bank');
      });

      it('drops the question when the author chooses not to add it, keeping what they typed', () => {
        const { fixture, root, button, texts } = submitRepeat();

        button("Don't add it").click();
        fixture.detectChanges();

        expect(root.textContent).not.toContain('This question is already in the bank');
        expect(texts()).toEqual(['Paris', 'Rome']);
      });
    });
  });

  describe('asking before work is thrown away', () => {
    const escape = () => new Event('cancel', { cancelable: true });

    it('closes at once, with nothing to ask, when nothing has been typed', () => {
      const { fixture, root, button } = show();

      button('Cancel').click();
      fixture.detectChanges();

      expect(root.querySelector('app-question-panel')).toBeNull();
    });

    it('asks first when there is something typed, and keeps the panel open', () => {
      const { fixture, root, fill, button, footer } = show();
      fill();

      button('Cancel').click();
      fixture.detectChanges();

      const ask = footer().querySelector('[role="alertdialog"]') as HTMLElement;
      expect(ask.textContent).toContain('Throw away what you have typed?');
      expect(root.querySelector('app-question-panel')).not.toBeNull();
      expect(document.activeElement).toBe(ask);
      // The Save buttons give way to the question, so there is one thing to answer.
      expect(button('Save')).toBeUndefined();
    });

    it('goes on with the question when the author keeps editing, with what they typed intact', () => {
      const { fixture, root, fill, button, texts } = show();
      fill();
      button('Cancel').click();
      fixture.detectChanges();

      button('Keep editing').click();
      fixture.detectChanges();

      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(texts()).toEqual(['Paris', 'Rome']);
      expect(button('Save')).toBeDefined();
    });

    it('closes when the author chooses to discard', () => {
      const { fixture, root, fill, button, host } = show();
      fill();
      button('Cancel').click();
      fixture.detectChanges();

      button('Discard').click();
      fixture.detectChanges();

      expect(root.querySelector('app-question-panel')).toBeNull();
      expect(host.made).toEqual([]);
    });

    it('asks, rather than closes, when Escape is pressed or the Close button is used with something typed', () => {
      const { fixture, root, fill, button } = show();
      fill();
      const cancel = escape();

      (root.querySelector('dialog') as HTMLDialogElement).dispatchEvent(cancel);
      fixture.detectChanges();

      expect(cancel.defaultPrevented).toBe(true);
      expect(root.querySelector('[role="alertdialog"]')).not.toBeNull();

      button('Keep editing').click();
      fixture.detectChanges();
      expect(root.querySelector('[role="alertdialog"]')).toBeNull();

      (root.querySelector('.modal__close') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(root.querySelector('[role="alertdialog"]')).not.toBeNull();
      expect(root.querySelector('app-question-panel')).not.toBeNull();
    });

    it('lets Escape close it when nothing has been typed', () => {
      const { fixture, root } = show();
      const cancel = escape();

      (root.querySelector('dialog') as HTMLDialogElement).dispatchEvent(cancel);
      fixture.detectChanges();

      expect(cancel.defaultPrevented).toBe(false);
    });

    describe('counts as work, although no field was typed in', () => {
      const asksAfter = (change: (root: HTMLElement) => void) => {
        const { fixture, root, button } = show();
        expect(button('Cancel')).toBeDefined();

        change(root);
        fixture.detectChanges();
        button('Cancel').click();
        fixture.detectChanges();

        expect(root.querySelector('[role="alertdialog"]')).not.toBeNull();
      };

      it('pinning an option', () => asksAfter((root) => root.querySelector<HTMLButtonElement>('button.option-pin')!.click()));

      it('choosing the correct option', () =>
        asksAfter((root) => root.querySelectorAll<HTMLInputElement>('input[type="radio"]')[0].dispatchEvent(new Event('change'))));

      it('adding an option', () =>
        asksAfter((root) =>
          Array.from(root.querySelectorAll('button'))
            .find((b) => b.textContent?.trim() === 'Add option')!
            .click(),
        ));
    });

    it('has nothing to lose again once a question is added and the next one is begun', () => {
      const { fixture, root, fill, button } = show();
      fill();
      button('Save and add another').click();
      httpMock.expectOne(isCreate).flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
      fixture.detectChanges();

      button('Cancel').click();
      fixture.detectChanges();

      expect(root.querySelector('app-question-panel')).toBeNull();
    });

    it('says changed, not typed, when it is an edit that would be thrown away', () => {
      const { fixture, type, button, footer } = show({ edit: question() });
      type(0, 'Paris, France');

      button('Cancel').click();
      fixture.detectChanges();

      expect(footer().textContent).toContain('Throw away what you have changed?');
    });
  });

  describe('editing a question', () => {
    it('is a sheet named Edit question, which reads the question and shows it as it is stored, with where it is filed', () => {
      const { root, texts } = show({ edit: question() });

      expect(root.querySelector('.modal__title')?.textContent?.trim()).toBe('Edit question');
      expect(texts()).toEqual(['Paris', 'Rome']);
      expect(root.querySelector('.panel-where')?.textContent).toContain('Maths');
      expect(root.querySelector('.panel-where')?.textContent).toContain('Algebra');
      expect(root.querySelector('.question-lock-note')).toBeNull();
      // Where it is filed is not changed here, so there is no picker, and no language: another language is a translation.
      expect(root.querySelector('#question-book')).toBeNull();
      expect(root.querySelector('#question-language')).toBeNull();
    });

    it('shows Loading while the question is read, and no form yet', () => {
      const fixture = TestBed.createComponent(Host);
      fixture.componentInstance.questionId.set('q1');
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(root.textContent).toContain('Loading…');
      expect(root.querySelector('form')).toBeNull();

      httpMock.expectOne(isGet).flush(question());
      fixture.detectChanges();
      expect(root.querySelector('form')).not.toBeNull();
    });

    it('puts the cursor in the question’s text once it is shown', () => {
      const focused = vi.spyOn(HTMLElement.prototype, 'focus');

      show({ edit: question() });

      expect(focused.mock.contexts.some((element) => (element as HTMLElement).id === 'question-text')).toBe(true);
    });

    it('shows the class first when the book it is filed under has one', () => {
      const { root } = show({ edit: question({ classId: 'k4', className: '4th' }) });

      expect(root.querySelector('.panel-where')?.textContent?.replace(/\s+/g, ' ').trim()).toContain('4th › Maths › Algebra');
    });

    it('says when it is not filed under a chapter', () => {
      const { root } = show({ edit: question({ chapterId: null, chapterTitle: null, bookId: null, bookName: null }) });

      expect(root.querySelector('.panel-where')?.textContent).toContain('Not filed under a chapter');
    });

    it('sends the whole edit, naming the options it keeps by id and the new one without an id, then closes and hands the page the question', () => {
      const { fixture, host, root, type, button, submit } = show({ edit: question() });
      type(0, 'Paris, France');
      button('Add option').click();
      fixture.detectChanges();
      type(2, 'Madrid');

      submit();

      const put = httpMock.expectOne(isPut);
      expect(put.request.body).toEqual({
        text: '<p>Capital of France?</p>',
        difficulty: null,
        topics: [],
        allowsMultiple: false,
        isTextAnswer: false,
        acceptedAnswers: [],
        explanation: null,
        options: [
          { id: 'o1', text: 'Paris, France', isCorrect: true, isPinned: false },
          { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
          { id: null, text: 'Madrid', isCorrect: false, isPinned: false },
        ],
      });
      const updated = question({ options: [
        { id: 'o1', text: 'Paris, France', isCorrect: true, isPinned: false },
        { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
        { id: 'o9', text: 'Madrid', isCorrect: false, isPinned: false },
      ] });
      put.flush(updated);
      fixture.detectChanges();

      expect(host.edited).toEqual([updated]);
      expect(root.querySelector('app-question-panel')).toBeNull();
    });

    it('can change which option is correct before anyone has answered', () => {
      const { pickCorrect, submit } = show({ edit: question() });
      pickCorrect(1);

      submit();

      expect(httpMock.expectOne(isPut).request.body.options.map((o: { isCorrect: boolean }) => o.isCorrect)).toEqual([false, true]);
    });

    it('keeps Save changes disabled while an option is empty', () => {
      const { type, button } = show({ edit: question() });
      type(1, '');

      expect(button('Save changes').disabled).toBe(true);
    });

    it('locks the answer key and the option list once candidates have answered, but not the wording', () => {
      const { root, type, button, submit } = show({ edit: question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: true } }) });

      expect(root.querySelector('.question-lock-note')?.textContent).toContain('only its wording can change');
      expect(Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).every((radio) => radio.disabled)).toBe(true);
      expect(button('Add option').hasAttribute('disabled')).toBe(true);
      expect(root.textContent).toContain('In the exam Maths mock');

      type(0, 'Paris!');
      submit();
      expect(httpMock.expectOne(isPut).request.body.options[0]).toEqual({ id: 'o1', text: 'Paris!', isCorrect: true, isPinned: false });
    });

    it('says how many exams hold the question', () => {
      const { root } = show({ edit: question({ usage: { examCount: 7, examNames: ['A', 'B', 'C', 'D', 'E'], answered: false } }) });

      expect(root.textContent).toContain('In 7 exams');
      expect(root.textContent).toContain('and others');
    });

    it('shows the API’s reason when a candidate answered while the panel was open, locks the controls and keeps the typing', () => {
      const { fixture, root, type, pickCorrect, submit, texts, footer } = show({ edit: question() });
      type(0, 'Paris!');
      pickCorrect(1);
      submit();

      httpMock.expectOne(isPut).flush(
        { title: 'question_locked', detail: 'Candidates have already answered this question, so only its wording can change.' },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(footer().querySelector('.error-message')?.textContent).toContain('only its wording can change');
      expect(root.querySelector('.question-lock-note')).not.toBeNull();
      expect(texts()[0]).toBe('Paris!');
      expect(root.querySelector('app-question-panel')).not.toBeNull();
    });

    it('shows any other API error and leaves the form as it was', () => {
      const { fixture, root, submit, footer } = show({ edit: question() });
      submit();

      httpMock.expectOne(isPut).flush({ title: 'invalid_question', detail: 'Every option needs text.' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(footer().querySelector('.error-message')?.textContent).toContain('Every option needs text.');
      expect(root.querySelector('.question-lock-note')).toBeNull();
    });

    it('shows the labels the question has, opened, and sends them back changed', () => {
      const { fixture, root, submit } = show({ edit: question({ difficulty: 'easy', topics: ['fractions', 'ratios'] }) });
      const difficulty = root.querySelector('#question-difficulty') as HTMLSelectElement;
      const topics = root.querySelector('#question-topics') as HTMLInputElement;
      expect(difficulty.value).toBe('easy');
      expect(topics.value).toBe('fractions, ratios');
      expect((root.querySelector('#question-more') as HTMLElement).hidden).toBe(false);

      difficulty.value = 'hard';
      difficulty.dispatchEvent(new Event('change'));
      topics.value = 'Percentages';
      topics.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      submit();

      expect(httpMock.expectOne(isPut).request.body).toMatchObject({ difficulty: 'hard', topics: ['percentages'] });
    });

    it('opens a multiple-answer question with its ticks, and sends every tick back as correct', () => {
      const { root, submit } = show({
        edit: question({
          allowsMultiple: true,
          options: [
            { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
            { id: 'o2', text: 'Lyon', isCorrect: true, isPinned: false },
            { id: 'o3', text: 'Rome', isCorrect: false, isPinned: false },
          ],
        }),
      });
      const ticks = Array.from(root.querySelectorAll<HTMLInputElement>('.option-row input[type="checkbox"][aria-label$="is correct"]'));
      expect(ticks.map((t) => t.checked)).toEqual([true, true, false]);

      submit();

      const body = httpMock.expectOne(isPut).request.body;
      expect(body.allowsMultiple).toBe(true);
      expect(body.options.map((o: { isCorrect: boolean }) => o.isCorrect)).toEqual([true, true, false]);
    });

    it('sends a pinned option as pinned', () => {
      const { root, fixture, submit } = show({ edit: question() });

      root.querySelectorAll<HTMLButtonElement>('button.option-pin')[1].click();
      fixture.detectChanges();
      submit();

      expect(httpMock.expectOne(isPut).request.body.options.map((o: { isPinned: boolean }) => o.isPinned)).toEqual([false, true]);
    });

    it('locks the several-answers choice once candidates have answered', () => {
      const { root } = show({ edit: question({ allowsMultiple: true, usage: { examCount: 1, examNames: ['Maths mock'], answered: true } }) });

      expect((root.querySelector('.option-tools input[type="checkbox"]') as HTMLInputElement).disabled).toBe(true);
    });

    it('still lets the labels be changed once candidates have answered', () => {
      const { root } = show({ edit: question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: true }, difficulty: 'easy' }) });

      expect((root.querySelector('#question-difficulty') as HTMLSelectElement).disabled).toBe(false);
      expect((root.querySelector('#question-topics') as HTMLInputElement).disabled).toBe(false);
    });

    it('says so when the question cannot be loaded, with no form and only a way out', () => {
      const { root, button, buttons } = show({ edit: 'missing' });

      expect(root.textContent).toContain('No question matches the given id.');
      expect(root.querySelector('form')).toBeNull();
      expect(button('Save changes').disabled).toBe(true);
      expect(buttons().some((b) => b.textContent?.trim() === 'Cancel')).toBe(true);
    });
  });
});
