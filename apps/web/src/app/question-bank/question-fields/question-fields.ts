import { Component, inject, input } from '@angular/core';
import { ControlContainer, FormBuilder, FormGroupDirective, ReactiveFormsModule } from '@angular/forms';
import { RichTextEditor } from '../../shared/rich-text/rich-text-editor';
import { newAcceptedAnswer, newOption, QuestionForm } from '../question-form';
import { QUESTION_DIFFICULTIES, QUESTION_LIMITS, QUESTION_TYPES } from '../question.models';

/**
 * The question text, its type, and either its options with the choice of the correct one or its accepted answers: the part of the
 * question form that creating and editing share. It lives inside the page's `<form [formGroup]>` and works on that form directly, so the
 * page keeps owning the form, the buttons and what happens on submit.
 */
@Component({
  selector: 'app-question-fields',
  imports: [ReactiveFormsModule, RichTextEditor],
  // Lets formControlName in this template find the page's form group, which a child component cannot see by default.
  viewProviders: [{ provide: ControlContainer, useExisting: FormGroupDirective }],
  templateUrl: './question-fields.html',
})
export class QuestionFields {
  private readonly container = inject(ControlContainer);
  private readonly formBuilder = inject(FormBuilder);

  /**
   * Candidates have answered the question, so which option is correct and the list of options are locked: stored scores
   * and reviews were worked out against them. The wording of the text and of each option can still be corrected.
   */
  readonly wordingOnly = input(false);

  /** Topics already in use, offered as completions so authors reuse "fractions" instead of spelling it three ways. */
  readonly topicSuggestions = input<readonly string[]>([]);

  protected readonly limits = QUESTION_LIMITS;
  protected readonly difficulties = QUESTION_DIFFICULTIES;
  protected readonly questionTypes = QUESTION_TYPES;

  protected get form(): QuestionForm {
    return this.container.control as unknown as QuestionForm;
  }

  protected get options() {
    return this.form.controls.options;
  }

  /** The accepted answers of a text question; the form keeps them even while a multiple-choice question is chosen. */
  protected get acceptedAnswers() {
    return this.form.controls.acceptedAnswers;
  }

  /** Whether the question is a text question, which asks for accepted answers rather than options. */
  protected get isText(): boolean {
    return this.form.controls.questionType.value === 'text';
  }

  protected addAcceptedAnswer(): void {
    if (this.acceptedAnswers.length < QUESTION_LIMITS.maxAcceptedAnswers) {
      this.acceptedAnswers.push(newAcceptedAnswer(this.formBuilder));
    }
  }

  /** A text question keeps at least one row, so there is always a field to type into. */
  protected removeAcceptedAnswer(index: number): void {
    if (this.acceptedAnswers.length > 1) {
      this.acceptedAnswers.removeAt(index);
    }
  }

  /**
   * Switches between one correct answer and several, keeping the author's work: the single choice becomes a tick, and the first
   * tick becomes the single choice (a question that had several loses the others, which the author sees at once).
   */
  protected setAllowsMultiple(multiple: boolean): void {
    const { correctIndex, options } = this.form.getRawValue();
    if (multiple) {
      this.options.controls.forEach((option, index) => option.controls.correct.setValue(index === correctIndex));
    } else {
      this.form.controls.correctIndex.setValue(options.findIndex((option) => option.correct));
      this.options.controls.forEach((option) => option.controls.correct.setValue(false));
    }

    this.form.controls.allowsMultiple.setValue(multiple);
  }

  protected addOption(): void {
    if (this.options.length < QUESTION_LIMITS.maxOptions) {
      this.options.push(newOption(this.formBuilder));
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
}
