import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptDto, AttemptQuestionDto } from '../candidate.models';

/** How often the countdown is redrawn. */
const TICK_MS = 1000;

/** The text sizes a candidate can pick, as a share of normal. Capped at 150% so the question and palette still fit side by side. */
const ZOOM_LEVELS = [1, 1.15, 1.3, 1.5] as const;

/** Where the chosen size is remembered. Browser-only: it is a comfort setting, not part of the exam. */
const ZOOM_STORAGE_KEY = 'exam.textZoom';

/** Reads the remembered text size, ignoring anything that is not one of the offered levels or a blocked store. */
function loadZoomLevel(): number {
  try {
    const stored = Number(localStorage.getItem(ZOOM_STORAGE_KEY));
    return ZOOM_LEVELS.find((level) => level === stored) ?? ZOOM_LEVELS[0];
  } catch {
    return ZOOM_LEVELS[0];
  }
}

/** The palette's wording for a question, for a screen reader; sighted candidates get the same from the colours and the legend. */
function paletteStatus(answered: boolean, marked: boolean, seen: boolean): string {
  if (answered && marked) return 'answered and marked for review';
  if (marked) return 'marked for review';
  if (answered) return 'answered';
  return seen ? 'not answered' : 'not visited';
}

/**
 * Where a candidate sits an exam and reads their result (FR-16 to FR-21). While the attempt is open it shows the
 * questions and a countdown to the deadline the server set; each choice is saved as it is made, so a closed tab
 * or a dropped connection loses nothing. When time runs out the page asks the server for the attempt again, which
 * closes it with what was saved. The server is the only authority on time and on the score.
 */
