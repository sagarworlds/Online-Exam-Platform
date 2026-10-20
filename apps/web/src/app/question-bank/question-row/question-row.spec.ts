import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { QuestionDto } from '../question.models';
import { QuestionRow } from './question-row';

const question = (overrides: Partial<QuestionDto> = {}): QuestionDto => ({
  id: 'q1', text: '<p>Capital of France?</p>', createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapterId: 'c1', chapterTitle: 'Algebra', bookId: 'b1', bookName: 'Maths', classId: null, className: null,
  usage: { examCount: 0, examNames: [], answered: false },
  difficulty: null,
  topics: [],
  allowsMultiple: false,
  options: [
    { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
  ],
  ...overrides,
});

const chapter = (id: string, order: number, title: string) => ({ id, bookId: 'b1', title, order, isArchived: false, questionCount: 0 });
const MATHS: BookDto = {
  id: 'b1', name: 'Maths', classId: null, className: null, subject: null, description: null, isArchived: false, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapters: [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry')],
};

describe('QuestionRow', () => {
  let fixture: ComponentFixture<QuestionRow>;
  let root: HTMLElement;
  let deleted: string[];
  let selections: boolean[];
  let filed: { questionId: string; chapterId: string }[];
  let statisticsAsked: string[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QuestionRow], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] }).compileComponents();
  });

  afterEach(() => document.querySelectorAll('.app-tooltip').forEach((bubble) => bubble.remove()));

  function show(q: QuestionDto = question(), inputs: { busy?: boolean; error?: string | null; selected?: boolean } = {}) {
    fixture = TestBed.createComponent(QuestionRow);
    fixture.componentRef.setInput('question', q);
    fixture.componentRef.setInput('books', [MATHS]);
    if (inputs.selected !== undefined) fixture.componentRef.setInput('selected', inputs.selected);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    if (inputs.error !== undefined) fixture.componentRef.setInput('error', inputs.error);
    deleted = [];
    selections = [];
    filed = [];
    statisticsAsked = [];
    fixture.componentInstance.deleteConfirmed.subscribe((id) => deleted.push(id));
    fixture.componentInstance.selectionChanged.subscribe((value) => selections.push(value));
    fixture.componentInstance.fileRequested.subscribe((request) => filed.push(request));
    fixture.componentInstance.statisticsRequested.subscribe((id) => statisticsAsked.push(id));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  /** An icon is found by its name (its aria-label); a text button by its words. */
  const control = (label: string) =>
    Array.from(root.querySelectorAll<HTMLElement>('button, a')).find((el) => (el.getAttribute('aria-label') ?? el.textContent?.trim()) === label) as HTMLButtonElement;
  const press = (label: string) => {
    control(label).click();
    fixture.detectChanges();
  };
  const text = (selector: string) => root.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();

  it('shows where the question is filed and its text on the line, and keeps its options for when it is opened', () => {
    show();

    expect(text('.question-row__where')).toBe('Maths › Algebra');
    expect(root.querySelector('.question-row__text')?.textContent).toContain('Capital of France?');
    expect(root.querySelector('.correct-option')).toBeNull();

    press('Show details');

    expect(root.querySelector('.correct-option')?.textContent).toContain('Paris');
  });

  it('shows the class first, then the book and the chapter, when the book has a class', () => {
    show(question({ classId: 'k4', className: '4th', bookName: 'English', chapterTitle: 'Nouns' }));

    expect(text('.question-row__where')).toBe('4th › English › Nouns');
  });

  it('shows the language the question is written in', () => {
    show(question({ language: 'mr' }));

    expect(root.querySelector('.badge--language')?.textContent?.trim()).toBe('Marathi');
  });

  it('shows no language for a question from an API that predates languages', () => {
    show();

    expect(root.querySelector('.badge--language')).toBeNull();
  });

  it('says so when the question is not filed', () => {
    show(question({ chapterId: null, chapterTitle: null, bookId: null, bookName: null, classId: null, className: null }));

    expect(root.querySelector('.question-row__where')?.textContent).toContain('Not filed under a chapter');
  });

  it('shows the review status and the difficulty on the line', () => {
    show(question({ status: 'approved', difficulty: 'hard' }));

    expect(root.querySelector('.question-row__badges .badge--status-approved')?.textContent?.trim()).toBe('Approved');
    expect(root.querySelector('.question-row__badges .badge--difficulty-hard')?.textContent?.trim()).toBe('hard');
  });

  it('shows the formatting of the text and nothing executable', () => {
    show(question({ text: '<p>H<sub>2</sub>O</p><img src="x" onerror="window.__ran = true">' }));

    expect(root.querySelector('sub')?.textContent).toBe('2');
    expect(root.querySelector('[onerror]')).toBeNull();
  });

  describe('opening', () => {
    it('is closed at first: two lines of text, no options, and a chevron that says it can be opened', () => {
      show();

      expect(root.querySelector('.question-row__text--clamped')).not.toBeNull();
      expect(root.querySelector('.question-row__detail')).toBeNull();
      expect(control('Show details').getAttribute('aria-expanded')).toBe('false');
    });

    it('opens to the whole text and the options, names what it opened, and closes again', () => {
      show();

      press('Show details');

      const chevron = control('Hide details');
      expect(chevron.getAttribute('aria-expanded')).toBe('true');
      expect(root.querySelector(`#${chevron.getAttribute('aria-controls')}`)?.textContent).toContain('Paris');
      expect(root.querySelector('.question-row__text--clamped')).toBeNull();

      press('Hide details');
      expect(root.querySelector('.question-row__detail')).toBeNull();
    });

    it('says in the opened part when a question takes several answers, or a typed one', () => {
      show(question({ allowsMultiple: true }));
      press('Show details');
      expect(root.querySelector('.question-row__labels')?.textContent).toContain('Several answers');
    });

    it('shows the accepted answers of a text question when opened', () => {
      show(question({ isTextAnswer: true, acceptedAnswers: ['Paris', 'paris'], options: [] }));
      press('Show details');

      expect(root.querySelector('.question-row__labels')?.textContent).toContain('Text answer');
      expect(Array.from(root.querySelectorAll('.question-row__accepted .badge')).map((b) => b.textContent?.trim())).toEqual(['Paris', 'paris']);
    });

    it('shows each topic as a badge when opened, and no labels for a question with none', () => {
      show(question({ topics: ['fractions', 'ratios'] }));
      expect(root.querySelector('.question-row__labels')).toBeNull();
      press('Show details');
      expect(Array.from(root.querySelectorAll('.question-row__labels .badge')).map((b) => b.textContent?.trim())).toEqual(['fractions', 'ratios']);

      show();
      press('Show details');
      expect(root.querySelector('.question-row__labels')).toBeNull();
    });

    it('gives the kind of question to a screen reader and to a pointer, on its icon', () => {
      show(question({ isTextAnswer: true, acceptedAnswers: ['Paris'], options: [] }));

      const icon = root.querySelector('.question-row__type') as HTMLElement;
      expect(icon.getAttribute('role')).toBe('img');
      expect(icon.getAttribute('aria-label')).toBe('Text answer');
    });
  });

  describe('usage', () => {
    it('shows no usage for a question nothing uses', () => {
      show();
      press('Show details');

      expect(root.querySelector('.question-row__badges .badge--inactive')).toBeNull();
      expect(root.querySelector('.question-row__usage')).toBeNull();
    });

    it('says how many exams hold the question, and names them on hover', () => {
      show(question({ usage: { examCount: 2, examNames: ['Maths mock', 'Algebra test'], answered: false } }));

      const badge = root.querySelector('.question-row__badges .badge--inactive') as HTMLElement;
      expect(badge.textContent?.trim()).toBe('In 2 exams');
      expect(badge.getAttribute('title')).toBe('Maths mock, Algebra test');
    });

    it('says "exam" for one, and shows when candidates have answered once opened', () => {
      show(question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: true } }));

      expect(root.querySelector('.question-row__badges .badge--inactive')?.textContent?.trim()).toBe('In 1 exam');
      press('Show details');
      expect(root.querySelector('.question-row__usage .badge')?.textContent?.trim()).toBe('Answered by candidates');
    });
  });

  describe('the icons', () => {
    it('links to the edit page', () => {
      show();

      expect(root.querySelector('a[href="/admin/questions/q1/edit"]')?.getAttribute('aria-label')).toBe('Edit');
    });

    it('are named by their aria-label, each, so none is only a picture', () => {
      show();

      const names = Array.from(root.querySelectorAll('.question-row__actions > .icon-btn')).map((el) => el.getAttribute('aria-label'));
      expect(names).toEqual(['Edit', 'Translations', 'Statistics', 'File under…', 'Delete', 'Show details']);
    });

    it('show a tooltip with the same words when focused', () => {
      show();

      control('Statistics').dispatchEvent(new Event('focus'));

      expect(Array.from(document.querySelectorAll('.app-tooltip')).map((b) => b.textContent)).toEqual(['Statistics']);
    });
  });

  describe('translations', () => {
    it('open on request, opening the row, loading them only then, and pass on that one was added', () => {
      show(question({ language: 'en' }));
      const http = TestBed.inject(HttpTestingController);
      const isTranslations = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/q1/translations');
      let added = 0;
      fixture.componentInstance.translationAdded.subscribe(() => added++);
      expect(root.querySelector('app-question-translations')).toBeNull();
      http.expectNone(isTranslations);

      press('Translations');
      http.expectOne(isTranslations).flush([{ id: 'q1', language: 'en', preview: 'Capital of France?', status: 'draft' }]);
      fixture.detectChanges();

      expect(root.querySelector('.question-row__detail app-question-translations')).not.toBeNull();
      expect(root.textContent).toContain('Add Hindi translation');
      expect(added).toBe(0);

      press('Translations');
      expect(root.querySelector('app-question-translations')).toBeNull();
    });
  });

  describe('statistics', () => {
    it('open the row and ask the page for them once, and are not asked for again once they are in', () => {
      show();

      press('Statistics');
      expect(root.querySelector('.question-row__statistics')).not.toBeNull();
      expect(statisticsAsked).toEqual(['q1']);

      fixture.componentRef.setInput('statistics', { answered: 4, correct: 3, percentCorrect: 75, options: [{ id: 'o1', text: 'Paris', isCorrect: true, timesChosen: 3 }] });
      fixture.detectChanges();
      expect(root.querySelector('.question-row__statistics')?.textContent).toContain('Answered 4 times, fully correct 3 (75%)');

      press('Statistics');
      press('Statistics');
      expect(statisticsAsked).toEqual(['q1']);
    });
  });

  describe('deleting', () => {
    const confirm = () => root.querySelector('[role="alertdialog"] button.btn--danger') as HTMLButtonElement;

    it('asks first, on the line even while the row is closed, and only deletes once confirmed', () => {
      show();

      press('Delete');
      expect(root.querySelector('[role="alertdialog"]')?.textContent).toContain('Delete this question for good?');
      expect(deleted).toEqual([]);

      confirm().click();
      expect(deleted).toEqual(['q1']);
    });

    it('goes back to normal when cancelled, with nothing deleted', () => {
      show();

      press('Delete');
      press('Cancel');

      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(control('Delete').disabled).toBe(false);
      expect(deleted).toEqual([]);
    });

    it('is unavailable for a question that is in an exam, and says why in its tooltip and to a screen reader', () => {
      show(question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: false } }));
      const icon = control('Delete');

      expect(icon.getAttribute('aria-disabled')).toBe('true');
      expect(icon.getAttribute('aria-describedby')).toBe('delete-reason-q1');
      expect(root.querySelector('#delete-reason-q1')?.textContent).toContain('Cannot be deleted while it is in an exam.');
      icon.dispatchEvent(new Event('focus'));
      expect(document.querySelector('.app-tooltip')?.textContent).toBe('Cannot be deleted while it is in an exam.');
    });

    it('does not ask when the question is in an exam, however the icon is pressed', () => {
      show(question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: false } }));

      press('Delete');

      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(deleted).toEqual([]);
    });

    it('cannot be started while a request about the question is running', () => {
      show(question(), { busy: true });

      expect(control('Delete').disabled).toBe(true);
    });
  });

  describe('selecting', () => {
    const checkbox = () => root.querySelector('input[type="checkbox"]') as HTMLInputElement;

    it('reflects whether the page has it selected, and has an accessible name', () => {
      show(question(), { selected: true });

      expect(checkbox().checked).toBe(true);
      expect(root.querySelector('.inline-check')?.textContent).toContain('Select this question');
      expect(root.querySelector('.question-row--selected')).not.toBeNull();
    });

    it('tells the page when it is ticked and unticked', () => {
      show();

      checkbox().checked = true;
      checkbox().dispatchEvent(new Event('change'));
      checkbox().checked = false;
      checkbox().dispatchEvent(new Event('change'));

      expect(selections).toEqual([true, false]);
    });
  });

  describe('filing', () => {
    const choose = (id: string, value: string) => {
      const select = root.querySelector(`#${id}`) as HTMLSelectElement;
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    it('opens a book and chapter choice, and files only once a chapter is chosen', () => {
      show(question({ chapterId: null, chapterTitle: null, bookId: null, bookName: null, classId: null, className: null }));
      expect(root.querySelector('#file-q1-book')).toBeNull();

      press('File under…');
      expect(control('File question').disabled).toBe(true);

      choose('file-q1-book', 'b1');
      expect(control('File question').disabled).toBe(true); // a book alone is not a place
      choose('file-q1-chapter', 'c2');
      expect(control('File question').disabled).toBe(false);
      press('File question');

      expect(filed).toEqual([{ questionId: 'q1', chapterId: 'c2' }]);
    });

    it('narrows the books by class on the way to a chapter, and files under the chapter chosen', () => {
      const english = (id: string, classId: string, className: string, chapterId: string) => ({
        ...MATHS, id, name: 'English', classId, className, chapters: [{ ...chapter(chapterId, 1, 'Nouns'), bookId: id }],
      });
      show(question({ chapterId: null, chapterTitle: null, bookId: null, bookName: null, classId: null, className: null }));
      fixture.componentRef.setInput('books', [MATHS, english('e4', 'k4', '4th', 'e4-c1'), english('e5', 'k5', '5th', 'e5-c1')]);
      fixture.detectChanges();

      press('File under…');
      expect(Array.from(root.querySelectorAll('#file-q1-book option')).map((o) => o.textContent?.trim())).toEqual(['Choose a book…', '4th · English', '5th · English', 'Maths']);
      choose('file-q1-class', 'k5');
      expect(Array.from(root.querySelectorAll('#file-q1-book option')).map((o) => o.textContent?.trim())).toEqual(['Choose a book…', 'English']);
      choose('file-q1-book', 'e5');
      choose('file-q1-chapter', 'e5-c1');
      press('File question');

      expect(filed).toEqual([{ questionId: 'q1', chapterId: 'e5-c1' }]);
    });

    it('opens the row, so the choice is not hidden', () => {
      show();

      press('File under…');

      expect(root.querySelector('.question-row__detail #file-q1-book')).not.toBeNull();
    });

    it('closes and forgets the choice once the question has moved', () => {
      show();
      press('File under…');
      choose('file-q1-book', 'b1');
      choose('file-q1-chapter', 'c2');

      fixture.componentRef.setInput('question', question({ chapterId: 'c2', chapterTitle: 'Geometry' }));
      fixture.detectChanges();
      expect(root.querySelector('#file-q1-book')).toBeNull();

      press('File under…');
      expect((root.querySelector('#file-q1-book') as HTMLSelectElement).value).toBe('');
    });

    it('stays open when nothing about the question changed, such as after a refusal', () => {
      show();
      press('File under…');

      fixture.componentRef.setInput('error', 'The chapter is archived.');
      fixture.detectChanges();

      expect(root.querySelector('#file-q1-book')).not.toBeNull();
    });

    it('cannot be used while a request about the question is running', () => {
      show(question(), { busy: true });

      expect(control('File under…').disabled).toBe(true);
    });
  });

  it('shows why the last request failed, as an alert, on the line even while the row is closed', () => {
    show(question(), { error: 'It is part of the exam "Maths mock", so it cannot be deleted.' });

    expect(root.querySelector('.question-row__detail')?.getAttribute('id')).toBeNull();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('so it cannot be deleted');
    expect(control('Show details').getAttribute('aria-expanded')).toBe('false');
  });
});
