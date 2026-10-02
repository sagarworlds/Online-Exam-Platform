import { Component, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { extractErrorMessage } from '../shared/problem-details';
import { RichTextEditor } from '../shared/rich-text/rich-text-editor';
import { QuestionApiService } from './question-api.service';
import { CreateQuestionRequest, QUESTION_LIMITS, QuestionDto } from './question.models';

/** Admin page: the newest questions, and a form to add one with its options and correct answer (FR-5). */
@Component({
  selector: 'app-question-bank',
  imports: [ReactiveFormsModule, RichTextEditor],
  templateUrl: './question-bank.html',
})
export class QuestionBank {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(QuestionApiService);

  protected readonly limits = QUESTION_LIMITS;
  protected readonly questions = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    text: ['', Validators.required],
    // Which option is the right answer, as a radio value; -1 until the author picks one.
    correctIndex: [-1, Validators.min(0)],
    options: this.formBuilder.array([this.newOption(), this.newOption()]),
  });

  constructor() {
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

    const { text, correctIndex, options } = this.form.getRawValue();
    const request: CreateQuestionRequest = {
      text,
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

  private refresh(): void {
    this.api.list().subscribe({
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
    this.options.clear();
    this.options.push(this.newOption());
    this.options.push(this.newOption());
  }

  private newOption() {
    return this.formBuilder.nonNullable.group({ text: ['', Validators.required] });
  }
}
