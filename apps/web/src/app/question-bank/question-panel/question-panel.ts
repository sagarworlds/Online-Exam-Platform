import { afterNextRender, Component, computed, ElementRef, inject, Injector, input, model, OnInit, output, Signal, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BookDto } from '../../book-management/book.models';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ModalDialog } from '../../shared/modal-dialog/modal-dialog';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { BookChapterPicker, isCompletePlacement, NO_PLACEMENT, Placement } from '../book-chapter-picker/book-chapter-picker';
import { QuestionApiService } from '../question-api.service';
import { QuestionFields } from '../question-fields/question-fields';
import {
  answersToCompare,
  createQuestionForm,
  fillQuestionForm,
  newOption,
  setAcceptedAnswers,
  toAcceptedAnswers,
  toAllowsMultiple,
  toEditedOptions,
  toExplanation,
  toIsTextAnswer,
  toLabels,
  toMatchRule,
  toNewOptions,
} from '../question-form';
import { CreateQuestionRequest, DuplicateQuestion, QuestionDto, QuestionLanguage } from '../question.models';
import { STATUS_KEYS } from '../question-review/question-review';

/** What the panel says when it has added a question: whether the author asked for the next one, so the page knows whether the panel stays. */
export interface QuestionCreated {
  another: boolean;
}

/**
 * The form for one question, in a sheet down the side of the question bank: a new question, or the edit of one that exists (FR-5, FR-7).
 * The list stays behind it, so adding and editing never leaves the page, and an author can add one question after another without the
 * panel closing between them ("Save and add another").
 *
 * Editing: before any candidate has answered, everything can change. Once one has, only the wording can: the correct option and the list
 * of options are locked, because stored scores and reviews were worked out against them. The API enforces that; this shows it.
 *
 * Whatever Save did is shown in the panel's footer, next to the buttons, where the author is looking: the form is long, so a message
 * above it would be out of sight. A refusal and the warning that the bank already has the question take focus, which also scrolls them in.
 * The panel asks before it closes with work in it, so a stray Escape does not throw away a question that was being typed.
 */
