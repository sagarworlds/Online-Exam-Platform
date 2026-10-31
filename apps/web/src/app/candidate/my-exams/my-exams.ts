import { Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import type { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { bestAttemptId } from '../best-attempt';
import { MAX_ATTEMPT_REQUEST_TEXT, MyExamDto } from '../candidate.models';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { rememberAttemptedOpen, rememberedAttemptedOpen } from './attempted-section-memory';
import { groupExams } from './exam-groups';
import { LocaleDatePipe } from '../../i18n/locale-date.pipe';

/** One block of the page: a heading and the exams under it. Only the attempted exams can be hidden. */
interface ExamSection {
  id: 'available' | 'upcoming' | 'attempted';
  titleKey: MessageKey;
  exams: MyExamDto[];
  collapsible: boolean;
}

/** How often the countdowns are worked out again. They are in minutes, so a finer beat would only redraw the same words. */
const COUNTDOWN_TICK_MS = 30_000;

/** How the window of an exam is written: the date and the minute, without seconds, which only make the line longer on a phone. */
const WINDOW_FORMAT = 'MMM d, y, h:mm a';

/**
 * The candidate's page: the exams they have accepted an invitation to, and whether each can be started now (FR-16). Starting leads to
 * the instructions and system check page (FR-17); the attempt itself begins there, once the candidate has acknowledged the instructions.
 *
 * The exams are grouped by what the candidate can do with them: the ones they can take now come first, then the ones still to open, and
 * the ones already attempted or closed come last under a heading that can be folded away. The attempted exams are at the bottom so that
 * showing or hiding them never moves the new exams, and a new exam is never found by scrolling past old results.
 *
 * Each exam is a card the candidate can fold: only the first one on the page starts open, so a candidate with several exams sees the one
 * to do next in full and the rest as a name, a state and one line, and opens whichever they want.
 */
@Component({
  selector: 'app-my-exams',
  imports: [LocaleDatePipe, RouterLink, Skeleton, TranslatePipe],
  templateUrl: './my-exams.html',
  styleUrl: './my-exams.css',
})
export class MyExams {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly exams = signal<MyExamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly bestAttemptId = bestAttemptId;
  protected readonly windowFormat = WINDOW_FORMAT;

  /** The time the countdowns count from, moved on every half minute so "Closes in 1 hour 20 minutes" does not go stale while the page is open. */
  private readonly now = signal(Date.now());

  /** The candidate's own choice to show or hide the attempted exams, remembered on this device; null until they make one. */
  private readonly attemptedChoice = signal<boolean | null>(rememberedAttemptedOpen());

  private readonly groups = computed(() => groupExams(this.exams()));

  /**
   * The blocks of the page, in order. "Available now" is always there, so a candidate with nothing to take is told so rather than left to
   * wonder where the new exams are; the other two appear when they have exams.
   */
  protected readonly sections = computed<ExamSection[]>(() => {
    const { available, upcoming, attempted } = this.groups();
    const sections: ExamSection[] = [{ id: 'available', titleKey: 'myExams.section.available', exams: available, collapsible: false }];
    if (upcoming.length > 0) {
      sections.push({ id: 'upcoming', titleKey: 'myExams.section.upcoming', exams: upcoming, collapsible: false });
    }
    if (attempted.length > 0) {
      sections.push({ id: 'attempted', titleKey: 'myExams.section.attempted', exams: attempted, collapsible: true });
    }
    return sections;
  });

  /**
   * Whether the attempted exams are shown. Hidden unless the candidate chose otherwise, so the new exams are what they see first; but when
   * there is nothing else on the page, they are shown, because a page with only a closed heading would look empty.
   */
  protected readonly attemptedOpen = computed(() => {
    const choice = this.attemptedChoice();
    const { available, upcoming } = this.groups();
    return choice ?? (available.length === 0 && upcoming.length === 0);
  });

  /**
   * The exam whose card is open until the candidate chooses otherwise: the first one on the page, which is the exam to do next, or
   * the most recent one when there is nothing to do. Worked out once, when the list is first read, so reading it again (after a request
   * for another attempt, say) can never fold a card the candidate is looking at because the order moved.
   */
  private readonly openByDefault = signal<string | null>(null);

  /** The cards the candidate has opened or folded themselves, by exam id; a card with no entry follows the default. */
  private readonly cardChoices = signal<ReadonlyMap<string, boolean>>(new Map());

  /** The exam whose "ask for another attempt" form is open; one at a time. */
  protected readonly requestingFor = signal<string | null>(null);
  protected readonly requestMessage = signal('');
  protected readonly requestBusy = signal(false);
  protected readonly requestError = signal<string | null>(null);
  protected readonly maxRequestText = MAX_ATTEMPT_REQUEST_TEXT;

  constructor() {
    this.load();
    const timer = setInterval(() => this.now.set(Date.now()), COUNTDOWN_TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  /** Reads the list of exams. Also what Try again does, so a list that could not be read does not leave the page with nothing to press. */
  protected load(): void {
    this.errorMessage.set(null);
    this.loading.set(true);
    this.api.listMyExams().subscribe({
      next: (exams) => {
        this.exams.set(exams);
        if (this.openByDefault() === null) {
          this.openByDefault.set(this.firstExamId(exams));
        }
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  /**
   * How long until the exam closes, if it is open, or opens, if it has not yet, in words; null for an exam that is over. The one thing a
   * candidate is checking when they come to this page is whether they have time.
   */
  protected countdown(exam: MyExamDto): string | null {
    // A moment that has passed on this device's clock is no countdown: the list is read again when the page is, and says what is true then.
    if (exam.state === 'Open' && Date.parse(exam.endUtc) > this.now()) {
      return this.i18n.t('myExams.closesIn', { time: this.timeUntil(exam.endUtc) });
    }
    if (exam.state === 'NotOpen' && Date.parse(exam.startUtc) > this.now()) {
      return this.i18n.t('myExams.opensIn', { time: this.timeUntil(exam.startUtc) });
    }
    return null;
  }

  /** The time until a moment as its two largest units ("2 days 3 hours", "1 hour 20 minutes", "12 minutes"), or "less than a minute". */
  private timeUntil(utc: string): string {
    const total = Math.floor((Date.parse(utc) - this.now()) / 60_000);
    if (total < 1) {
      return this.i18n.t('time.lessThanMinute');
    }

    const days = Math.floor(total / 1440);
    const hours = Math.floor((total % 1440) / 60);
    const minutes = total % 60;
    const parts: string[] = [];
    if (days > 0) {
      parts.push(this.i18n.plural('time.days', days));
      if (hours > 0) {
        parts.push(this.i18n.plural('time.hours', hours));
      }
    } else if (hours > 0) {
      parts.push(this.i18n.plural('time.hours', hours));
      if (minutes > 0) {
        parts.push(this.i18n.plural('time.minutes', minutes));
      }
    } else {
      parts.push(this.i18n.plural('time.minutes', minutes));
    }
    return parts.join(' ');
  }

  /** The id of the exam shown first on the page, whichever section it is in; null when there are none. */
  private firstExamId(exams: readonly MyExamDto[]): string | null {
    const { available, upcoming, attempted } = groupExams(exams);
    return (available[0] ?? upcoming[0] ?? attempted[0])?.examId ?? null;
  }

  /** Whether an exam's card is open: as the candidate left it, or, if they have not touched it, only the first exam's. */
  protected isOpen(exam: MyExamDto): boolean {
    return this.cardChoices().get(exam.examId) ?? exam.examId === this.openByDefault();
  }

  /** Opens an exam's card, or folds it. Each card is its own: opening one leaves the others as they are. */
  protected toggleCard(exam: MyExamDto): void {
    const choices = new Map(this.cardChoices());
    choices.set(exam.examId, !this.isOpen(exam));
    this.cardChoices.set(choices);
  }

  /**
   * The one line a folded card still says, so closing it does not hide what matters: that the candidate has an attempt in progress, how
   * long they have, or, for an exam that is over, what they scored. Null when there is nothing to add.
   */
  protected cardSummary(exam: MyExamDto): string | null {
    if (this.resumableAttemptId(exam) !== null) {
      return this.i18n.t('myExams.inProgress');
    }
    const countdown = this.countdown(exam);
    if (countdown !== null) {
      return countdown;
    }
    const scored = [...exam.attempts].reverse().find((attempt) => attempt.status === 'Submitted' && attempt.score !== null);
    return scored ? `${this.i18n.t('myExams.yourScore')} ${scored.score} / ${scored.maxScore}` : null;
  }

  /** Shows or hides the attempted exams, and remembers the choice for the next visit. */
  protected toggleAttempted(): void {
    const open = !this.attemptedOpen();
    this.attemptedChoice.set(open);
    rememberAttemptedOpen(open);
  }

  /** Whether attempts need numbering: only when there can be, or already are, more than one. */
  protected showNumbers(exam: MyExamDto): boolean {
    return exam.attemptsAllowed > 1 || exam.attempts.length > 1;
  }

  /** The extra time the candidate's accommodation gives, in words (FR-49); null when it gives none. */
  protected extraTime(exam: MyExamDto): string | null {
    const seconds = exam.accommodation?.extraTimeSeconds ?? 0;
    return seconds > 0 ? this.i18n.plural('time.minutes', Math.round(seconds / 60)) : null;
  }

  /** The label of the button that begins the next attempt. */
  protected startLabel(exam: MyExamDto): string {
    return exam.attempts.length === 0 ? this.i18n.t('myExams.start') : this.i18n.t('myExams.startAttempt', { number: exam.attemptsUsed + 1 });
  }

  /**
   * The attempt the candidate started and has not yet submitted, so they can carry on with it; null when there is none.
   * Only one attempt can be open at a time, so the first match is the one.
   */
  protected resumableAttemptId(exam: MyExamDto): string | null {
    return exam.attempts.find((attempt) => attempt.status === 'InProgress')?.id ?? null;
  }

  /** Whether the card offers "ask for another attempt" now: the API allows it and the request form is not already open. */
  protected canAskForAttempt(exam: MyExamDto): boolean {
    return exam.canRequestAttempt && this.requestingFor() !== exam.examId;
  }

  /** Whether the card has an action row to show: resume, start, or ask for another attempt. */
  protected hasActions(exam: MyExamDto): boolean {
    return this.resumableAttemptId(exam) !== null || exam.canStartAttempt || this.canAskForAttempt(exam);
  }

  /** Opens the form in place of the button that asked for it, and puts focus in its first field, where the candidate starts. */
  protected openRequestForm(exam: MyExamDto): void {
    this.requestMessage.set('');
    this.requestError.set(null);
    this.requestingFor.set(exam.examId);
    afterNextRender(() => this.element(`request-message-${exam.examId}`)?.focus(), { injector: this.injector });
  }

  /** Puts the form away without sending anything, and gives focus back to the button that opened it. */
  protected closeRequestForm(exam: MyExamDto): void {
    this.requestingFor.set(null);
    afterNextRender(() => this.element(`request-attempt-${exam.examId}`)?.focus(), { injector: this.injector });
  }

  /**
   * Sends the request, then reads the list again so the card shows it waiting, whatever else changed meanwhile. The send button is disabled
   * while the request runs, and a disabled control loses focus, so focus is put back on it when the request is refused.
   */
  protected sendRequest(exam: MyExamDto): void {
    if (this.requestBusy()) {
      return;
    }

    this.requestBusy.set(true);
    this.requestError.set(null);
    this.api.requestAttempt(exam.examId, this.requestMessage().trim() || null).subscribe({
      next: () => {
        this.requestBusy.set(false);
        this.requestingFor.set(null);
        // The form goes, and the button that opened it goes once the list is read again, so focus moves to the exam's title that stays.
        afterNextRender(() => this.element(`exam-title-${exam.examId}`)?.focus(), { injector: this.injector });
        this.reload();
      },
      error: (error: unknown) => {
        this.requestBusy.set(false);
        this.requestError.set(extractErrorMessage(error));
        afterNextRender(() => this.element(`request-send-${exam.examId}`)?.focus(), { injector: this.injector });
      },
    });
  }

  private reload(): void {
    this.api.listMyExams().subscribe({
      next: (exams) => this.exams.set(exams),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  /**
   * The element with this id on the page. Ids here come from the exam's id, which is a GUID that may start with a digit, so they are matched
   * as attribute values: a CSS id selector would not parse.
   */
  private element<T extends HTMLElement = HTMLElement>(id: string): T | null {
    return this.host.nativeElement.querySelector<T>(`[id="${id}"]`);
  }
}
