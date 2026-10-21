import { Component, ElementRef, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ExamApiService } from '../../exam-authoring/exam-api.service';
import { ExamDto } from '../../exam-authoring/exam.models';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
import { focusFirstInvalidFieldAfterRender } from '../../shared/focus-first-invalid-field';
import { extractErrorMessage } from '../../shared/problem-details';
import { BatchApiService } from '../batch-api.service';

@Component({
  selector: 'app-batch-create',
  imports: [ReactiveFormsModule, RouterLink, FormField, FieldControl, TranslatePipe],
  template: `
    <div class="page">
      <h1>{{ 'batches.new.title' | t }}</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
        <p class="hint">{{ 'batches.new.required' | t }}</p>

        <!-- An exam is chosen by name from the exams there are, not typed as an id nobody knows by heart. -->
        <app-form-field
          [label]="'batches.new.exam' | t"
          [required]="true"
          [hint]="'batches.new.examHint' | t"
          [error]="errorOf('examId', 'batches.new.examError')"
        >
          <select appFieldControl formControlName="examId">
            <option value="">{{ (examsState() === 'loading' ? 'batches.new.examsLoading' : 'batches.new.examChoose') | t }}</option>
            @for (exam of exams(); track exam.id) {
              <option [value]="exam.id">{{ examLabel(exam) }}</option>
            }
          </select>
        </app-form-field>
        @if (examsState() === 'failed') {
          <p class="warning-message" role="alert">
            {{ 'batches.new.examsFailed' | t }}
            <button type="button" class="btn btn--link" (click)="loadExams()">{{ 'batches.new.examsRetry' | t }}</button>
          </p>
        }
        @if (examsState() === 'ready' && exams().length === 0) {
          <p class="hint">
            {{ 'batches.new.examsNone' | t }}
            <a routerLink="/exams/create">{{ 'batches.new.examsNoneLink' | t }}</a>
          </p>
        }

        <app-form-field [label]="'batches.new.name' | t" [required]="true" [error]="errorOf('name', 'batches.new.nameError')">
          <input appFieldControl type="text" formControlName="name" />
        </app-form-field>

        <app-form-field [label]="'batches.new.description' | t" [optionalText]="'common.optional' | t">
          <textarea appFieldControl formControlName="description" rows="3"></textarea>
        </app-form-field>

        <app-form-field
          [label]="'batches.new.maxMembers' | t"
          [required]="true"
          [hint]="'batches.new.maxMembersHint' | t"
          [error]="errorOf('maxMembers', 'batches.new.maxMembersError')"
        >
          <input appFieldControl type="number" formControlName="maxMembers" min="1" />
        </app-form-field>

        @if (error()) {
          <div class="error-message" role="alert">{{ error() }}</div>
        }

        <div class="actions">
          <button type="submit" class="btn btn--primary" [attr.aria-busy]="loading()">
            {{ (loading() ? 'batches.new.submitting' : 'batches.new.submit') | t }}
          </button>
          <a routerLink="/batches" class="btn">{{ 'common.cancel' | t }}</a>
        </div>
      </form>
    </div>
  `,
})
export class BatchCreate implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
  private examApi = inject(ExamApiService);
  private router = inject(Router);
  private i18n = inject(I18nService);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private injector = inject(Injector);

  form!: FormGroup;
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  /** Whether Create has been pressed, so a field is named as wrong only from then on, not while it is still being filled in. */
  protected readonly attempted = signal(false);

  private readonly allExams = signal<ExamDto[]>([]);
  protected readonly examsState = signal<'loading' | 'ready' | 'failed'>('loading');
  /** The exams a batch can be made for, by name: one that has been archived is no longer sat, so it is not offered. */
  protected readonly exams = computed(() =>
    this.allExams()
      .filter((exam) => exam.status !== 'Archived')
      .sort((a, b) => a.name.localeCompare(b.name)),
  );

  ngOnInit() {
    this.form = this.fb.group({
      examId: ['', Validators.required],
      name: ['', Validators.required],
      description: [''],
      maxMembers: [50, [Validators.required, Validators.min(1)]],
    });
    this.loadExams();
  }

  protected loadExams() {
    this.examsState.set('loading');
    this.examApi.getExams().subscribe({
      next: (exams) => {
        this.allExams.set(exams);
        this.examsState.set('ready');
      },
      error: () => this.examsState.set('failed'),
    });
  }

  /** An exam's name, and whether it is a draft or published, since two exams may share a name. */
  protected examLabel(exam: ExamDto): string {
    return `${exam.name} (${this.i18n.t(`exams.status.${exam.status}` as MessageKey)})`;
  }

  /** The message for a field that is wrong, once Create has been pressed; null while it is right or not yet asked about. */
  protected errorOf(name: string, message: MessageKey): string | null {
    return this.attempted() && this.form?.get(name)?.invalid ? this.i18n.t(message) : null;
  }

  onSubmit() {
    this.attempted.set(true);
    if (this.form.invalid) {
      // Create is never switched off with no reason given: pressing it says what is wrong and goes to the first field that is.
      focusFirstInvalidFieldAfterRender(this.host, this.injector);
      return;
    }
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set('');

    const request = {
      examId: this.form.value.examId,
      name: this.form.value.name,
      description: this.form.value.description,
      maxMembers: parseInt(this.form.value.maxMembers),
    };

    this.batchApi.createBatch(request).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/batches']);
      },
      error: (err: unknown) => {
        this.error.set(extractErrorMessage(err, this.i18n.t('batches.new.failed')));
        this.loading.set(false);
      },
    });
  }
}
