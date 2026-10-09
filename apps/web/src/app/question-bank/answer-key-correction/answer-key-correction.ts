import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { extractErrorMessage } from '../../shared/problem-details';
import { QuestionApiService } from '../question-api.service';
import { acceptedAnswersErrors } from '../question-form';
import { AnswerKeyCorrectionResult, MAX_KEY_CORRECTION_REASON, QUESTION_LIMITS, QuestionDto } from '../question.models';

/**
 * Where staff correct the answer key of a question once candidates have answered it (FR-31), the one change such a question allows. It
 * reads the question, starts from its current key, and asks for the corrected one and the reason, which every candidate whose score moves
 * is shown. The key is the options' correct ones, or for a text question its accepted answers. The API rescores the affected attempts and
 * accepts the disputes of the question; this reports what it did and leaves the page around it to show that. A question with one correct
 * option is chosen with radio buttons, one that allows several with checkboxes, as in the question editor.
 */
@Component({
  selector: 'app-answer-key-correction',
  templateUrl: './answer-key-correction.html',
})
export class AnswerKeyCorrection implements OnInit {
  private readonly api = inject(QuestionApiService);

  readonly questionId = input.required<string>();

  /** The API took the correction; carries what it did, which may be nothing when the key was already as asked. */
  readonly corrected = output<AnswerKeyCorrectionResult>();
  /** Staff closed the panel without correcting anything. */
  readonly cancelled = output<void>();

  protected readonly question = signal<QuestionDto | null>(null);
  protected readonly loadError = signal<string | null>(null);
  /** The ids of the options chosen as correct; starts as the question's current key. */
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  /** The accepted answers of a text question, as the author edits them; blank rows are left out when sent. */
  protected readonly answers = signal<readonly string[]>([]);
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly maxReason = MAX_KEY_CORRECTION_REASON;
  protected readonly limits = QUESTION_LIMITS;

  /** Why the choice cannot be sent, or null when it has the shape the API accepts: one option, or for a multiple-answer question some but not all. */
  protected readonly choiceProblem = computed(() => {
    const question = this.question();
    if (question === null) {
      return null;
    }

    const count = this.chosen().size;
    if (count === 0) {
      return question.allowsMultiple ? 'Tick at least one correct option.' : 'Choose the correct option.';
    }

    return question.allowsMultiple && count >= question.options.length ? 'At least one option must stay incorrect.' : null;
  });

  /** Why the accepted answers cannot be sent, or null when they have the shape the API accepts (the same rules as the question form). */
  protected readonly answersProblem = computed(() => {
    const errors = acceptedAnswersErrors(this.answers());
    if (errors === null) {
      return null;
    }
    if (errors['noAcceptedAnswer']) {
      return 'Add at least one accepted answer.';
    }
    return errors['tooManyAcceptedAnswers']
      ? `Use at most ${QUESTION_LIMITS.maxAcceptedAnswers} accepted answers.`
      : 'Two accepted answers match the same typed answer. Keep only one of them.';
  });

  /** The problem with the key as the question takes it: its options for a multiple-choice question, its accepted answers for a text one. */
  protected readonly keyProblem = computed(() => {
    const question = this.question();
    if (question === null) {
      return null;
    }
    return question.isTextAnswer ? this.answersProblem() : this.choiceProblem();
  });

  protected readonly canSave = computed(
    () => !this.busy() && this.question() !== null && this.keyProblem() === null && this.reason().trim() !== '',
  );

  // Not the constructor: the questionId input has no value yet there.
  ngOnInit(): void {
    this.api.get(this.questionId()).subscribe({
      next: (question) => {
        this.question.set(question);
        this.chosen.set(new Set(question.options.filter((option) => option.isCorrect).map((option) => option.id)));
        this.answers.set(question.acceptedAnswers ?? []);
      },
      error: (error: unknown) => this.loadError.set(extractErrorMessage(error, 'The question could not be loaded. Please try again.')),
    });
  }

  protected optionLetter(index: number): string {
    return String.fromCharCode(65 + index);
  }

  protected setAnswer(index: number, text: string): void {
    this.answers.update((current) => current.map((answer, i) => (i === index ? text : answer)));
  }

  protected addAnswer(): void {
    if (this.answers().length < QUESTION_LIMITS.maxAcceptedAnswers) {
      this.answers.update((current) => [...current, '']);
    }
  }

  /** A text question keeps at least one row, so there is always a field to type into. */
  protected removeAnswer(index: number): void {
    if (this.answers().length > 1) {
      this.answers.update((current) => current.filter((_, i) => i !== index));
    }
  }

  /** A radio button replaces the choice; a checkbox adds or takes away one option. */
  protected choose(optionId: string, checked: boolean, multiple: boolean): void {
    if (!multiple) {
      this.chosen.set(new Set([optionId]));
      return;
    }

    this.chosen.update((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(optionId);
      } else {
        next.delete(optionId);
      }
      return next;
    });
  }

  protected save(): void {
    const question = this.question();
    if (question === null || !this.canSave()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const reason = this.reason().trim();
    // A text question's key is its accepted answers, sent with no option ids; a multiple-choice one's is its options, in the question's own order.
    const request = question.isTextAnswer
      ? this.api.correctAnswerKey(question.id, [], reason, this.answers().map((answer) => answer.trim()).filter((answer) => answer.length > 0))
      : this.api.correctAnswerKey(question.id, question.options.filter((option) => this.chosen().has(option.id)).map((option) => option.id), reason);
    request.subscribe({
      next: (result) => {
        this.busy.set(false);
        this.corrected.emit(result);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(extractErrorMessage(error));
      },
    });
  }
}
