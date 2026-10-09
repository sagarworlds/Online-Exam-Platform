import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { BookApiService } from '../../book-management/book-api.service';
import { BookDto } from '../../book-management/book.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { CreateExamRequest } from '../exam.models';
import { NO_SCOPE, isScopeComplete, toScopeRequest } from '../exam-scope-fields/exam-scope';
import { ExamScopeFields } from '../exam-scope-fields/exam-scope-fields';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Accepts a blank value (no series) or a GUID. The API reads this field as a GUID and answers any other text
 * with an unhelpful "request could not be read", so the form says what is wrong before anything is sent.
 */
export function optionalGuid(control: AbstractControl): ValidationErrors | null {
  const value = String(control.value ?? '').trim();
  return value === '' || GUID_PATTERN.test(value) ? null : { guid: true };
}

@Component({
  selector: 'app-exam-builder',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, ExamScopeFields],
  template: `
    <div class="page">
      <h1>Create New Exam</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="field">
          <label for="name">Exam Name *</label>
          <!-- The limits are the API's (255 and 1000 characters): the browser stops typing at them, so a longer name is never sent. -->
          <input
            type="text"
            id="name"
            formControlName="name"
            placeholder="Enter exam name"
            maxlength="255"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <div class="field-error">Exam name is required</div>
          }
        </div>

        <div class="field">
          <label for="description">Description</label>
          <textarea
            id="description"
            formControlName="description"
            placeholder="Enter exam description"
            maxlength="1000"
            rows="4"
          ></textarea>
        </div>

        <div class="field">
          <label for="seriesId">Series ID (Optional)</label>
          <input
            type="text"
            id="seriesId"
            formControlName="seriesId"
            placeholder="Leave blank for a standalone exam"
          />
          @if (form.get('seriesId')?.hasError('guid')) {
            <div class="field-error">
              A series ID looks like 7c9e6679-7425-40de-944b-e07fc1f90ae7. Leave it blank for a standalone exam.
            </div>
          }
        </div>

        @if (booksState() === 'ready') {
          <app-exam-scope-fields [books]="books()" [(scope)]="scope" />
        } @else if (booksState() === 'unavailable') {
          <p class="hint">
            Limiting an exam to a book needs access to the question bank, so this exam can use questions from anywhere.
          </p>
        }

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || !scopeComplete() || loading" class="btn btn--primary">
            {{ loading ? 'Creating...' : 'Create Exam' }}
          </button>
          <a routerLink="/exams" class="btn">Cancel</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </form>
    </div>
  `
})
export class ExamBuilder implements OnInit {
  private fb = inject(FormBuilder);
  private examApi = inject(ExamApiService);
  private router = inject(Router);
  private bookApi = inject(BookApiService);

  /** The books an exam can be limited to; 'unavailable' when the user may not read the question bank. */
  protected readonly books = signal<BookDto[]>([]);
  protected readonly booksState = signal<'loading' | 'ready' | 'unavailable'>('loading');
  protected readonly scope = signal(NO_SCOPE);
  protected readonly scopeComplete = computed(() => isScopeComplete(this.scope()));

  form!: FormGroup;
  loading = false;
  error = '';

  ngOnInit() {
    this.form = this.fb.group({
      name: ['', Validators.required],
      description: [''],
      seriesId: ['', optionalGuid],
    });

    // Books are read separately from creating the exam: a user who may author exams but not read the question bank
    // still creates exams, just not ones limited to a book.
    this.bookApi.list().subscribe({
      next: (books) => {
        this.books.set(books);
        this.booksState.set('ready');
      },
      error: (err: unknown) => {
        console.error('Books could not be loaded for the scope choice', err);
        this.booksState.set('unavailable');
      },
    });
  }

  onSubmit() {
    if (!this.form.valid || !this.scopeComplete()) return;

    this.loading = true;
    this.error = '';

    // The API takes null for "no series" and refuses the empty string, which is what a
    // blank field holds, so a blank (or whitespace-only) value is sent as null.
    const seriesId = (this.form.value.seriesId ?? '').trim();

    const scope = this.scope();
    const request: CreateExamRequest = {
      name: this.form.value.name,
      description: this.form.value.description,
      seriesId: seriesId === '' ? null : seriesId,
      // Sent only when it limits something, so an unlimited exam is requested exactly as it always was.
      ...(scope.type === 'Independent' ? {} : { scope: toScopeRequest(scope) }),
    };

    this.examApi.createExam(request).subscribe({
      next: (exam) => {
        this.loading = false;
        // Straight to the editor, where sections and questions are added.
        this.router.navigate(['/exams', exam.id]);
      },
      error: (err: unknown) => {
        this.error = extractErrorMessage(err, 'Failed to create exam');
        this.loading = false;
        console.error(err);
      },
    });
  }
}
