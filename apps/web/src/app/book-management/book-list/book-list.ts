import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { BookApiService } from '../book-api.service';
import { compareNames } from '../book-class';
import { BOOK_LIMITS, BookDto } from '../book.models';
import { ClassApiService } from '../class-api.service';
import { ClassManager } from '../class-manager/class-manager';
import { ClassDto } from '../class.models';

/** The books filed under one class, for the heading above them; a null class stands for the books that have none. */
interface BookGroup {
  classId: string | null;
  name: string | null;
  archived: boolean;
  books: BookDto[];
}

/**
 * Admin page: the classes, the books grouped under them, and a form to add a book. A book's chapters are managed on its own
 * page (FR-5). A class is optional on a book, so the books without one are listed last, under "No class".
 */
@Component({
  selector: 'app-book-list',
  imports: [ReactiveFormsModule, RouterLink, ClassManager],
  templateUrl: './book-list.html',
})
export class BookList {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(BookApiService);
  private readonly classApi = inject(ClassApiService);
  private readonly router = inject(Router);

  protected readonly limits = BOOK_LIMITS;
  protected readonly books = signal<BookDto[]>([]);
  /** Every class, archived ones included: the manager lists them all and the book form offers the open ones. */
  protected readonly classes = signal<ClassDto[]>([]);
  protected readonly openClasses = computed(() => this.classes().filter((item) => !item.isArchived).sort((a, b) => compareNames(a.name, b.name)));
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Archived books are loaded only when asked for, so the list an author works from stays short. */
  protected readonly showArchived = signal(false);

  /** The books under a heading per class, in natural order of the class's name, and the books without a class last. */
  protected readonly groups = computed<BookGroup[]>(() => {
    const archivedIds = new Set(this.classes().filter((item) => item.isArchived).map((item) => item.id));
    const byClass = new Map<string, BookGroup>();
    const withoutClass: BookDto[] = [];
    for (const book of this.books()) {
      if (!book.classId) {
        withoutClass.push(book);
        continue;
      }

      const group = byClass.get(book.classId) ?? { classId: book.classId, name: book.className ?? '', archived: archivedIds.has(book.classId), books: [] };
      group.books.push(book);
      byClass.set(book.classId, group);
    }

    const groups = [...byClass.values()].sort((a, b) => compareNames(a.name ?? '', b.name ?? ''));
    return withoutClass.length > 0 ? [...groups, { classId: null, name: null, archived: false, books: withoutClass }] : groups;
  });
  /** Headings only help once some book has a class; until then the list is the plain list it always was. */
  protected readonly grouped = computed(() => this.groups().some((group) => group.classId !== null));

  protected readonly form = this.formBuilder.nonNullable.group({
    classId: '',
    name: ['', [Validators.required, Validators.maxLength(BOOK_LIMITS.name)]],
    subject: ['', Validators.maxLength(BOOK_LIMITS.subject)],
    description: ['', Validators.maxLength(BOOK_LIMITS.description)],
  });

  constructor() {
    this.refresh();
    this.loadClasses();
  }

  protected toggleArchived(): void {
    this.showArchived.update((shown) => !shown);
    this.refresh();
  }

  /** A class was added, renamed, archived or restored. Its books carry its name, so both lists are read again. */
  protected onClassesChanged(): void {
    this.loadClasses();
    this.refresh();
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    const { classId, name, subject, description } = this.form.getRawValue();
    this.saving.set(true);
    this.errorMessage.set(null);

    this.api.create({ name, classId: classId || null, subject: subject.trim() || null, description: description.trim() || null }).subscribe({
      next: (book) => {
        this.saving.set(false);
        // Straight to the new book, where its chapters are added.
        void this.router.navigate(['/admin/books', book.id]);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private loadClasses(): void {
    this.classApi.list(true).subscribe({
      next: (classes) => {
        this.classes.set(classes);
        // A class that was archived meanwhile is no longer offered, so it cannot stay chosen for the new book.
        if (!this.openClasses().some((item) => item.id === this.form.controls.classId.value)) {
          this.form.controls.classId.setValue('');
        }
      },
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  private refresh(): void {
    this.loading.set(true);
    this.api.list(this.showArchived()).subscribe({
      next: (books) => {
        this.books.set(books);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