@Component({
  selector: 'app-question-panel',
  imports: [ReactiveFormsModule, BookChapterPicker, ModalDialog, QuestionFields, TranslatePipe],
  templateUrl: './question-panel.html',
  styleUrl: './question-panel.css',
})
export class QuestionPanel implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(QuestionApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The question to edit, or null for a new one. */
  readonly questionId = input<string | null>(null);
  /** Every book, for filing a new question. */
  readonly books = input.required<readonly BookDto[]>();
  /** Topics already in use, offered as completions. */
  readonly topics = input<readonly string[]>([]);
  /**
   * Where a new question is filed. It is two-way bound so the page keeps it: an author enters many questions into one chapter in a row,
   * and the panel is a new one each time it opens.
   */
  readonly placement = model<Placement>(NO_PLACEMENT);
  /** The language a new question is written in, kept by the page for the same reason as {@link placement}. */
  readonly language = model<QuestionLanguage>('en');

  /** A new question was added. */
  readonly created = output<QuestionCreated>();
  /** An existing question was edited; this is it as it is now. */
  readonly saved = output<QuestionDto>();
  /** The panel has closed, however it was closed. */
  readonly closed = output<void>();

  private readonly dialog = viewChild(ModalDialog);
  /** The warning that the bank already has the question, once it is on the page. */
  private readonly repeatCard = viewChild<ElementRef<HTMLElement>>('repeatCard');
  /** The reason the last Save was refused, once it is on the page. */
  private readonly saveFailure = viewChild<ElementRef<HTMLElement>>('saveFailure');
  /** The question whether to throw away the changes, once it is on the page. */
  private readonly discardAsk = viewChild<ElementRef<HTMLElement>>('discardAsk');

  protected readonly form = createQuestionForm(this.formBuilder);
  /** The form as it was when the author began, to tell whether there is anything to lose. */
  private baseline = '';

  /** The question being edited, once it has been read. */
  protected readonly question = signal<QuestionDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  /** Whether the last Save of a new question was accepted and the panel stayed open for the next one. */
  protected readonly savedNotice = signal(false);
  /** Why the last Save was refused, other than as a repeat. */
  protected readonly saveError = signal<string | null>(null);
  /** A question the bank refused as a repeat, with what it repeats, until the author adds it anyway or gives up (FR-9). */
  protected readonly repeat = signal<{ request: CreateQuestionRequest; matches: DuplicateQuestion[]; another: boolean } | null>(null);
  /** Whether the panel is asking if the changes may be thrown away. */
  protected readonly confirmingDiscard = signal(false);

  protected readonly isEdit = computed(() => this.questionId() !== null);
  protected readonly heading = computed(() => this.i18n.t(this.isEdit() ? 'qb.panel.edit' : 'qb.new'));
  /** Whether candidates have answered the question, which locks its answer key and option list. */
  protected readonly wordingOnly = computed(() => this.question()?.usage.answered ?? false);
  protected readonly placementComplete = computed(() => isCompletePlacement(this.placement()));

  /** Which exams hold the question, for the line under where it is filed: the first few by name, and that there are more if there are. */
  protected inExamText(current: QuestionDto): string {
    const { examCount, examNames } = current.usage;
    const names = examNames.join(', ');
    return this.i18n.plural('qb.panel.inExams', examCount, { names: examCount > examNames.length ? `${names} ${this.i18n.t('qb.panel.andOthers')}` : names });
  }

  /** A status the API names, in the words of the language in use; one this page has no words for is shown as the API wrote it. */
  protected statusText(status: string): string {
    return status in STATUS_KEYS ? this.i18n.t(STATUS_KEYS[status as keyof typeof STATUS_KEYS]) : status;
  }

  /** Whether the form differs from how it began. It is what the panel would lose by closing. */
  protected get changed(): boolean {
    return this.snapshot() !== this.baseline;
  }

  /** Whether Save may be pressed. */
  protected get canSave(): boolean {
    if (this.saving() || this.form.invalid) {
      return false;
    }
    return this.isEdit() ? this.question() !== null : this.placementComplete();
  }

  ngOnInit(): void {
    const id = this.questionId();
    if (id === null) {
      this.form.controls.language.setValue(this.language());
      this.markClean();
      this.focusText();
    } else {
      this.load(id);
    }
  }

  /** Saves, and closes the panel. */
  protected submit(): void {
    this.save(false);
  }

  /** Saves a new question and starts the next one, leaving the panel open. */
  protected saveAndAddAnother(): void {
    this.save(true);
  }

  /** Asks to close: at once when there is nothing to lose, otherwise after asking whether the changes may go. */
  protected requestClose(): void {
    if (this.changed) {
      this.askToDiscard();
    } else {
      this.dialog()?.close();
    }
  }

  protected askToDiscard(): void {
    this.confirmingDiscard.set(true);
    this.focusOnceRendered(this.discardAsk);
  }

  protected keepEditing(): void {
    this.confirmingDiscard.set(false);
    this.focusText();
  }

  protected discard(): void {
    this.dialog()?.close();
  }

  /** Adds the question the bank called a repeat, because the author looked at what it repeats and still wants it. */
  protected addAnyway(): void {
    const pending = this.repeat();
    if (pending && !this.saving()) {
      this.send({ ...pending.request, allowDuplicate: true }, pending.another);
    }
  }

  protected dismissRepeat(): void {
    this.repeat.set(null);
  }

  private save(another: boolean): void {
    if (!this.canSave) {
      return;
    }

    const current = this.question();
    if (current !== null) {
      this.saveEdit(current);
    } else {
      this.send(this.newQuestionRequest(), another);
    }
  }

  private newQuestionRequest(): CreateQuestionRequest {
    return {
      text: this.form.getRawValue().text,
      chapterId: this.placement().chapterId || null,
      options: toNewOptions(this.form),
      ...toLabels(this.form),
      allowsMultiple: toAllowsMultiple(this.form),
      language: this.form.getRawValue().language,
      isTextAnswer: toIsTextAnswer(this.form),
      acceptedAnswers: toAcceptedAnswers(this.form),
      matchRule: toMatchRule(this.form),
      explanation: toExplanation(this.form),
    };
  }

  private send(request: CreateQuestionRequest, another: boolean): void {
    this.saving.set(true);
    this.savedNotice.set(false);
    this.saveError.set(null);
    this.repeat.set(null);

    this.api.create(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.language.set(this.form.getRawValue().language);
        this.created.emit({ another });
        if (another) {
          this.startNext();
        } else {
          this.dialog()?.close();
        }
      },
      error: (error: unknown) => {
        this.saving.set(false);
        if (extractProblemCode(error) === 'duplicate_question') {
          this.showRepeat(request, error, another);
          return;
        }
        this.showSaveError(error);
      },
    });
  }

  private saveEdit(current: QuestionDto): void {
    this.saving.set(true);
    this.saveError.set(null);

    this.api
      .update(current.id, {
        text: this.form.getRawValue().text,
        options: toEditedOptions(this.form),
        ...toLabels(this.form),
        allowsMultiple: toAllowsMultiple(this.form),
        isTextAnswer: toIsTextAnswer(this.form),
        acceptedAnswers: toAcceptedAnswers(this.form),
        matchRule: toMatchRule(this.form),
        explanation: toExplanation(this.form),
      })
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.saved.emit(updated);
          this.dialog()?.close();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          // A candidate answered while the panel was open. Lock the controls now so the author sees the rule they ran into,
          // and keeps what they typed instead of losing it to a reload.
          if (extractProblemCode(error) === 'question_locked') {
            this.question.update((existing) => (existing ? { ...existing, usage: { ...existing.usage, answered: true } } : existing));
          }
          this.showSaveError(error);
        },
      });
  }

  private load(id: string): void {
    this.loading.set(true);
    this.api.get(id).subscribe({
      next: (question) => {
        this.loading.set(false);
        this.question.set(question);
        fillQuestionForm(this.form, this.formBuilder, question);
        this.markClean();
        this.focusText();
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(extractErrorMessage(error, this.i18n.t('qb.panel.loadError')));
      },
    });
  }

  /**
   * Looks up what the refused question repeats, so the author can see it before deciding. The warning is in the footer, beside the Save
   * button, and takes focus, which also scrolls it into view.
   */
  private showRepeat(request: CreateQuestionRequest, refusal: unknown, another: boolean): void {
    this.api.duplicates(request.text, answersToCompare(request)).subscribe({
      next: (matches) => {
        this.repeat.set({ request, matches, another });
        this.focusOnceRendered(this.repeatCard);
      },
      // The refusal itself is the message when the list of matches cannot be had.
      error: () => this.showSaveError(refusal),
    });
  }

  /** Shows why Save was refused, in the footer for the same reason as the repeat warning, and takes the author there. */
  private showSaveError(error: unknown): void {
    this.saveError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
    this.focusOnceRendered(this.saveFailure);
  }

  /** Empties the form for the next question. The language stays as it was, and so does the chapter: an author enters many in a row. */
  private startNext(): void {
    this.form.reset({
      text: '',
      questionType: 'choice',
      allowsMultiple: false,
      correctIndex: -1,
      difficulty: '',
      topics: '',
      language: this.form.getRawValue().language,
    });
    this.form.controls.options.clear();
    this.form.controls.options.push(newOption(this.formBuilder));
    this.form.controls.options.push(newOption(this.formBuilder));
    setAcceptedAnswers(this.form, this.formBuilder);
    this.markClean();
    this.savedNotice.set(true);
    this.focusText();
  }

  private snapshot(): string {
    return JSON.stringify(this.form.getRawValue());
  }

  private markClean(): void {
    this.baseline = this.snapshot();
    this.form.markAsPristine();
  }

  /**
   * Puts the cursor in the question's text. The editor is made after the first render, so this waits for the render after that: in the
   * phase after the one the editor is made in.
   */
  private focusText(): void {
    afterNextRender({ read: () => this.host.nativeElement.querySelector<HTMLElement>('#question-text')?.focus() }, { injector: this.injector });
  }

  /** Moves focus to a message the panel has just shown and brings all of it into view. The message exists only after the next render. */
  private focusOnceRendered(message: Signal<ElementRef<HTMLElement> | undefined>): void {
    afterNextRender(
      () => {
        const element = message()?.nativeElement;
        if (element) {
          // Focus does not scroll here: when a message is only partly on screen, browsers differ in how far focus moves it (WebKit leaves it
          // flush against the screen's edge, a pixel short), so the page is scrolled below, the same way in every engine.
          element.focus({ preventScroll: true });
          this.revealWhole(element);
        }
      },
      { injector: this.injector },
    );
  }

  /** Scrolls an element that is not wholly in view to the middle of what can be seen, or to its top when it is taller than that. */
  private revealWhole(element: HTMLElement): void {
    const { top, bottom } = element.getBoundingClientRect();
    const visible = this.visibleArea(element);
    if (top < visible.top || bottom > visible.bottom) {
      element.scrollIntoView({ block: bottom - top > visible.bottom - visible.top ? 'start' : 'center' });
    }
  }

  /**
   * The part of the screen an element can be seen in: the screen, narrowed by each box around it that scrolls, since what is past the edge
   * of the panel's footer or body is on the screen but cut off. Both are boxes that scroll, and the footer is small on a short screen.
   */
  private visibleArea(element: HTMLElement): { top: number; bottom: number } {
    const document = element.ownerDocument;
    let top = 0;
    let bottom = document.documentElement.clientHeight;
    for (let box = element.parentElement; box !== null; box = box.parentElement) {
      const overflow = document.defaultView?.getComputedStyle(box).overflowY;
      if (overflow === 'auto' || overflow === 'scroll') {
        const rect = box.getBoundingClientRect();
        top = Math.max(top, rect.top);
        bottom = Math.min(bottom, rect.bottom);
      }
    }
    return { top, bottom };
  }
}
