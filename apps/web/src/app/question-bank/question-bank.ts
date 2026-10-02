import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BookApiService } from '../book-management/book-api.service';
import { BookDto } from '../book-management/book.models';
import { extractErrorMessage } from '../shared/problem-details';
import { BookChapterPicker, isCompletePlacement, NO_PLACEMENT, Placement } from './book-chapter-picker/book-chapter-picker';
import { QuestionApiService } from './question-api.service';
import { QuestionCard } from './question-card/question-card';
import { createQuestionForm, newOption, toNewOptions } from './question-form';
import { QuestionFields } from './question-fields/question-fields';
import { CreateQuestionRequest, QuestionDto, QuestionFilter } from './question.models';

/** The value of the list filter's book select that means "questions not filed under any chapter". */
export const UNFILED = 'unfiled';

/**
 * Admin page: the newest questions, and a form to add one with its options and correct answer (FR-5). A question can
 * be filed under a chapter of a book, and the list can be narrowed to a book, a chapter, or what is not filed yet.
 */
@Component({
  selector: 'app-question-bank',
  imports: [ReactiveFormsModule, BookChapterPicker, QuestionCard, QuestionFields],
  templateUrl: './question-bank.html',
})
export class QuestionBank {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);

  protected readonly unfiled = UNFILED;
  protected readonly questions = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** What the last action on the list did, such as a deletion; announced to screen readers. */
  protected readonly notice = signal<string | null>(null);
  /** The question a request is running for, if any, so only its card is locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last request about a question failed, by question id, so the reason shows on that question's own card. */
  protected readonly cardErrors = signal<Record<string, string>>({});

  /** Every book, archived ones included: the list filter must reach questions filed under them. */
  protected readonly books = signal<BookDto[]>([]);

  /**
   * Where the new question is filed. A book needs a chapter, because a question belongs to a chapter, not a book.
   * It survives saving: an author enters many questions into one chapter in a row.
   */
  protected readonly placement = signal<Placement>(NO_PLACEMENT);
  protected readonly placementComplete = computed(() => isCompletePlacement(this.placement()));

  /** The list filter: a book id, {@link UNFILED}, or '' for everything; and a chapter id or ''. */
  protected readonly filterBook = signal('');
  protected readonly filterChapter = signal('');
  protected readonly filterChapters = computed(() => this.books().find((book) => book.id === this.filterBook())?.chapters ?? []);

  protected readonly form = createQuestionForm(this.formBuilder);

  constructor() {
    this.refresh();
    this.bookApi.list(true).subscribe({
      next: (books) => this.books.set(books),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  protected onFilterBookChanged(value: string): void {
    this.filterBook.set(value);
    this.filterChapter.set('');
    this.refresh();
  }

  protected onFilterChapterChanged(value: string): void {
    this.filterChapter.set(value);
    this.refresh();
  }

  protected submit(): void {
    if (this.form.invalid || !this.placementComplete() || this.saving()) {
      return;
    }

    const request: CreateQuestionRequest = {
      text: this.form.getRawValue().text,
      chapterId: this.placement().chapterId || null,
      options: toNewOptions(this.form),
    };

    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);

    this.api.create(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.set(true);
        this.resetForm();
        this.refresh();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected deleteQuestion(id: string): void {
    this.startAction(id);
    this.api.remove(id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.questions.update((list) => list.filter((question) => question.id !== id));
        this.notice.set('Question deleted.');
      },
      error: (error: unknown) => this.failAction(id, error),
    });
  }

  private startAction(id: string): void {
    this.busyId.set(id);
    this.notice.set(null);
    this.saved.set(false);
    this.cardErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([key]) => key !== id)));
  }

  private failAction(id: string, error: unknown): void {
    this.busyId.set(null);
    this.cardErrors.update((errors) => ({ ...errors, [id]: extractErrorMessage(error) }));
  }

  /** The filter the list is currently narrowed by; a chapter implies its book, so only one of them is sent. */
  private currentFilter(): QuestionFilter {
    const book = this.filterBook();
    if (book === UNFILED) {
      return { unfiled: true };
    }
    if (this.filterChapter()) {
      return { chapterId: this.filterChapter() };
    }
    return book ? { bookId: book } : {};
  }

  private refresh(): void {
    this.loading.set(true);
    this.api.list(this.currentFilter()).subscribe({
      next: (questions) => {
        this.questions.set(questions);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private resetForm(): void {
    this.form.reset({ text: '', correctIndex: -1 });
    this.form.controls.options.clear();
    this.form.controls.options.push(newOption(this.formBuilder));
    this.form.controls.options.push(newOption(this.formBuilder));
  }
}
