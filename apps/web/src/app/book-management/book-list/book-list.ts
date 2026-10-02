import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { BookApiService } from '../book-api.service';
import { BOOK_LIMITS, BookDto } from '../book.models';

/** Admin page: the books, and a form to add one. A book's chapters are managed on its own page (FR-5). */
@Component({
  selector: 'app-book-list',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './book-list.html',
})
export class BookList {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(BookApiService);
  private readonly router = inject(Router);

  protected readonly limits = BOOK_LIMITS;
  protected readonly books = signal<BookDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Archived books are loaded only when asked for, so the list an author works from stays short. */
  protected readonly showArchived = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(BOOK_LIMITS.name)]],
    subject: ['', Validators.maxLength(BOOK_LIMITS.subject)],
    description: ['', Validators.maxLength(BOOK_LIMITS.description)],
  });

  constructor() {
    this.refresh();
  }

  protected toggleArchived(): void {
    this.showArchived.update((shown) => !shown);
    this.refresh();
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    const { name, subject, description } = this.form.getRawValue();
    this.saving.set(true);
    this.errorMessage.set(null);

    this.api.create({ name, subject: subject.trim() || null, description: description.trim() || null }).subscribe({
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
