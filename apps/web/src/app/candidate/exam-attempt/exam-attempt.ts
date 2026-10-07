import { DOCUMENT, DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, finalize } from 'rxjs';
import { Component, DestroyRef, HostListener, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptDto, AttemptQuestionDto, AttemptStatusDto, AttemptWarningDto, FocusViolationKind } from '../candidate.models';
import { BLOCKED_MESSAGES, BlockedAction, ContentGuard } from './content-guard';
import { shortcutFor } from './exam-shortcuts';
import { FocusMonitor } from './focus-monitor';
import { loadVisitedQuestions, saveVisitedQuestions } from './visited-questions-store';
import { MathDirective } from '../../shared/rich-text/math.directive';

/** How often the countdown is redrawn. */
const TICK_MS = 1000;

/** How often the page asks the server whether an administrator has paused, warned or ended the attempt. Frequent enough to feel prompt, rare enough to cost little. */
const HEARTBEAT_MS = 10_000;

/** Where the warnings a candidate has dismissed are remembered, per attempt, so a reload does not show them again. Browser-only. */
const dismissedWarningsKey = (attemptId: string) => `exam.dismissedWarnings.${attemptId}`;

/** Reads the dismissed warning ids, treating a blocked store as none. */
function loadDismissedWarnings(attemptId: string | null): Set<string> {
  if (attemptId === null) {
    return new Set();
  }
  try {
    const parsed: unknown = JSON.parse(sessionStorage.getItem(dismissedWarningsKey(attemptId)) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : []);
  } catch {
    return new Set();
  }
}

/** How long the "that is turned off" sentence stays on screen. Long enough to read once, short enough not to nag. */
const NOTICE_MS = 4000;

/** What a candidate is told right after leaving the exam page: how many times so far, and what the next ones will cost. */
function warningFor(violations: number, limit: number): string {
  const left = limit - violations;
  return (
    `You left the exam page (${violations} of ${limit} allowed). ` +
    (left === 1 ? 'If you leave once more, the exam will be submitted.' : `If you leave ${left} more times, the exam will be submitted.`)
  );
}

/** The text sizes a candidate can pick, as a share of normal. Capped at 150% so the question and palette still fit side by side. */
const ZOOM_LEVELS = [1, 1.15, 1.3, 1.5] as const;

/** Where the chosen size is remembered. Browser-only: it is a comfort setting, not part of the exam. */
const ZOOM_STORAGE_KEY = 'exam.textZoom';

/** Where the high contrast choice is remembered. Browser-only, like the text size. */
const CONTRAST_STORAGE_KEY = 'exam.highContrast';

/** Reads whether high contrast was left on, treating a blocked store as off. */
function loadHighContrast(): boolean {
  try {
    return localStorage.getItem(CONTRAST_STORAGE_KEY) === 'on';
  } catch {
    return false;
  }
}

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
/** The options chosen for a question, whichever shape the response came in. */
export function chosenOptionIds(question: AttemptQuestionDto): string[] {
  return question.selectedOptionIds ?? (question.selectedOptionId ? [question.selectedOptionId] : []);
}

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
  imports: [RouterLink, DatePipe, DecimalPipe, MathDirective],
  templateUrl: './exam-attempt.html',
})
export class ExamAttempt {
  private readonly api = inject(CandidateApiService);
  private readonly route = inject(ActivatedRoute).snapshot;
  private readonly router = inject(Router);

  /**
   * Whether a staff member is previewing the exam (FR-15) rather than a candidate sitting it. The page is the same, but nothing is
   * saved (the preview API drops it), the exam's proctoring is not applied to them, and ending it goes back to the exam.
   */
  protected readonly preview = this.route.data?.['preview'] === true;

  /** The id the preview's back link points to: the exam's, which is the id in the address. */
  protected readonly examIdForLinks = this.route.paramMap.get('id') ?? '';

  // On the preview route the id in the address is the exam's; the preview API reads it as such.
  private readonly attemptId = this.route.paramMap.get(this.preview ? 'id' : 'attemptId');

  protected readonly attempt = signal<AttemptDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly confirmingSubmit = signal(false);
  protected readonly submitting = signal(false);

