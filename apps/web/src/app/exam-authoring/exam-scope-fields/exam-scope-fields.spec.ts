import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BookDto } from '../../book-management/book.models';
import { NO_SCOPE, ScopeSelection } from './exam-scope';
import { ExamScopeFields } from './exam-scope-fields';

const chapter = (id: string, order: number, title: string, isArchived = false) => ({ id, bookId: 'b1', title, order, isArchived, questionCount: 0 });
const book = (
  id: string,
  name: string,
  chapters = [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry'), chapter('c3', 3, 'Old', true)],
  isArchived = false,
  schoolClass: { id: string; name: string } | null = null,
): BookDto => ({
  id, name, subject: null, description: null, classId: schoolClass?.id ?? null, className: schoolClass?.name ?? null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
});

@Component({
  imports: [ExamScopeFields],
  template: `<app-exam-scope-fields [books]="books()" [(scope)]="scope" [disabled]="disabled()" [hideLegend]="hideLegend()" />`,
})
class Host {
  readonly books = signal<BookDto[]>([book('b1', 'Maths'), book('b2', 'Physics', [{ ...chapter('c9', 1, 'Optics'), bookId: 'b2' }]), book('b3', 'Archived', [], true)]);
  readonly scope = signal<ScopeSelection>({ ...NO_SCOPE });
  readonly disabled = signal(false);
  readonly hideLegend = signal(false);
}

describe('ExamScopeFields', () => {
  function open() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  const radio = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).find((r) => r.parentElement?.textContent?.includes(label)) as HTMLInputElement;

  it('shows its legend, or keeps it for screen readers only when the page already has a heading', () => {
    const { fixture, host, root } = open();
    const legend = root.querySelector('legend') as HTMLElement;
    expect(legend.classList.contains('visually-hidden')).toBe(false);

    host.hideLegend.set(true);
    fixture.detectChanges();

    expect(legend.classList.contains('visually-hidden')).toBe(true);
    expect(legend.textContent).toContain('Questions come from');
  });

  it('starts on "anywhere", with no book or chapters offered', () => {
    const { root } = open();

    expect(radio(root, 'Anywhere in the question bank').checked).toBe(true);
    expect(root.querySelector('select')).toBeNull();
    expect(root.querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('offers the open books for a book scope, never an archived one', () => {
    const { fixture, root } = open();

    radio(root, 'One whole book').click();
    fixture.detectChanges();

    expect(Array.from(root.querySelectorAll('select option')).map((o) => o.textContent?.trim())).toEqual(['Choose a book…', 'Maths', 'Physics']);
  });

  it('reports the choice to its parent as it is made', () => {
    const { fixture, host, root } = open();

    radio(root, 'Chosen chapters of a book').click();
    fixture.detectChanges();
    const select = root.querySelector('select') as HTMLSelectElement;
    select.value = 'b1';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const boxes = root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    boxes[0].click();
    boxes[1].click();
    fixture.detectChanges();

    expect(host.scope()).toEqual({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1', 'c2'] });
    expect(Array.from(boxes).map((b) => b.parentElement?.textContent?.trim())).toEqual(['1. Algebra', '2. Geometry']);
  });

  it('unticking a chapter removes it', () => {
    const { fixture, host, root } = open();
    host.scope.set({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1', 'c2'] });
    fixture.detectChanges();

    root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[0].click();
    fixture.detectChanges();

    expect(host.scope().chapterIds).toEqual(['c2']);
  });

  it('clears what no longer applies when the kind of limit changes', () => {
    const { fixture, host, root } = open();
    host.scope.set({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1'] });
    fixture.detectChanges();

    radio(root, 'One whole book').click();
    fixture.detectChanges();
    expect(host.scope()).toEqual({ type: 'Book', bookId: 'b1', chapterIds: [] });

    radio(root, 'Anywhere in the question bank').click();
    fixture.detectChanges();
    expect(host.scope()).toEqual({ type: 'Independent', bookId: '', chapterIds: [] });
  });

  it('shows the chosen book as selected when editing an existing scope', () => {
    const { fixture, host, root } = open();

    host.scope.set({ type: 'Book', bookId: 'b2', chapterIds: [] });
    fixture.detectChanges();

    expect((root.querySelector('select') as HTMLSelectElement).value).toBe('b2');
  });

  it('says when a book has no open chapters', () => {
    const { fixture, host, root } = open();
    host.books.set([book('b1', 'Empty', [chapter('c3', 1, 'Old', true)])]);
    host.scope.set({ type: 'Chapters', bookId: 'b1', chapterIds: [] });
    fixture.detectChanges();

    expect(root.textContent).toContain('no open chapters');
  });

  it('says so when there are no open books at all', () => {
    const { fixture, host, root } = open();
    host.books.set([]);
    host.scope.set({ type: 'Book', bookId: '', chapterIds: [] });
    fixture.detectChanges();

    expect(root.textContent).toContain('no open books yet');
  });

  describe('class', () => {
    const FOURTH = { id: 'k4', name: '4th' };
    const FIFTH = { id: 'k5', name: '5th' };
    const english = (id: string, schoolClass: { id: string; name: string }, chapterTitle: string) =>
      book(id, 'English', [{ ...chapter(`${id}-c1`, 1, chapterTitle), bookId: id }, { ...chapter(`${id}-c2`, 2, 'Verbs'), bookId: id }], false, schoolClass);

    /** Maths has no class, English is in two classes, and the book of a third class is archived. */
    function openWithClasses() {
      const opened = open();
      opened.host.books.set([
        book('b1', 'Maths'),
        english('b4', FOURTH, 'Nouns'),
        english('b5', FIFTH, 'Poems'),
        book('b3', 'Old Grammar', [], true, { id: 'k3', name: '3rd' }),
      ]);
      opened.host.scope.set({ type: 'Book', bookId: '', chapterIds: [] });
      opened.fixture.detectChanges();
      return opened;
    }
    const classSelect = (root: HTMLElement) => root.querySelector('select[id$="-class"]') as HTMLSelectElement;
    const bookSelect = (root: HTMLElement) => root.querySelector('select[id$="-book"]') as HTMLSelectElement;
    const options = (select: HTMLSelectElement) => Array.from(select.options).map((o) => o.textContent?.trim());
    const pick = (fixture: ComponentFixture<Host>, select: HTMLSelectElement, value: string) => {
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    it('shows no class select while no book has a class', () => {
      const { fixture, host, root } = open();
      host.scope.set({ type: 'Book', bookId: '', chapterIds: [] });
      fixture.detectChanges();

      expect(bookSelect(root)).not.toBeNull();
      expect(classSelect(root)).toBeNull();
    });

    it('offers the classes that have an open book, in natural order, and "No class" for the books without one', () => {
      const { fixture, host, root } = openWithClasses();
      host.books.update((books) => [...books, book('b10', 'Science', [], false, { id: 'k10', name: '10th' }), book('b2', 'Science', [], false, { id: 'k2', name: '2nd' })]);
      fixture.detectChanges();

      // 3rd only has an archived book, so it is not offered.
      expect(options(classSelect(root))).toEqual(['Any class', '2nd', '4th', '5th', '10th', 'No class']);
    });

    it('does not offer "No class" when every open book has one', () => {
      const { fixture, host, root } = openWithClasses();
      host.books.set([english('b4', FOURTH, 'Nouns')]);
      fixture.detectChanges();

      expect(options(classSelect(root))).toEqual(['Any class', '4th']);
    });

    it('labels each book with its class while any class is chosen, the classes first and in order', () => {
      const { root } = openWithClasses();

      expect(classSelect(root).value).toBe('');
      expect(options(bookSelect(root))).toEqual(['Choose a book…', '4th · English', '5th · English', 'Maths']);
    });

    it('narrows the books to the class chosen, with plain names, and to those without a class', () => {
      const { fixture, root } = openWithClasses();

      pick(fixture, classSelect(root), 'k5');
      expect(options(bookSelect(root))).toEqual(['Choose a book…', 'English']);
      expect(Array.from(bookSelect(root).options).map((o) => o.value)).toEqual(['', 'b5']);

      pick(fixture, classSelect(root), 'none');
      expect(options(bookSelect(root))).toEqual(['Choose a book…', 'Maths']);
    });

    it('still emits only the type, the book and the chapters', () => {
      const { fixture, host, root } = openWithClasses();
      host.scope.set({ type: 'Chapters', bookId: '', chapterIds: [] });
      fixture.detectChanges();

      pick(fixture, classSelect(root), 'k4');
      pick(fixture, bookSelect(root), 'b4');
      root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[1].click();
      fixture.detectChanges();

      expect(host.scope()).toEqual({ type: 'Chapters', bookId: 'b4', chapterIds: ['b4-c2'] });
    });

    it('clears the book and its chapters when the class changes to one that does not hold the book', () => {
      const { fixture, host, root } = openWithClasses();
      host.scope.set({ type: 'Chapters', bookId: 'b4', chapterIds: ['b4-c1'] });
      fixture.detectChanges();

      pick(fixture, classSelect(root), 'k5');

      expect(host.scope()).toEqual({ type: 'Chapters', bookId: '', chapterIds: [] });
      expect(bookSelect(root).value).toBe('');
      expect(classSelect(root).value).toBe('k5');
    });

    it('keeps the book and its chapters when the class changes to the one that holds it, or to any class', () => {
      const { fixture, host, root } = openWithClasses();
      host.scope.set({ type: 'Chapters', bookId: 'b4', chapterIds: ['b4-c1'] });
      fixture.detectChanges();

      pick(fixture, classSelect(root), 'k4');
      expect(host.scope()).toEqual({ type: 'Chapters', bookId: 'b4', chapterIds: ['b4-c1'] });

      pick(fixture, classSelect(root), '');
      expect(host.scope()).toEqual({ type: 'Chapters', bookId: 'b4', chapterIds: ['b4-c1'] });
    });

    it('starts on the class of the book an existing scope names', () => {
      const { fixture, host, root } = openWithClasses();

      host.scope.set({ type: 'Book', bookId: 'b5', chapterIds: [] });
      fixture.detectChanges();

      expect(classSelect(root).value).toBe('k5');
      expect(bookSelect(root).value).toBe('b5');
      expect(options(bookSelect(root))).toEqual(['Choose a book…', 'English']);
    });

    it('starts on "No class" for an existing scope on a book without a class', () => {
      const { fixture, host, root } = openWithClasses();

      host.scope.set({ type: 'Book', bookId: 'b1', chapterIds: [] });
      fixture.detectChanges();

      expect(classSelect(root).value).toBe('none');
    });

    it('does not turn "any class" into the class of the book chosen under it', () => {
      const { fixture, root } = openWithClasses();

      pick(fixture, bookSelect(root), 'b4');

      expect(classSelect(root).value).toBe('');
      expect(options(bookSelect(root))).toContain('5th · English');
    });

    it('lets a book chosen from outside win over a class that does not hold it', () => {
      const { fixture, host, root } = openWithClasses();
      pick(fixture, classSelect(root), 'k4');

      host.scope.set({ type: 'Book', bookId: 'b5', chapterIds: [] });
      fixture.detectChanges();

      expect(classSelect(root).value).toBe('k5');
    });

    it('is not offered for an exam that draws from anywhere', () => {
      const { fixture, host, root } = openWithClasses();

      host.scope.set({ type: 'Independent', bookId: '', chapterIds: [] });
      fixture.detectChanges();

      expect(classSelect(root)).toBeNull();
    });
  });

  it('cannot be changed while disabled', () => {
    const { fixture, host, root } = open();

    host.disabled.set(true);
    fixture.detectChanges();

    expect(root.querySelector('fieldset')?.disabled).toBe(true);
  });
});
