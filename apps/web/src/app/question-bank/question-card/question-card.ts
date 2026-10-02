import { Component, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuestionDto } from '../question.models';

/**
 * One question in the question bank list (FR-5): where it is filed, its text and options with the correct one marked,
 * where it is in use, and what the author may do with it. It only shows and asks; the page holds the list and makes the
 * requests, so a card never has to know how the question got there or what happens next.
 */
@Component({
  selector: 'app-question-card',
  imports: [RouterLink],
  templateUrl: './question-card.html',
})
export class QuestionCard {
  readonly question = input.required<QuestionDto>();
  /** True while a request about this question is running, so its buttons cannot be pressed twice. */
  readonly busy = input(false);
  /** Why the last request about this question failed, if it did. */
  readonly error = input<string | null>(null);

  /** The author confirmed deleting this question; carries its id. */
  readonly deleteConfirmed = output<string>();

  protected readonly confirmingDelete = signal(false);

  /** An exam holds the question, so the API would refuse to delete it; say so up front instead of after a click. */
  protected readonly inExam = computed(() => this.question().usage.examCount > 0);
  protected readonly examLabel = computed(() => {
    const count = this.question().usage.examCount;
    return `In ${count} ${count === 1 ? 'exam' : 'exams'}`;
  });

  protected confirmDelete(): void {
    this.confirmingDelete.set(false);
    this.deleteConfirmed.emit(this.question().id);
  }
}