  /** Answers and marks still being sent to the server, and what to do when the last one has been answered. */
  private inFlight = 0;
  private whenIdle: (() => void)[] = [];
  protected readonly remainingSeconds = signal(0);

  /** The question text size, as a multiple of normal. */
  protected readonly zoom = signal<number>(loadZoomLevel());
  /** Whether the stronger black-and-white colour scheme is on. */
  protected readonly highContrast = signal(loadHighContrast());
  protected readonly canZoomOut = computed(() => this.zoom() > ZOOM_LEVELS[0]);
  protected readonly canZoomIn = computed(() => this.zoom() < ZOOM_LEVELS[ZOOM_LEVELS.length - 1]);

  protected readonly questions = computed(() => this.attempt()?.sections.flatMap((s) => s.questions) ?? []);
  protected readonly answeredCount = computed(() => this.questions().filter((q) => chosenOptionIds(q).length > 0).length);
  protected readonly markedCount = computed(() => this.questions().filter((q) => q.markedForReview).length);
  protected readonly isOpen = computed(() => this.attempt()?.status === 'InProgress');

  /** Whether an administrator has paused the attempt (FR-29): the questions are hidden and the clock is stopped until it is resumed. */
  protected readonly paused = computed(() => this.isOpen() && (this.attempt()?.pausedAtUtc ?? null) !== null);

  private readonly dismissedWarnings = signal(loadDismissedWarnings(this.attemptId));

  /** The warnings administrators sent that the candidate has not dismissed yet, oldest first. */
  protected readonly unreadWarnings = computed<AttemptWarningDto[]>(() =>
    this.isOpen() ? (this.attempt()?.warnings ?? []).filter((w) => !this.dismissedWarnings().has(w.id)) : [],
  );

  /**
   * Whether copying, pasting, right-click and printing are turned off right now (FR-23): only while the exam is open, and
   * unless the author lifted it. The result and the review are the candidate's own, so they are free to copy and print.
   */
  protected readonly protectContent = computed(() => !this.preview && this.isOpen() && this.attempt()?.contentProtection !== false);

  /** The sentence saying what was just refused, or null. It clears itself, so the page does not fill up with warnings. */
  protected readonly protectionNotice = signal<string | null>(null);

  private noticeTimer: ReturnType<typeof setTimeout> | undefined;

  private readonly guard = new ContentGuard(inject(DOCUMENT), (action) => this.showNotice(action));

  /**
   * How many times the candidate may leave the exam page right now (FR-22): only while the exam is open, and 0 when the exam does
   * not watch. The server holds the count and ends the attempt; the page only reports and shows what it is told.
   */
  protected readonly focusLimit = computed(() => (this.isOpen() ? (this.attempt()?.focusViolationLimit ?? 0) : 0));
  // Not while an administrator has paused the attempt: the candidate was told to wait, so stepping away is not a departure.
  protected readonly watchFocus = computed(() => !this.preview && this.focusLimit() > 0 && !this.paused());

  /** How many times the candidate has left so far, as the server last said. */
  protected readonly focusViolations = signal(0);

  /** What the candidate was just told about leaving, until they dismiss it. Shown as an alert: they have just come back and need to see it. */
  protected readonly focusWarning = signal<string | null>(null);

  /** Whether the page is full screen right now, so the offer to enter it shows only when it helps. */
  protected readonly inFullscreen = signal(false);

  private readonly focus = new FocusMonitor(inject(DOCUMENT), (kind) => this.reportDeparture(kind));

  /** Whether to offer full screen: the exam watches, the browser allows it, and the candidate is not in it. */
  protected readonly canOfferFullscreen = computed(() => this.watchFocus() && this.focus.canEnterFullscreen && !this.inFullscreen());

  /**
   * The questions the candidate has had on screen, so the palette can tell "not visited" from "not answered". It is
   * kept in this browser, per attempt: a visit is not scored or sent to the server, so it survives a reload
   * but not a change of device, where only answers and review marks follow the candidate.
   */
  private readonly visited = signal<ReadonlySet<string>>(
    this.attemptId === null ? new Set() : loadVisitedQuestions(this.attemptId),
  );

