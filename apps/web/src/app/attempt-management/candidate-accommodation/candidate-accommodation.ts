import {
  Component,
  ElementRef,
  Injector,
  Signal,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import {
  ACCOMMODATION_FORMATS,
  Accommodation,
  AccommodationFormat,
  ExamCandidateDto,
  MAX_ACCOMMODATION_MINUTES,
  MAX_ACCOMMODATION_NOTES,
} from '../attempt-admin.models';

/**
 * What one candidate is allowed at an exam because of a disability or another need (FR-49), for the staff who may give it: extra time, a
 * reader or scribe, and alternate formats of the exam page. It shows what is given as a short list of facts, and a form to change it. The
 * API decides what is acceptable and answers with the candidate's row, which the page shows instead of the old one; this only keeps staff
 * from sending what would be refused.
 *
 * A change does not swap the panel out from under the person who made it. A saved or removed accommodation is confirmed in place, and focus
 * moves to that confirmation; a cancelled form or a kept accommodation returns focus to the button that opened it.
 */
@Component({
  selector: 'app-candidate-accommodation',
  imports: [TranslatePipe],
  templateUrl: './candidate-accommodation.html',
  styleUrl: './candidate-accommodation.css',
})
export class CandidateAccommodation {
  private readonly api = inject(AttemptAdminApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);

  readonly examId = input.required<string>();
  readonly candidate = input.required<ExamCandidateDto>();

  /** The candidate as the API now reports them, after an accommodation was saved or removed. */
  readonly changed = output<ExamCandidateDto>();

  // The template names these with "...Line" and "...Button" so they cannot shadow the signals of the same names in the template.
  private readonly noticeRef = viewChild<ElementRef<HTMLElement>>('noticeLine');
  private readonly changeRef = viewChild<ElementRef<HTMLElement>>('changeButton');
  private readonly giveRef = viewChild<ElementRef<HTMLElement>>('giveButton');
  private readonly removeRef = viewChild<ElementRef<HTMLElement>>('removeButton');
  private readonly confirmRef = viewChild<ElementRef<HTMLElement>>('confirmBox');
  private readonly minutesRef = viewChild<ElementRef<HTMLElement>>('minutesBox');

  protected readonly formats = ACCOMMODATION_FORMATS;
  protected readonly maxMinutes = MAX_ACCOMMODATION_MINUTES;
  protected readonly maxNotes = MAX_ACCOMMODATION_NOTES;

  protected readonly editing = signal(false);
  /** Whether Save has been pressed. The form's problems are named only from then on, so nobody is told off before they have tried. */
  protected readonly attempted = signal(false);
  protected readonly confirmingRemove = signal(false);
  protected readonly busy = signal(false);
  /** A problem saving or removing, shown where the person is looking. */
  protected readonly error = signal<string | null>(null);
  /** What the last save or removal did, as a message key so it follows the language chosen. */
  protected readonly notice = signal<MessageKey | null>(null);

  protected readonly minutes = signal(0);
  protected readonly readerScribe = signal(false);
  protected readonly chosen = signal<ReadonlySet<AccommodationFormat>>(new Set());
  protected readonly notes = signal('');

  /** Whether the form gives anything at all; the API refuses an accommodation that gives nothing. */
  protected readonly givesSomething = computed(
    () => this.minutes() > 0 || this.readerScribe() || this.chosen().size > 0,
  );
  protected readonly minutesValid = computed(
    () =>
      Number.isInteger(this.minutes()) &&
      this.minutes() >= 0 &&
      this.minutes() <= MAX_ACCOMMODATION_MINUTES,
  );
  /** The extra-time box's problem, named once Save has been pressed. */
  protected readonly minutesError = computed<MessageKey | null>(() =>
    this.attempted() && !this.minutesValid() ? 'admin.accommodation.minutesError' : null,
  );
  /** The problem with giving nothing, named once Save has been pressed. */
  protected readonly givesNothingError = computed(() => this.attempted() && !this.givesSomething());

  /** An id for a field of this candidate's form, unique on a page that lists many candidates. */
  protected id(field: string): string {
    return `accommodation-${this.candidate().candidateId}-${field}`;
  }

  /** The extra time as words: "15 minutes", or "None" when none is given. */
  protected minutesText(minutes: number): string {
    return minutes > 0
      ? this.i18n.plural('admin.accommodation.minutes', minutes)
      : this.i18n.t('admin.accommodation.noneValue');
  }

  /** The alternate formats given, in the order they are offered, separated by commas; "None" when there are none. */
  protected formatsText(accommodation: Accommodation): string {
    if (accommodation.alternateFormats.length === 0) {
      return this.i18n.t('admin.accommodation.noneValue');
    }
    return ACCOMMODATION_FORMATS.filter((format) =>
      accommodation.alternateFormats.includes(format.value),
    )
      .map((format) => this.i18n.t(format.label))
      .join(', ');
  }

  /** Opens the form, filled with what the candidate has so far, and puts focus on its first field. */
  protected edit(): void {
    const current = this.candidate().accommodation;
    this.minutes.set(current?.extraTimeMinutes ?? 0);
    this.readerScribe.set(current?.readerScribe ?? false);
    this.chosen.set(new Set(current?.alternateFormats ?? []));
    this.notes.set(current?.notes ?? '');
    this.attempted.set(false);
    this.error.set(null);
    this.notice.set(null);
    this.confirmingRemove.set(false);
    this.editing.set(true);
    this.focusLater(this.minutesRef);
  }

  /** Closes the form without sending anything, and gives focus back to the button that opened it. */
  protected cancel(): void {
    this.editing.set(false);
    this.attempted.set(false);
    this.error.set(null);
    this.focusLater(this.changeRef, this.giveRef);
  }

  /** Reads the minutes box; an empty or unreadable box counts as none, which the form then names if nothing else is given. */
  protected setMinutes(value: string): void {
    const parsed = value.trim() === '' ? 0 : Number(value);
    this.minutes.set(Number.isNaN(parsed) ? 0 : parsed);
  }

  protected toggleFormat(format: AccommodationFormat, checked: boolean): void {
    this.chosen.update((current) => {
      const next = new Set(current);
      if (checked) next.add(format);
      else next.delete(format);
      return next;
    });
  }

  protected save(): void {
    this.attempted.set(true);
    if (this.busy() || !this.givesSomething() || !this.minutesValid()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const note = this.notes().trim();
    const request = {
      extraTimeMinutes: this.minutes(),
      readerScribe: this.readerScribe(),
      // In the order the formats are offered, whichever order they were ticked in.
      alternateFormats: ACCOMMODATION_FORMATS.map((f) => f.value).filter((value) =>
        this.chosen().has(value),
      ),
      notes: note === '' ? null : note,
    };

    this.api.setAccommodation(this.examId(), this.candidate().candidateId, request).subscribe({
      next: (row) => {
        this.busy.set(false);
        this.editing.set(false);
        this.attempted.set(false);
        this.notice.set('admin.accommodation.saved');
        this.changed.emit(row);
        this.focusLater(this.noticeRef);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(extractErrorMessage(error, this.i18n.t('admin.accommodation.saveError')));
      },
    });
  }

  /** Asks before removing, in place, and moves focus to the question so the choice is read first. */
  protected askRemove(): void {
    this.confirmingRemove.set(true);
    this.focusLater(this.confirmRef);
  }

  /** Keeps the accommodation, closes the question, and gives focus back to the Remove button. */
  protected keep(): void {
    this.confirmingRemove.set(false);
    this.focusLater(this.removeRef);
  }

  protected remove(): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.api.removeAccommodation(this.examId(), this.candidate().candidateId).subscribe({
      next: (row) => {
        this.busy.set(false);
        this.confirmingRemove.set(false);
        this.notice.set('admin.accommodation.removed');
        this.changed.emit(row);
        this.focusLater(this.noticeRef);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(extractErrorMessage(error, this.i18n.t('admin.accommodation.removeError')));
      },
    });
  }

  /**
   * Moves focus to the first of these controls that is drawn once the change is on screen. The change removes the control that had focus,
   * so without this the focus would fall back to the top of the page.
   */
  private focusLater(...targets: readonly Signal<ElementRef<HTMLElement> | undefined>[]): void {
    afterNextRender(
      () => {
        for (const target of targets) {
          const element = target()?.nativeElement;
          if (element !== undefined) {
            element.focus();
            return;
          }
        }
      },
      { injector: this.injector },
    );
  }
}
