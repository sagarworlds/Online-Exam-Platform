import { Component, inject, input } from '@angular/core';
import { ControlContainer, FormBuilder, FormGroupDirective, ReactiveFormsModule } from '@angular/forms';
import { RichTextEditor } from '../../shared/rich-text/rich-text-editor';
import { newOption, QuestionForm } from '../question-form';
import { QUESTION_LIMITS } from '../question.models';

/**
 * The question text and its options with the choice of the correct one: the part of the question form that creating
 * and editing share. It lives inside the page's `<form [formGroup]>` and works on that form directly, so the page keeps
 * owning the form, the buttons and what happens on submit.
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

  protected readonly limits = QUESTION_LIMITS;

  protected get form(): QuestionForm {
    return this.container.control as unknown as QuestionForm;
  }

  protected get options() {
    return this.form.controls.options;
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