  /** Which question is on screen, as an index into the flat list of every section's questions. */
  private readonly requestedIndex = signal(0);

  /** Where each section's questions sit in the flat list: the first index and the last. */
  private readonly sectionRanges = computed(() => {
    let start = 0;
    return (this.attempt()?.sections ?? []).map((section) => {
      const range = { id: section.id, name: section.name, start, end: start + section.questions.length - 1 };
      start += section.questions.length;
      return range;
    });
  });

  protected readonly sectionLocked = computed(() => this.attempt()?.sectionLockEnabled === true);

  /** The section the candidate is in when sections are locked (the first until the server says otherwise); null when they roam. */
  private readonly activeRange = computed(() => {
    if (!this.sectionLocked()) {
      return null;
    }
    const ranges = this.sectionRanges();
    return ranges.find((r) => r.id === this.attempt()?.activeSectionId) ?? ranges[0] ?? null;
  });

  /** The question on screen. Clamped into the open section when sections are locked, and into the exam always, so a reload can never leave it pointing nowhere. */
  protected readonly position = computed(() => {
    const active = this.activeRange();
    const last = Math.max(0, this.questions().length - 1);
    return Math.min(Math.max(this.requestedIndex(), active?.start ?? 0), active?.end ?? last);
  });
  /** The section the candidate has asked to move to and is being asked to confirm; null when no move is pending. */
  protected readonly leavingTo = signal<{ id: string; name: string; start: number } | null>(null);
  protected readonly currentQuestion = computed<AttemptQuestionDto | null>(() => this.questions()[this.position()] ?? null);
  /** First in the open section when sections are locked: the section before it cannot be returned to. */
  protected readonly isFirst = computed(() => this.position() <= (this.activeRange()?.start ?? 0));
  protected readonly isLast = computed(() => this.position() >= this.questions().length - 1);

