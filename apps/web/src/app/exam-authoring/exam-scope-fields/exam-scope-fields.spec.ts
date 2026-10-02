import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BookDto } from '../../book-management/book.models';
import { NO_SCOPE, ScopeSelection } from './exam-scope';
import { ExamScopeFields } from './exam-scope-fields';

const chapter = (id: string, order: number, title: string, isArchived = false) => ({ id, bookId: 'b1', title, order, isArchived, questionCount: 0 });
const book = (id: string, name: string, chapters = [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry'), chapter('c3', 3, 'Old', true)], isArchived = false): BookDto => ({
  id, name, subject: null, description: null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
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

  it('cannot be changed while disabled', () => {
    const { fixture, host, root } = open();

    host.disabled.set(true);
    fixture.detectChanges();

    expect(root.querySelector('fieldset')?.disabled).toBe(true);
  });
});
