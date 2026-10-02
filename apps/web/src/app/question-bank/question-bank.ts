import { Component, computed, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BookApiService } from '../book-management/book-api.service';
import { BookDto } from '../book-management/book.models';
import { extractErrorMessage } from '../shared/problem-details';
import { RichTextEditor } from '../shared/rich-text/rich-text-editor';
import { QuestionApiService } from './question-api.service';
import { CreateQuestionRequest, QUESTION_LIMITS, QuestionDto, QuestionFilter } from './question.models';

/** The value of the list filter's book select that means "questions not filed under any chapter". */
export const UNFILED = 'unfiled';

/**
 * Admin page: the newest questions, and a form to add one with its options and correct answer (FR-5). A question can
 * be filed under a chapter of a book, and the list can be narrowed to a book, a chapter, or what is not filed yet.
 */
@Component({
  selector: 'app-question-bank',
  imports: [ReactiveFormsModule, RichTextEditor, RouterLink],
  templateUrl: './question-bank.html',
})
export class QuestionBank {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);

  protected readonly limits = QUESTION_LIMITS;
  protected readonly unfiled = UNFILED;
  protected readonly questions = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** Every book, archived ones included: the list filter must reach questions filed under them. */
  protected readonly books = signal<BookDto[]>([]);
  /** Books a new question may be filed under: an archived book takes nothing new. */
  protected readonly openBooks = computed(() => this.books().filter((book) => !book.isArchived));

  /** The book chosen in the form, which decides the chapters offered there. */
  protected readonly formBookId = signal('');
  protected readonly formChapters = computed(
    () => this.books().find((book) => book.id === this.formBookId())?.chapters.filter((chapter) => !chapter.isArchived) ?? [],
  );

  /** The list filter: a book id, {@link UNFILED}, or '' for everything; and a chapter id or ''. */
  protected readonly filterBook = signal('');
  protected readonly filterChapter = signal('');
  protected readonly filterChapters = computed(() => this.books().find((book) => book.id === this.filterBook())?.chapters ?? []);

  protected readonly form = this.formBuilder.nonNullable.group({
    // Where the new question is filed. A book needs a chapter, because a question belongs to a chapter, not a book.
    bookId: [''],
    chapterId: [''],
    text: ['', Validators.required],
    // Which option is the right answer, as a radio value; -1 until the author picks one.
    correctIndex: [-1, Validators.min(0)],
    options: this.formBuilder.array([this.newOption(), this.newOption()]),
  });

  constructor() {
    this.refresh();
    this.bookApi.list(true).subscribe({
      next: (books) => this.books.set(books),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  protected onFormBookChanged(): void {
    const { bookId, chapterId } = this.form.controls;
    this.formBookId.set(bookId.value);
    chapterId.setValue('');
    chapterId.setValidators(bookId.value ? Validators.required : null);
    chapterId.updateValueAndValidity();
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

  protected get options(): FormArray {
    return this.form.controls.options;
  }

  protected addOption(): void {
    if (this.options.length < QUESTION_LIMITS.maxOptions) {
      this.options.push(this.newOption());
    }
  }

  protected removeOption(index: number): void {
    if (this.options.length <= QUESTION_LIMITS.minOptions) {
      return;
    }

    this.options.removeAt(index);
    // Keep the chosen answer pointing at the same option after the list shifts.
    const chosen = this.form.controls.correctIndex.value;
    if (chosen === index) {
      this.form.controls.correctIndex.setValue(-1);
    } else if (chosen > index) {
      this.form.controls.correctIndex.setValue(chosen - 1);
    }
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    const { text, correctIndex, options, chapterId } = this.form.getRawValue();
    const request: CreateQuestionRequest = {
      text,
      chapterId: chapterId || null,
      options: options.map((option: { text: string }, index: number) => ({
        text: option.text,
        isCorrect: index === correctIndex,
      })),
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
    // The book and chapter stay: an author enters many questions into one chapter in a row.
    const { bookId, chapterId } = this.form.getRawValue();
    this.form.reset({ bookId, chapterId, text: '', correctIndex: -1 });
    this.options.clear();
    this.options.push(this.newOption());
    this.options.push(this.newOption());
  }

  private newOption() {
    return this.formBuilder.nonNullable.group({ text: ['', Validators.required] });
  }
}