  /** True on the last question of a locked section that has another after it, where "next" means leaving the section. */
  protected readonly nextLeavesSection = computed(() => {
    const active = this.activeRange();
    return active !== null && this.position() === active.end && !this.isLast();
  });

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
    const active = this.activeRange();
    return (this.attempt()?.sections ?? []).map((section) => {
      const items = section.questions.map((question, i) => {
        const answered = chosenOptionIds(question).length > 0;
        const marked = question.markedForReview;
        const seen = !answered && !marked && visited.has(question.id);
        return {
          index: offset + i,
          number: offset + i + 1,
          answered,
          marked,
          seen,
          status: paletteStatus(answered, marked, seen),
          // A section behind the candidate is closed for good; one ahead can be moved to (after a confirmation).
          closed: active !== null && offset + i < active.start,
        };
      });
      offset += section.questions.length;
      return { id: section.id, name: section.name, items };
    });
  });

  /**
   * What the pre-submit confirmation tells the candidate, per section and in all: how many questions are answered, marked for
   * review, and never opened. "Not visited" counts only questions with no answer and no mark, the same rule as the palette, so
   * the two always agree.
   */
  protected readonly submitSummary = computed(() => {
    const sections = this.palette().map((section) => ({
      id: section.id,
      name: section.name,
      total: section.items.length,
      answered: section.items.filter((item) => item.answered).length,
      marked: section.items.filter((item) => item.marked).length,
      notVisited: section.items.filter((item) => !item.answered && !item.marked && !item.seen).length,
    }));
    return { sections, notVisited: sections.reduce((sum, section) => sum + section.notVisited, 0) };
  });

  /** Server clock minus the candidate's clock at the moment the attempt was fetched, so the countdown tracks the server. */
  private clockOffsetMs = 0;
  private timer: ReturnType<typeof setInterval> | null = null;
  private heartbeat: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.stopTimer();
      // Leaving the exam page must hand the clipboard, the menu and printing back, whatever the page was doing.
      this.guard.stop();
      // And stop counting departures: leaving this page is not leaving the exam once it is over.
      this.focus.stop();
      clearTimeout(this.noticeTimer);
    });
    effect(() => (this.protectContent() ? this.guard.start() : this.guard.stop()));
    effect(() => (this.watchFocus() ? this.focus.start() : this.focus.stop()));

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

  @HostListener('document:fullscreenchange')
  protected onFullscreenChange(): void {
    this.inFullscreen.set(this.focus.isFullscreen);
  }

  /** Asks for full screen; must come from the candidate's own click. */
  protected goFullscreen(): void {
    void this.focus.enterFullscreen();
  }

  protected dismissWarning(warning: AttemptWarningDto): void {
    const dismissed = new Set(this.dismissedWarnings()).add(warning.id);
    this.dismissedWarnings.set(dismissed);
    if (this.attemptId !== null) {
      try {
        sessionStorage.setItem(dismissedWarningsKey(this.attemptId), JSON.stringify([...dismissed]));
      } catch {
        // A blocked store only means a reload shows the warning once more.
      }
    }
  }

  protected dismissFocusWarning(): void {
    this.focusWarning.set(null);
  }

  /**
   * Tells the server the candidate left the page and shows what it answers. The count is the server's, so a candidate cannot reset
   * it by reloading; if the server ended the attempt the page reloads it and shows the result. A report that fails for any other
   * reason (offline) is dropped: the page cannot count what the server did not hear, and nagging about it helps no one.
   */
  private reportDeparture(kind: FocusViolationKind): void {
    const attempt = this.attempt();
    if (attempt === null || !this.isOpen() || this.submitting()) {
      return;
    }

    this.api.reportFocusViolation(attempt.id, kind).subscribe({
      next: (result) => {
        if (result.attemptEnded) {
          this.focusWarning.set(null);
          this.reload();
          return;
        }
        if (result.limit > 0) {
          this.focusViolations.set(result.violations);
          this.focusWarning.set(warningFor(result.violations, result.limit));
        }
      },
      error: (error: unknown) => {
        // 409: the attempt ended under the candidate (time ran out); the saved state is the truth now.
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.reload();
        }
      },
    });
  }

  /** Says, briefly and calmly, what was just turned off. */
  private showNotice(action: BlockedAction): void {
    this.protectionNotice.set(BLOCKED_MESSAGES[action]);
    clearTimeout(this.noticeTimer);
    this.noticeTimer = setTimeout(() => this.protectionNotice.set(null), NOTICE_MS);
  }

  /** The time left as m:ss (or h:mm:ss), for the countdown. */
  protected formatRemaining(totalSeconds: number): string {
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    const pad = (value: number) => value.toString().padStart(2, '0');
    return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`;
  }

  /**
   * Shows the question at <paramref name="index"/> (0-based); an index outside the exam is ignored. With sections locked,
   * a question in a section already left is ignored and one in a later section asks for confirmation first.
   */
  protected goTo(index: number): void {
    if (index < 0 || index >= this.questions().length) {
      return;
    }

    const active = this.activeRange();
    if (active !== null && (index < active.start || index > active.end)) {
      const target = this.sectionRanges().find((r) => index >= r.start && index <= r.end);
      if (target !== undefined && target.start > active.end) {
        this.leavingTo.set({ id: target.id, name: target.name, start: index });
      }
      return;
    }

    this.requestedIndex.set(index);
    this.markCurrentVisited();
  }

  protected next(): void {
    this.goTo(this.position() + 1);
  }

  protected stayInSection(): void {
    this.leavingTo.set(null);
  }

  /** Tells the server the candidate is leaving their section for good, then shows the question they chose. */
  protected confirmLeaveSection(): void {
    const attempt = this.attempt();
    const target = this.leavingTo();
    if (attempt === null || target === null) {
      return;
    }

    this.errorMessage.set(null);
    this.api.moveToSection(attempt.id, target.id).subscribe({
      next: () => {
        this.leavingTo.set(null);
        this.attempt.update((current) => (current === null ? null : { ...current, activeSectionId: target.id }));
        this.requestedIndex.set(target.start);
        this.markCurrentVisited();
      },
      error: (error: unknown) => {
        this.leavingTo.set(null);
        this.explainFailure(error, 'You could not move to that section. Please try again.');
      },
    });
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

  /** Turns the high contrast colours on or off and remembers the choice in this browser. */
  protected toggleHighContrast(): void {
    const next = !this.highContrast();
    this.highContrast.set(next);
    try {
      localStorage.setItem(CONTRAST_STORAGE_KEY, next ? 'on' : 'off');
    } catch {
      // A blocked store only means the choice is forgotten on the next visit; the change on screen still applies.
    }
  }

  /**
   * Keyboard shortcuts while sitting the exam: N next, P previous, M mark for review, C clear response.
   * They stand down while a confirmation is open, so a stray key cannot move the candidate under a question they are being asked.
   */
  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    const question = this.currentQuestion();
    const shortcut = shortcutFor(event);
    if (shortcut === null || question === null || !this.isOpen() || this.confirmingSubmit() || this.leavingTo() !== null) {
      return;
    }

    event.preventDefault();
    switch (shortcut) {
      case 'next':
        this.next();
        break;
      case 'previous':
        this.previous();
        break;
      case 'mark':
        this.toggleMark(question);
        break;
      case 'clear':
        this.clearResponse(question);
        break;
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

  /**
   * Counts a save while it runs. A submit waits for every save already sent: otherwise the last answer, chosen a moment before the
   * submit, can reach the server after the attempt was scored and be left out of the score.
   */
  private tracked<T>(request: Observable<T>): Observable<T> {
    this.inFlight++;
    return request.pipe(
      finalize(() => {
        this.inFlight--;
        if (this.inFlight === 0) {
          const waiting = this.whenIdle;
          this.whenIdle = [];
          waiting.forEach((resume) => resume());
        }
      }),
    );
  }

  /** Runs <paramref name="action"/> once no save is in flight; at once when none is. */
  private afterSaves(action: () => void): void {
    if (this.inFlight === 0) {
      action();
    } else {
      this.whenIdle.push(action);
    }
  }

  /** Records a choice. It shows at once and is saved in the background; if the save fails the previous choice comes back. */
  protected choose(question: AttemptQuestionDto, optionId: string): void {
    const attempt = this.attempt();
    if (attempt === null || !this.isOpen()) {
      return;
    }

    const previous = chosenOptionIds(question);
    if (question.allowsMultiple) {
      this.toggleOption(attempt.id, question, optionId, previous);
      return;
    }

    if (previous.length === 1 && previous[0] === optionId) {
      return;
    }

    this.setSelection(question.id, [optionId]);
    this.errorMessage.set(null);

    this.tracked(this.api.saveAnswer(attempt.id, question.id, optionId)).subscribe({
      error: (error: unknown) => {
        this.setSelection(question.id, previous);
        this.explainFailure(error, 'Your answer could not be saved. Please try again.');
      },
    });
  }

  /** Whether the question has any answer to clear. */
  protected isAnswered(question: AttemptQuestionDto): boolean {
    return chosenOptionIds(question).length > 0;
  }

  /** Whether the option is one of those chosen for the question; what a radio or a checkbox shows as ticked. */
  protected isChosen(question: AttemptQuestionDto, optionId: string): boolean {
    return chosenOptionIds(question).includes(optionId);
  }

  /**
   * Ticks or unticks one option of a multiple-answer question and saves the whole set, since the set is the answer. Unticking the
   * last one takes the answer back, which is the same as clearing it: a question is either answered with something or not at all.
   */
  private toggleOption(attemptId: string, question: AttemptQuestionDto, optionId: string, previous: string[]): void {
    const next = previous.includes(optionId) ? previous.filter((id) => id !== optionId) : [...previous, optionId];
    this.setSelection(question.id, next);
    this.errorMessage.set(null);

    const save = next.length === 0 ? this.api.clearAnswer(attemptId, question.id) : this.api.saveAnswers(attemptId, question.id, next);
    this.tracked(save).subscribe({
      error: (error: unknown) => {
        this.setSelection(question.id, previous);
        this.explainFailure(error, 'Your answer could not be saved. Please try again.');
      },
    });
  }

  /** Takes back the question's answer. It shows at once and is saved in the background; if the save fails the answer comes back. */
  protected clearResponse(question: AttemptQuestionDto): void {
    const attempt = this.attempt();
    const previous = chosenOptionIds(question);
    if (attempt === null || !this.isOpen() || previous.length === 0) {
      return;
    }

    this.setSelection(question.id, []);
    this.errorMessage.set(null);

    this.tracked(this.api.clearAnswer(attempt.id, question.id)).subscribe({
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

    // Ending a preview scores nothing and stores nothing; it goes back to the exam being edited.
    if (this.preview) {
      void this.router.navigate(['/exams', this.examIdForLinks]);
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.afterSaves(() => this.sendSubmit(attempt.id, true));
  }

  /** Sends the submit. One that clashed with another request for the attempt is sent once more: submitting is safe to repeat. */
  private sendSubmit(attemptId: string, mayRetry: boolean): void {
    this.api.submitAttempt(attemptId).subscribe({
      next: (submitted) => {
        this.submitting.set(false);
        this.confirmingSubmit.set(false);
        this.show(submitted);
      },
      error: (error: unknown) => {
        if (mayRetry && error instanceof HttpErrorResponse && error.status === 409) {
          this.sendSubmit(attemptId, false);
          return;
        }
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private show(attempt: AttemptDto): void {
    this.loading.set(false);
    this.attempt.set(attempt);
    this.focusViolations.set(attempt.focusViolations ?? 0);
    this.stopTimer();

    if (attempt.status === 'InProgress') {
      this.markCurrentVisited();
      this.clockOffsetMs = Date.parse(attempt.serverTimeUtc) - Date.now();
      this.tick();
      this.timer = setInterval(() => this.tick(), TICK_MS);
      // A preview has no attempt on the server to pause, warn or end.
      if (!this.preview) {
        this.heartbeat = setInterval(() => this.checkStatus(), HEARTBEAT_MS);
      }
    }
  }

  /**
   * Asks the server how the attempt stands, so an administrator's pause, resume, warning or termination reaches the candidate within
   * seconds. The server's answer replaces what the page holds: the deadline moves after a pause is resumed, and anything that is no
   * longer open reloads as the result. A failed check is ignored; the next one tries again.
   */
  private checkStatus(): void {
    if (this.attemptId === null || !this.isOpen()) {
      return;
    }

    this.api.getAttemptStatus(this.attemptId).subscribe({
      next: (status) => this.applyStatus(status),
      error: () => undefined,
    });
  }

  private applyStatus(status: AttemptStatusDto): void {
    if (status.status !== 'InProgress') {
      this.reload();
      return;
    }

    this.clockOffsetMs = Date.parse(status.serverTimeUtc) - Date.now();
    this.attempt.update((attempt) =>
      attempt === null
        ? null
        : { ...attempt, pausedAtUtc: status.pausedAtUtc, deadlineUtc: status.deadlineUtc, warnings: status.warnings, serverTimeUtc: status.serverTimeUtc },
    );
    this.tick();
  }

  /** Notes that the question on screen has been seen. */
  private markCurrentVisited(): void {
    const id = this.currentQuestion()?.id;
    if (id !== undefined) {
      if (this.visited().has(id)) {
        return;
      }
      const seen = new Set(this.visited()).add(id);
      this.visited.set(seen);
      if (this.attemptId !== null) {
        saveVisitedQuestions(this.attemptId, seen);
      }
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

    // While paused the clock is stopped at the moment of the pause, so what is left stays what it was.
    const pausedAt = attempt.pausedAtUtc ?? null;
    const now = pausedAt === null ? Date.now() + this.clockOffsetMs : Date.parse(pausedAt);
    const remainingMs = Date.parse(attempt.deadlineUtc) - now;
    this.remainingSeconds.set(Math.max(0, Math.ceil(remainingMs / 1000)));

    // A preview that runs out of time just shows zero: there is no attempt for the server to close, and reloading would redraw the paper.
    if (remainingMs <= 0 && pausedAt === null && !this.preview) {
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

  private setSelection(questionId: string, optionIds: string[]): void {
    this.changeQuestion(questionId, { selectedOptionId: optionIds[0] ?? null, selectedOptionIds: optionIds });
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
    if (this.heartbeat !== null) {
      clearInterval(this.heartbeat);
      this.heartbeat = null;
    }
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
