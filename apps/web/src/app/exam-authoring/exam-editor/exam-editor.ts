import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { QuestionApiService } from '../../question-bank/question-api.service';
import { QuestionDto } from '../../question-bank/question.models';
import { PlainTextPipe } from '../../shared/rich-text/plain-text.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ExamDto } from '../exam.models';

/**
 * Admin page for one exam: its sections and questions, the schedule, and publishing (FR-11, FR-13).
 * A published exam is shown read-only, because the API refuses edits to it.
 */
@Component({
  selector: 'app-exam-editor',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, PlainTextPipe],
  templateUrl: './exam-editor.html',
})
export class ExamEditor {
  private readonly formBuilder = inject(FormBuilder);
  private readonly examApi = inject(ExamApiService);
  private readonly questionApi = inject(QuestionApiService);
  private readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly exam = signal<ExamDto | null>(null);
  protected readonly bank = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly isDraft = computed(() => this.exam()?.status === 'Draft');
  protected readonly questionCount = computed(
    () => this.exam()?.sections?.reduce((total, section) => total + section.questions.length, 0) ?? 0,
  );
  protected readonly canPublish = computed(
    () => this.isDraft() && this.exam()?.isScheduled === true && this.questionCount() > 0,
  );

  /** Bank questions not yet in this exam: an exam may include a question only once. */
  protected readonly availableQuestions = computed(() => {
    const used = new Set(
      this.exam()?.sections?.flatMap((section) => section.questions.map((question) => question.questionId)) ?? [],
    );
    return this.bank().filter((question) => !used.has(question.id));
  });

  protected readonly sectionForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
  });

  constructor() {
    this.reload();
    this.questionApi.list().subscribe({
      next: (questions) => this.bank.set(questions),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  protected addSection(): void {
    if (this.sectionForm.invalid || this.busy()) {
      return;
    }

    this.run(this.examApi.addSection(this.examId, this.sectionForm.getRawValue().name.trim()), () =>
      this.sectionForm.reset({ name: '' }),
    );
  }

  protected addQuestion(sectionId: string, questionId: string): void {
    if (this.busy()) {
      return;
    }

    // Said out loud rather than ignored: the list behind the picker is redrawn after every change, which can
    // clear a choice made a moment earlier, and a silent no-op would look like a button that does nothing.
    if (!questionId) {
      this.errorMessage.set('Choose a question to add.');
      return;
    }

    this.run(this.examApi.addQuestion(this.examId, sectionId, questionId));
  }

  protected publish(): void {
    if (!this.canPublish() || this.busy()) {
      return;
    }

    this.run(this.examApi.publish(this.examId));
  }

  // Runs one change, then reloads the exam so the page always shows what the server stored.
  private run(call: Observable<unknown>, afterSuccess?: () => void): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    call.subscribe({
      next: () => {
        afterSuccess?.();
        this.busy.set(false);
        this.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private reload(): void {
    this.examApi.getExamById(this.examId).subscribe({
      next: (exam) => {
        this.exam.set(exam);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
