import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { QuestionDto } from '../question.models';
import { QuestionCard } from './question-card';

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

const chapter = (id: string, order: number, title: string) => ({ id, bookId: 'b1', title, order, isArchived: false, questionCount: 0 });
const MATHS: BookDto = {
  id: 'b1', name: 'Maths', subject: null, description: null, isArchived: false, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapters: [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry')],
};

describe('QuestionCard', () => {
  let fixture: ComponentFixture<QuestionCard>;
  let root: HTMLElement;
  let deleted: string[];
  let selections: boolean[];
  let filed: { questionId: string; chapterId: string }[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QuestionCard], providers: [provideRouter([])] }).compileComponents();
  });

  function show(q: QuestionDto = question(), inputs: { busy?: boolean; error?: string | null; selected?: boolean } = {}) {
    fixture = TestBed.createComponent(QuestionCard);
    fixture.componentRef.setInput('question', q);
    fixture.componentRef.setInput('books', [MATHS]);
    if (inputs.selected !== undefined) fixture.componentRef.setInput('selected', inputs.selected);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    if (inputs.error !== undefined) fixture.componentRef.setInput('error', inputs.error);
    deleted = [];
    selections = [];
    filed = [];
    fixture.componentInstance.deleteConfirmed.subscribe((id) => deleted.push(id));
    fixture.componentInstance.selectionChanged.subscribe((value) => selections.push(value));
    fixture.componentInstance.fileRequested.subscribe((request) => filed.push(request));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const press = (label: string) => {
    button(label).click();
    fixture.detectChanges();
  };

  it('shows where the question is filed, its text, and its options with the correct one marked', () => {
    show();

    expect(root.querySelector('.question-card__where')?.textContent).toContain('Maths');
    expect(root.querySelector('.question-card__where')?.textContent).toContain('Algebra');
    expect(root.querySelector('.question-card__text')?.textContent).toContain('Capital of France?');
    expect(root.querySelector('.correct-option')?.textContent).toContain('Paris');
  });

  it('says so when the question is not filed', () => {
    show(question({ chapterId: null, chapterTitle: null, bookId: null, bookName: null }));

    expect(root.querySelector('.question-card__where')?.textContent).toContain('Not filed under a chapter');
  });

  it('shows the question’s formatting and nothing executable', () => {
    show(question({ text: '<p>H<sub>2</sub>O</p><img src="x" onerror="window.__ran = true">' }));

    expect(root.querySelector('sub')?.textContent).toBe('2');
    expect(root.querySelector('[onerror]')).toBeNull();
  });

  it('shows no usage for a question nothing uses', () => {
    show();

    expect(root.querySelector('.question-card__usage')).toBeNull();
  });

  it('says how many exams hold the question, and names them on hover', () => {
    show(question({ usage: { examCount: 2, examNames: ['Maths mock', 'Algebra test'], answered: false } }));

    const badge = root.querySelector('.question-card__usage .badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('In 2 exams');
    expect(badge.getAttribute('title')).toBe('Maths mock, Algebra test');
  });

  it('says "exam" for one, and shows when candidates have answered', () => {
    show(question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: true } }));

    const badges = Array.from(root.querySelectorAll('.question-card__usage .badge')).map((b) => b.textContent?.trim());
    expect(badges).toEqual(['In 1 exam', 'Answered by candidates']);
  });

  it('links to the edit page', () => {
    show();

    expect(root.querySelector('a[href="/admin/questions/q1/edit"]')?.textContent).toContain('Edit');
  });

  describe('deleting', () => {
    it('asks first, and only deletes once confirmed', () => {
      show();

      press('Delete');
      expect(root.querySelector('[role="alertdialog"]')?.textContent).toContain('Delete this question for good?');
      expect(deleted).toEqual([]);

      press('Delete'); // the confirming one
      expect(deleted).toEqual(['q1']);
    });

    it('goes back to normal when cancelled, with nothing deleted', () => {
      show();

      press('Delete');
      press('Cancel');

      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(button('Delete').disabled).toBe(false);
      expect(deleted).toEqual([]);
    });

    it('is disabled with its reason for a question that is in an exam', () => {
      show(question({ usage: { examCount: 1, examNames: ['Maths mock'], answered: false } }));

      expect(button('Delete').disabled).toBe(true);
      const reason = root.querySelector('#delete-reason-q1');
      expect(reason?.textContent).toContain('Cannot be deleted while it is in an exam.');
      expect(button('Delete').getAttribute('aria-describedby')).toBe('delete-reason-q1');
    });

    it('cannot be started while a request about the question is running', () => {
      show(question(), { busy: true });

      expect(button('Delete').disabled).toBe(true);
    });
  });

  describe('selecting', () => {
    const checkbox = () => root.querySelector('input[type="checkbox"]') as HTMLInputElement;

    it('reflects whether the page has it selected, and has an accessible name', () => {
      show(question(), { selected: true });

      expect(checkbox().checked).toBe(true);
      expect(root.querySelector('.inline-check')?.textContent).toContain('Select this question');
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
      show(question({ chapterId: null, chapterTitle: null, bookId: null, bookName: null }));
      expect(root.querySelector('#file-q1-book')).toBeNull();

      press('File under…');
      expect(button('File question').disabled).toBe(true);

      choose('file-q1-book', 'b1');
      expect(button('File question').disabled).toBe(true); // a book alone is not a place
      choose('file-q1-chapter', 'c2');
      expect(button('File question').disabled).toBe(false);
      press('File question');

      expect(filed).toEqual([{ questionId: 'q1', chapterId: 'c2' }]);
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

      expect(button('File under…').disabled).toBe(true);
    });
  });

  it('shows why the last request failed, as an alert', () => {
    show(question(), { error: 'It is part of the exam "Maths mock", so it cannot be deleted.' });

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('so it cannot be deleted');
  });
});