@Component({
  selector: 'app-exam-attempt',
  imports: [RouterLink, DatePipe, DecimalPipe],
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

  /** The question text size, as a multiple of normal. */
  protected readonly zoom = signal<number>(loadZoomLevel());
  protected readonly canZoomOut = computed(() => this.zoom() > ZOOM_LEVELS[0]);
  protected readonly canZoomIn = computed(() => this.zoom() < ZOOM_LEVELS[ZOOM_LEVELS.length - 1]);

  protected readonly questions = computed(() => this.attempt()?.sections.flatMap((s) => s.questions) ?? []);
  protected readonly answeredCount = computed(() => this.questions().filter((q) => q.selectedOptionId !== null).length);
  protected readonly markedCount = computed(() => this.questions().filter((q) => q.markedForReview).length);
  protected readonly isOpen = computed(() => this.attempt()?.status === 'InProgress');

  /**
   * The questions the candidate has had on screen, so the palette can tell "not visited" from "not answered". It is
   * kept in the page only: a visit is not scored or saved, so after a reload only what the server holds (answers and
   * review marks) is remembered and the rest starts again as not visited.
   */
  private readonly visited = signal<ReadonlySet<string>>(new Set());

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

  /**
   * The question palette: every question numbered across sections, with where it stands. A question is answered, marked
   * for review, both, or neither; a question with neither is "not answered" once seen and "not visited" until then.
   * Each item also says so in words for a screen reader, so the colours are never the only signal.
   */
  protected readonly palette = computed(() => {
    let offset = 0;
    const visited = this.visited();
    return (this.attempt()?.sections ?? []).map((section) => {
      const items = section.questions.map((question, i) => {
        const answered = question.selectedOptionId !== null;
        const marked = question.markedForReview;
        const seen = !answered && !marked && visited.has(question.id);
        return {
          index: offset + i,
          number: offset + i + 1,
          answered,
          marked,
          seen,
          status: paletteStatus(answered, marked, seen),
        };
      });
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
      this.markCurrentVisited();
    }
  }

  protected next(): void {
    this.goTo(this.position() + 1);
  }

  protected previous(): void {
    this.goTo(this.position() - 1);
  }

  /**
   * Saves & Next: makes sure the choice on screen is stored, then moves on. Every choice is already saved the moment it
   * is made, so there is nothing left to send here; the button exists so a candidate used to "save, then next" has the
   * action they expect, and on the last question it simply stays put.
   */
  protected saveAndNext(): void {
    if (!this.isLast()) {
      this.next();
    }
  }

  /** Makes the question text one step larger. */
  protected zoomIn(): void {
    this.stepZoom(1);
  }

  /** Makes the question text one step smaller. */
  protected zoomOut(): void {
    this.stepZoom(-1);
  }

  private stepZoom(direction: 1 | -1): void {
    const current = ZOOM_LEVELS.findIndex((level) => level === this.zoom());
    const next = ZOOM_LEVELS[Math.min(ZOOM_LEVELS.length - 1, Math.max(0, current + direction))];
    this.zoom.set(next);
    try {
      localStorage.setItem(ZOOM_STORAGE_KEY, String(next));
    } catch {
      // A blocked store only means the size is forgotten on the next visit; the change on screen still applies.
    }
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
        this.explainFailure(error, 'Your answer could not be saved. Please try again.');
      },
    });
  }

  /** Takes back the question's answer. It shows at once and is saved in the background; if the save fails the answer comes back. */
  protected clearResponse(question: AttemptQuestionDto): void {
    const attempt = this.attempt();
    if (attempt === null || !this.isOpen() || question.selectedOptionId === null) {
      return;
    }

    const previous = question.selectedOptionId;
    this.setSelection(question.id, null);
    this.errorMessage.set(null);

    this.api.clearAnswer(attempt.id, question.id).subscribe({
      error: (error: unknown) => {
        this.setSelection(question.id, previous);
        this.explainFailure(error, 'Your answer could not be cleared. Please try again.');
      },
    });
  }

  /** Marks the question for review, or takes the mark off. It shows at once; if the save fails the mark goes back as it was. */
  protected toggleMark(question: AttemptQuestionDto): void {
    const attempt = this.attempt();
    if (attempt === null || !this.isOpen()) {
      return;
    }

    const previous = question.markedForReview;
    this.setMarked(question.id, !previous);
    this.errorMessage.set(null);

    const request = previous
      ? this.api.unmarkForReview(attempt.id, question.id)
      : this.api.markForReview(attempt.id, question.id);
    request.subscribe({
      error: (error: unknown) => {
        this.setMarked(question.id, previous);
        this.explainFailure(error, 'The review mark could not be saved. Please try again.');
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
      this.markCurrentVisited();
      this.clockOffsetMs = Date.parse(attempt.serverTimeUtc) - Date.now();
      this.tick();
      this.timer = setInterval(() => this.tick(), TICK_MS);
    }
  }

  /** Notes that the question on screen has been seen. */
  private markCurrentVisited(): void {
    const id = this.currentQuestion()?.id;
    if (id !== undefined) {
      this.visited.update((seen) => (seen.has(id) ? seen : new Set(seen).add(id)));
    }
  }

  /**
   * Says why a change could not be saved. A 409 means the attempt ended under the candidate (time ran out), so the
   * saved state is the truth now and the page reloads it; anything else is shown and the candidate can try again.
   */
  private explainFailure(error: unknown, fallback: string): void {
    if (error instanceof HttpErrorResponse && error.status === 409) {
      this.reload();
    } else {
      this.errorMessage.set(extractErrorMessage(error, fallback));
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
    this.changeQuestion(questionId, { selectedOptionId: optionId });
  }

  private setMarked(questionId: string, marked: boolean): void {
    this.changeQuestion(questionId, { markedForReview: marked });
  }

  /** Applies a change to one question of the open attempt, leaving every other question as it was. */
  private changeQuestion(questionId: string, change: Partial<AttemptQuestionDto>): void {
    this.attempt.update((attempt) =>
      attempt === null
        ? null
        : {
            ...attempt,
            sections: attempt.sections.map((section) => ({
              ...section,
              questions: section.questions.map((q) => (q.id === questionId ? { ...q, ...change } : q)),
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
