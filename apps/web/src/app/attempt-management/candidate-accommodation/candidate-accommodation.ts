import { Component, computed, inject, input, output, signal } from '@angular/core';
import { extractErrorMessage } from '../../shared/problem-details';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import {
  ACCOMMODATION_FORMATS,
  AccommodationFormat,
  ExamCandidateDto,
  MAX_ACCOMMODATION_MINUTES,
  MAX_ACCOMMODATION_NOTES,
} from '../attempt-admin.models';

/**
 * What one candidate is allowed at an exam because of a disability or another need (FR-49), for the staff who may give it: extra time, a
 * reader or scribe, and alternate formats of the exam page. It shows what is given and a form to change it. The API decides what is
 * acceptable and answers with the candidate's row, which the page shows instead of the old one; this only keeps staff from sending what
 * would be refused.
 */
@Component({
  selector: 'app-candidate-accommodation',
  templateUrl: './candidate-accommodation.html',
})
export class CandidateAccommodation {
  private readonly api = inject(AttemptAdminApiService);

  readonly examId = input.required<string>();
  readonly candidate = input.required<ExamCandidateDto>();

  /** The candidate as the API now reports them, after an accommodation was saved or removed. */
  readonly changed = output<ExamCandidateDto>();

  protected readonly formats = ACCOMMODATION_FORMATS;
  protected readonly maxMinutes = MAX_ACCOMMODATION_MINUTES;
  protected readonly maxNotes = MAX_ACCOMMODATION_NOTES;

  protected readonly editing = signal(false);
  protected readonly confirmingRemove = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** What the last save or removal did, announced to screen readers. */
  protected readonly notice = signal<string | null>(null);

  protected readonly minutes = signal(0);
  protected readonly readerScribe = signal(false);
  protected readonly chosen = signal<ReadonlySet<AccommodationFormat>>(new Set());
  protected readonly notes = signal('');

  /** Whether the form gives anything at all; the API refuses an accommodation that gives nothing, so saving one is not offered. */
  protected readonly givesSomething = computed(() => this.minutes() > 0 || this.readerScribe() || this.chosen().size > 0);
  protected readonly minutesValid = computed(() => Number.isInteger(this.minutes()) && this.minutes() >= 0 && this.minutes() <= MAX_ACCOMMODATION_MINUTES);
  protected readonly canSave = computed(() => !this.busy() && this.givesSomething() && this.minutesValid());

  /** An id for a field of this candidate's form, unique on a page that lists many candidates. */
  protected id(field: string): string {
    return `accommodation-${this.candidate().candidateId}-${field}`;
  }

  protected formatLabel(format: AccommodationFormat): string {
    return ACCOMMODATION_FORMATS.find((f) => f.value === format)?.label ?? format;
  }

  /** Opens the form, filled with what the candidate has so far. */
  protected edit(): void {
    const current = this.candidate().accommodation;
    this.minutes.set(current?.extraTimeMinutes ?? 0);
    this.readerScribe.set(current?.readerScribe ?? false);
    this.chosen.set(new Set(current?.alternateFormats ?? []));
    this.notes.set(current?.notes ?? '');
    this.error.set(null);
    this.notice.set(null);
    this.confirmingRemove.set(false);
    this.editing.set(true);
  }

  protected cancel(): void {
    this.editing.set(false);
    this.error.set(null);
  }

  /** Reads the minutes box; an empty or unreadable box counts as none, which the form then refuses if nothing else is given. */
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
    if (!this.canSave()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const note = this.notes().trim();
    const request = {
      extraTimeMinutes: this.minutes(),
      readerScribe: this.readerScribe(),
      // In the order the formats are offered, whichever order they were ticked in.
      alternateFormats: ACCOMMODATION_FORMATS.map((f) => f.value).filter((value) => this.chosen().has(value)),
      notes: note === '' ? null : note,
    };

    this.api.setAccommodation(this.examId(), this.candidate().candidateId, request).subscribe({
      next: (row) => {
        this.busy.set(false);
        this.editing.set(false);
        this.notice.set('Accommodation saved. A candidate who is sitting now has the extra time added to their attempt.');
        this.changed.emit(row);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(extractErrorMessage(error, 'The accommodation could not be saved. Please try again.'));
      },
    });
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
        this.notice.set('Accommodation removed. An attempt already in progress keeps the time it was given.');
        this.changed.emit(row);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(extractErrorMessage(error, 'The accommodation could not be removed. Please try again.'));
      },
    });
  }
}
