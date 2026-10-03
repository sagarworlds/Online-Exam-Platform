import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { QuestionApiService } from '../question-api.service';
import { QuestionFields } from '../question-fields/question-fields';
import { createQuestionForm, fillQuestionForm, toAllowsMultiple, toEditedOptions, toLabels } from '../question-form';
import { QuestionDto } from '../question.models';

/**
 * Admin page for editing one question (FR-5, FR-7). Before any candidate has answered it, everything can change. Once
 * one has, only the wording can: the correct option and the list of options are locked, because stored scores and
 * reviews were worked out against them. The API enforces that; this page shows it so the author is never surprised.
 */
@Component({
  selector: 'app-question-edit',
  imports: [ReactiveFormsModule, RouterLink, QuestionFields],
  templateUrl: './question-edit.html',
})
export class QuestionEdit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(QuestionApiService);
  private readonly questionId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  protected readonly question = signal<QuestionDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** Topics already in use, offered as completions. */
  protected readonly topics = signal<string[]>([]);

  protected readonly form = createQuestionForm(this.formBuilder);
  /** Whether candidates have answered the question, which locks its answer key and option list. */
  protected readonly wordingOnly = computed(() => this.question()?.usage.answered ?? false);

  constructor() {
    if (this.questionId === null) {
      this.loading.set(false);
      this.errorMessage.set('No question was given.');
      return;
    }

    this.load(this.questionId);
    // Only a convenience; without suggestions the author can still type any topic.
    this.api.topics().subscribe({ next: (topics) => this.topics.set(topics), error: () => this.topics.set([]) });
  }

  protected submit(): void {
    const question = this.question();
    if (question === null || this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);

    this.api.update(question.id, { text: this.form.getRawValue().text, options: toEditedOptions(this.form), ...toLabels(this.form), allowsMultiple: toAllowsMultiple(this.form) }).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.saved.set(true);
        this.show(updated);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
        // A candidate answered while this page was open. Lock the controls now so the author sees the rule they ran into,
        // and keeps what they typed instead of losing it to a reload.
        if (extractProblemCode(error) === 'question_locked') {
          this.question.update((current) => (current ? { ...current, usage: { ...current.usage, answered: true } } : current));
        }
      },
    });
  }

  /** Throws away unsaved changes by reading the question again. */
  protected discard(): void {
    if (this.questionId !== null) {
      this.saved.set(false);
      this.errorMessage.set(null);
      this.load(this.questionId);
    }
  }

  private load(id: string): void {
    this.loading.set(true);
    this.api.get(id).subscribe({
      next: (question) => {
        this.loading.set(false);
        this.show(question);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error, 'The question could not be loaded.'));
      },
    });
  }

  private show(question: QuestionDto): void {
    this.question.set(question);
    fillQuestionForm(this.form, this.formBuilder, question);
    this.form.markAsPristine();
  }
}
