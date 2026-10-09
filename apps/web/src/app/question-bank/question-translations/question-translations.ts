import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormArray, FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { RichTextEditor } from '../../shared/rich-text/rich-text-editor';
import { QuestionApiService } from '../question-api.service';
import { QUESTION_LANGUAGES, QuestionDto, QuestionLanguage, QuestionTranslation } from '../question.models';
import { STATUS_LABELS } from '../question-review/question-review';

/**
 * The versions of one question in other languages (FR-10), under its card: which languages it has, each one a link to
 * that question, and a form to write the next one. A translation takes the source's answer key, so the form asks only for words:
 * the question and one option text for each option of the source, shown beside it in the same order.
 */
@Component({
  selector: 'app-question-translations',
  imports: [ReactiveFormsModule, RouterLink, RichTextEditor],
  templateUrl: './question-translations.html',
})
export class QuestionTranslations implements OnInit {
  private readonly api = inject(QuestionApiService);
  private readonly formBuilder = inject(FormBuilder);

  /** The question whose translations are shown; it is the one a new translation is made from. */
  readonly question = input.required<QuestionDto>();

  /** A translation was saved, so the page can show the new question in its list. */
  readonly added = output<void>();

  protected readonly languages = QUESTION_LANGUAGES;
  protected readonly statusLabels = STATUS_LABELS;
  /** The question and its translations; null until they have loaded. */
  protected readonly linked = signal<QuestionTranslation[] | null>(null);
  protected readonly error = signal<string | null>(null);
  /** The language whose form is open, if one is. */
  protected readonly adding = signal<QuestionLanguage | null>(null);
  protected readonly saving = signal(false);

  /** The languages the group does not have yet, each of which can be added. */
  protected readonly missing = computed(() => {
    const have = new Set((this.linked() ?? []).map((translation) => translation.language));
    return QUESTION_LANGUAGES.filter((language) => !have.has(language.code));
  });
  protected readonly idPrefix = computed(() => `translation-${this.question().id}`);

  protected readonly form = this.formBuilder.nonNullable.group({
    text: ['', Validators.required],
    options: this.formBuilder.array<FormControl<string>>([]),
  });

  // Not the constructor: the question input has no value yet there.
  ngOnInit(): void {
    this.load();
  }

  protected get options(): FormArray<FormControl<string>> {
    return this.form.controls.options;
  }

  protected labelOf(code: string): string {
    return QUESTION_LANGUAGES.find((language) => language.code === code)?.label ?? code;
  }

  /** Opens an empty form with one option box for each option of the question being translated. */
  protected start(language: QuestionLanguage): void {
    this.error.set(null);
    this.form.controls.text.reset('');
    this.options.clear();
    this.question().options.forEach(() => this.options.push(this.formBuilder.nonNullable.control('', Validators.required)));
    this.adding.set(language);
  }

  protected cancel(): void {
    this.adding.set(null);
  }

  protected save(): void {
    const language = this.adding();
    if (!language || this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    const { text, options } = this.form.getRawValue();
    this.api.addTranslation(this.question().id, { language, text, options }).subscribe({
      next: () => {
        this.saving.set(false);
        this.adding.set(null);
        this.load();
        this.added.emit();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(error));
      },
    });
  }

  private load(): void {
    this.api.translations(this.question().id).subscribe({
      next: (translations) => this.linked.set(translations),
      error: (error: unknown) => this.error.set(extractErrorMessage(error)),
    });
  }
}
