import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { extractErrorMessage } from '../../shared/problem-details';
import { BookApiService } from '../book-api.service';
import { compareNames } from '../book-class';
import { BOOK_LIMITS, BookDto, ChapterDto } from '../book.models';
import { ClassApiService } from '../class-api.service';
import { ClassDto } from '../class.models';

/** One entry of the class select; the book's own class stays on offer while it is archived, and nothing else archived does. */
interface ClassOption {
  id: string;
  label: string;
}

/**
 * Admin page for one book: its details and its chapters (FR-5). Every change goes to the API, which answers with the
 * book as it now stands, and that is what the page shows, so the screen never drifts from what is stored.
 * Books and chapters are archived rather than deleted, so nothing filed under them is ever lost.
 */
@Component({
  selector: 'app-book-detail',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './book-detail.html',
})
export class BookDetail {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(BookApiService);
  private readonly classApi = inject(ClassApiService);
  private readonly bookId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  protected readonly limits = BOOK_LIMITS;
  protected readonly book = signal<BookDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** The chapter whose title is being edited, if any. */
  protected readonly renamingId = signal<string | null>(null);

  /** Every class, archived ones included, so the book's own class can be told apart as archived. */
  private readonly classes = signal<ClassDto[]>([]);
  protected readonly classOptions = computed<ClassOption[]>(() => {
    const current = this.book();
    const options = this.classes()
      .filter((item) => !item.isArchived || item.id === current?.classId)
      .map((item) => ({ id: item.id, label: item.isArchived ? `${item.name} (archived)` : item.name }));
    // The classes may not have loaded (or the call failed); the book's class must still be there to keep on saving.
    if (current?.classId && !options.some((option) => option.id === current.classId)) {
      options.push({ id: current.classId, label: current.className ?? 'Unnamed class' });
    }

    return options.sort((a, b) => compareNames(a.label, b.label));
  });

  protected readonly totalQuestions = computed(() => this.book()?.chapters.reduce((sum, c) => sum + c.questionCount, 0) ?? 0);

  protected readonly detailsForm = this.formBuilder.nonNullable.group({
    classId: '',
    name: ['', [Validators.required, Validators.maxLength(BOOK_LIMITS.name)]],
    subject: ['', Validators.maxLength(BOOK_LIMITS.subject)],
    description: ['', Validators.maxLength(BOOK_LIMITS.description)],
  });
  protected readonly chapterForm = this.formBuilder.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(BOOK_LIMITS.chapterTitle)]],
  });
  protected readonly renameForm = this.formBuilder.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(BOOK_LIMITS.chapterTitle)]],
  });

  constructor() {
    if (this.bookId === null) {
      this.loading.set(false);
      this.errorMessage.set('No book was given.');
      return;
    }

    this.api.get(this.bookId).subscribe({
      next: (book) => this.show(book),
      error: (error: unknown) => this.fail(error),
    });
    this.classApi.list(true).subscribe({
      next: (classes) => this.classes.set(classes),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  protected saveDetails(): void {
    const book = this.book();
    if (book === null || this.detailsForm.invalid) {
      return;
    }

    const { classId, name, subject, description } = this.detailsForm.getRawValue();
    this.change(
      this.api.update(book.id, { name, classId: classId || null, subject: subject.trim() || null, description: description.trim() || null }),
      () => this.saved.set(true),
    );
  }

  protected addChapter(): void {
    const book = this.book();
    if (book === null || this.chapterForm.invalid) {
      return;
    }

    this.change(this.api.addChapter(book.id, this.chapterForm.getRawValue().title), () => this.chapterForm.reset({ title: '' }));
  }

  protected startRename(chapter: ChapterDto): void {
    this.renamingId.set(chapter.id);
    this.renameForm.reset({ title: chapter.title });
  }

  protected cancelRename(): void {
    this.renamingId.set(null);
  }

  protected saveRename(chapter: ChapterDto): void {
    const book = this.book();
    if (book === null || this.renameForm.invalid) {
      return;
    }

    this.change(this.api.renameChapter(book.id, chapter.id, this.renameForm.getRawValue().title), () => this.renamingId.set(null));
  }

  protected setChapterArchived(chapter: ChapterDto, archived: boolean): void {
    const book = this.book();
    if (book !== null) {
      this.change(archived ? this.api.archiveChapter(book.id, chapter.id) : this.api.restoreChapter(book.id, chapter.id));
    }
  }

  protected setBookArchived(archived: boolean): void {
    const book = this.book();
    if (book !== null) {
      this.change(archived ? this.api.archive(book.id) : this.api.restore(book.id));
    }
  }

  // Runs one change and shows the book the API answers with; a refusal is shown and the page stays as it was.
  private change(call: Observable<BookDto>, afterSuccess?: () => void): void {
    this.busy.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);
    call.subscribe({
      next: (book) => {
        this.busy.set(false);
        this.show(book);
        afterSuccess?.();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private show(book: BookDto): void {
    this.loading.set(false);
    this.book.set(book);
    this.detailsForm.reset({ classId: book.classId ?? '', name: book.name, subject: book.subject ?? '', description: book.description ?? '' });
  }

  private fail(error: unknown): void {
    this.loading.set(false);
    this.errorMessage.set(extractErrorMessage(error));
  }
}
