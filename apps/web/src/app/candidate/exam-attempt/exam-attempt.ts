import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptDto, AttemptQuestionDto } from '../candidate.models';

/** How often the countdown is redrawn. */
const TICK_MS = 1000;

/**
 * Where a candidate sits an exam and reads their result (FR-16 to FR-21). While the attempt is open it shows the
 * questions and a countdown to the deadline the server set; each choice is saved as it is made, so a closed tab
 * or a dropped connection loses nothing. When time runs out the page asks the server for the attempt again, which
 * closes it with what was saved. The server is the only authority on time and on the score.
 */
@Component({
  selector: 'app-exam-attempt',
  imports: [RouterLink],
  templateUrl: './exam-attempt.html',
})
export class ExamAttempt {
  private readonly api = inject(CandidateApiService);
  private readonly attemptId = inject(ActivatedRoute).snapshot.paramMap.get('attemptId');

  protected readonly attempt = signal<AttemptDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly confirmingSubmit = signal(false);
  protected readonly submitting = signal(false);
  protected readonly remainingSeconds = signal(0);

  protected readonly questions = computed(() => this.attempt()?.sections.flatMap((s) => s.questions) ?? []);
  protected readonly answeredCount = computed(() => this.questions().filter((q) => q.selectedOptionId !== null).length);
  protected readonly isOpen = computed(() => this.attempt()?.status === 'InProgress');

  /** Which question is on screen, as an index into the flat list of every section's questions. */
  private readonly requestedIndex = signal(0);

  /** The question on screen. Clamped, so a reload that changes the question count can never leave it pointing nowhere. */
  protected readonly position = computed(() => Math.min(this.requestedIndex(), Math.max(0, this.questions().length - 1)));
  protected readonly currentQuestion = computed<AttemptQuestionDto | null>(() => this.questions()[this.position()] ?? null);
  protected readonly isFirst = computed(() => this.position() === 0);
  protected readonly isLast = computed(() => this.position() >= this.questions().length - 1);

  /** The name of the section the on-screen question belongs to. */
  protected readonly currentSectionName = computed(() => {
    let remaining = this.position();
    for (const section of this.attempt()?.sections ?? []) {
      if (remaining < section.questions.length) {
        return section.name;
      }
      remaining -= section.questions.length;
    }
    return '';
  });

  /** The question palette: every question numbered across sections, with whether it has an answer. */
  protected readonly palette = computed(() => {
    let offset = 0;
    return (this.attempt()?.sections ?? []).map((section) => {
      const items = section.questions.map((question, i) => ({
        index: offset + i,
        number: offset + i + 1,
        answered: question.selectedOptionId !== null,
      }));
      offset += section.questions.length;
      return { id: section.id, name: section.name, items };
    });
  });

  /** Server clock minus the candidate's clock at the moment the attempt was fetched, so the countdown tracks the server. */
  private clockOffsetMs = 0;
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopTimer());

    if (this.attemptId === null) {
      this.loading.set(false);
      this.errorMessage.set('No attempt was given.');
      return;
    }

    this.api.getAttempt(this.attemptId).subscribe({
      next: (attempt) => this.show(attempt),
      error: (error: unknown) => this.fail(error),
    });
  }

  /** The time left as m:ss (or h:mm:ss), for the countdown. */
  protected formatRemaining(totalSeconds: number): string {
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    const pad = (value: number) => value.toString().padStart(2, '0');
    return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`;
  }

  /** Shows the question at <paramref name="index"/> (0-based); an index outside the exam is ignored. */
  protected goTo(index: number): void {
    if (index >= 0 && index < this.questions().length) {
      this.requestedIndex.set(index);
    }
  }

  protected next(): void {
    this.goTo(this.position() + 1);
  }

  protected previous(): void {
    this.goTo(this.position() - 1);
  }

  /** A, B, C… for the n-th choice (0-based), the way printed papers label them. */
  protected optionLetter(index: number): string {
    return String.fromCharCode(65 + index);
  }

  /** Records a choice. It shows at once and is saved in the background; if the save fails the previous choice comes back. */
  protected choose(question: AttemptQuestionDto, optionId: string): void {
    const attempt = this.attempt();
    if (attempt === null || !this.isOpen() || question.selectedOptionId === optionId) {
      return;
    }

    const previous = question.selectedOptionId;
    this.setSelection(question.id, optionId);
    this.errorMessage.set(null);

    this.api.saveAnswer(attempt.id, question.id, optionId).subscribe({
      error: (error: unknown) => {
        this.setSelection(question.id, previous);
        // A 409 means the attempt ended under the candidate (time ran out): the saved state is the truth now.
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.reload();
        } else {
          this.errorMessage.set(extractErrorMessage(error, 'Your answer could not be saved. Please try again.'));
        }
      },
    });
  }

  protected askToSubmit(): void {
    this.confirmingSubmit.set(true);
  }

  protected cancelSubmit(): void {
    this.confirmingSubmit.set(false);
  }

  protected submit(): void {
    const attempt = this.attempt();
    if (attempt === null || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.api.submitAttempt(attempt.id).subscribe({
      next: (submitted) => {
        this.submitting.set(false);
        this.confirmingSubmit.set(false);
        this.show(submitted);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private show(attempt: AttemptDto): void {
    this.loading.set(false);
    this.attempt.set(attempt);
    this.stopTimer();

    if (attempt.status === 'InProgress') {
      this.clockOffsetMs = Date.parse(attempt.serverTimeUtc) - Date.now();
      this.tick();
      this.timer = setInterval(() => this.tick(), TICK_MS);
    }
  }

  private tick(): void {
    const attempt = this.attempt();
    if (attempt === null) {
      return;
    }

    const remainingMs = Date.parse(attempt.deadlineUtc) - (Date.now() + this.clockOffsetMs);
    this.remainingSeconds.set(Math.max(0, Math.ceil(remainingMs / 1000)));

    if (remainingMs <= 0) {
      // Out of time: the server closes the attempt when it is next read, and tells us the result.
      this.stopTimer();
      this.reload();
    }
  }

  private reload(): void {
    if (this.attemptId === null) {
      return;
    }

    this.api.getAttempt(this.attemptId).subscribe({
      next: (attempt) => this.show(attempt),
      error: (error: unknown) => this.fail(error),
    });
  }

  private setSelection(questionId: string, optionId: string | null): void {
    this.attempt.update((attempt) =>
      attempt === null
        ? null
        : {
            ...attempt,
            sections: attempt.sections.map((section) => ({
              ...section,
              questions: section.questions.map((q) => (q.id === questionId ? { ...q, selectedOptionId: optionId } : q)),
            })),
          },
    );
  }

  private fail(error: unknown): void {
    this.loading.set(false);
    this.errorMessage.set(extractErrorMessage(error));
  }

  private stopTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
