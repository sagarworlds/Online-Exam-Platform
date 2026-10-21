import { Component, ElementRef, Injector, OnInit, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { BookApiService } from '../../book-management/book-api.service';
import { BookDto } from '../../book-management/book.models';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { focusFirstInvalidFieldAfterRender } from '../../shared/focus-first-invalid-field';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
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
  imports: [ReactiveFormsModule, RouterLink, ExamScopeFields, FormField, FieldControl, TranslatePipe],
  template: `
    <div class="page">
      <h1>{{ 'exams.new.title' | t }}</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
        <p class="hint">{{ 'exams.new.required' | t }}</p>

        <!-- The limits are the API's (255 and 1000 characters): the browser stops typing at them, so a longer name is never sent. -->
        <app-form-field [label]="'exams.new.name' | t" [required]="true" [error]="errorOf('name', 'exams.new.nameError')">
          <input appFieldControl type="text" formControlName="name" maxlength="255" />
        </app-form-field>

        <app-form-field [label]="'exams.new.description' | t" [optionalText]="'common.optional' | t">
          <textarea appFieldControl formControlName="description" maxlength="1000" rows="4"></textarea>
        </app-form-field>

        <app-form-field
          [label]="'exams.new.series' | t"
          [optionalText]="'common.optional' | t"
          [hint]="'exams.new.seriesHint' | t"
          [error]="errorOf('seriesId', 'exams.new.seriesError')"
        >
          <input appFieldControl type="text" formControlName="seriesId" />
        </app-form-field>

        @if (booksState() === 'ready') {
          <app-exam-scope-fields [books]="books()" [(scope)]="scope" />
          @if (attempted() && !scopeComplete()) {
            <p #scopeProblem class="field-error" role="alert" tabindex="-1">{{ 'exams.new.scopeError' | t }}</p>
          }
        } @else if (booksState() === 'unavailable') {
          <p class="hint">{{ 'exams.new.booksUnavailable' | t }}</p>
        }

        @if (error()) {
          <div class="error-message" role="alert">{{ error() }}</div>
        }

        <div class="actions">
          <button type="submit" class="btn btn--primary" [attr.aria-busy]="loading()">
            {{ (loading() ? 'exams.new.submitting' : 'exams.new.submit') | t }}
          </button>
          <a routerLink="/exams" class="btn">{{ 'common.cancel' | t }}</a>
        </div>
      </form>
    </div>
  `,
})
export class ExamBuilder implements OnInit {
  private fb = inject(FormBuilder);
  private examApi = inject(ExamApiService);
  private router = inject(Router);
  private bookApi = inject(BookApiService);
  private i18n = inject(I18nService);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private injector = inject(Injector);
  private readonly scopeProblem = viewChild<ElementRef<HTMLElement>>('scopeProblem');

  /** The books an exam can be limited to; 'unavailable' when the user may not read the question bank. */
  protected readonly books = signal<BookDto[]>([]);
  protected readonly booksState = signal<'loading' | 'ready' | 'unavailable'>('loading');
  protected readonly scope = signal(NO_SCOPE);
  protected readonly scopeComplete = computed(() => isScopeComplete(this.scope()));

  form!: FormGroup;
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  /** Whether Create has been pressed, so a field is named as wrong only from then on, not while it is still being filled in. */
  protected readonly attempted = signal(false);

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

  /** The message for a field that is wrong, once Create has been pressed; null while it is right or not yet asked about. */
  protected errorOf(name: string, message: MessageKey): string | null {
    return this.attempted() && this.form?.get(name)?.invalid ? this.i18n.t(message) : null;
  }

  onSubmit() {
    this.attempted.set(true);
    if (!this.form.valid || !this.scopeComplete()) {
      // Create is never switched off with no reason given: pressing it says what is wrong and goes to the first field that is, or to the
      // note under the book choice when that is what is left.
      if (this.form.valid) {
        afterNextRender(() => this.scopeProblem()?.nativeElement.focus(), { injector: this.injector });
      } else {
        focusFirstInvalidFieldAfterRender(this.host, this.injector);
      }
      return;
    }
    if (this.loading()) return;

    this.loading.set(true);
    this.error.set('');

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
        this.loading.set(false);
        // Straight to the editor, where sections and questions are added.
        this.router.navigate(['/exams', exam.id]);
      },
      error: (err: unknown) => {
        this.error.set(extractErrorMessage(err, this.i18n.t('exams.new.failed')));
        this.loading.set(false);
        console.error(err);
      },
    });
  }
}
