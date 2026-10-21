import { Component, ElementRef, Injector, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { focusFirstInvalidFieldAfterRender } from '../../shared/focus-first-invalid-field';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
import { extractErrorMessage } from '../../shared/problem-details';
import { ToastService } from '../../shared/toast/toast.service';
import { ExamApiService } from '../exam-api.service';
import { ExamDto, ScheduleExamRequest } from '../exam.models';
import { existsInZone, isKnownTimeZone, localToUtcIso, utcToLocalInput } from './zoned-time';

/** The fields that hold a moment. */
type TimeField = 'startTime' | 'endTime' | 'lateEntryDeadline';

/** The time zones an exam can be scheduled in. Names of places, so they are not translated. */
const TIME_ZONES: readonly { id: string; label: string }[] = [
  { id: 'Asia/Kolkata', label: 'India (GMT+5:30)' },
  { id: 'UTC', label: 'UTC' },
  { id: 'Europe/London', label: 'Europe/London' },
  { id: 'America/New_York', label: 'US Eastern' },
  { id: 'America/Los_Angeles', label: 'US Pacific' },
  { id: 'Australia/Sydney', label: 'Australia/Sydney' },
];

/** The zone the form starts in: the browser's own when it is one of the choices, so the times typed are the ones the author means, else India. */
function defaultTimeZone(): string {
  const own = Intl.DateTimeFormat().resolvedOptions().timeZone;
  return TIME_ZONES.some((zone) => zone.id === own) ? own : 'Asia/Kolkata';
}

/** Admin page that sets when one exam runs: its window, how long an attempt lasts, and the late-entry cutoff (FR-13). */
@Component({
  selector: 'app-exam-scheduler',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, FormField, FieldControl],
  templateUrl: './exam-scheduler.html',
})
export class ExamScheduler {
  private readonly formBuilder = inject(FormBuilder);
  private readonly examApi = inject(ExamApiService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether Save has been pressed, so a field is named as wrong only from then on, not while it is still being filled in. */
  protected readonly attempted = signal(false);
  /** Set when the exam could not be read, so the form says its current schedule is not shown rather than looking as if there were none. */
  protected readonly loadFailed = signal(false);
  /** The zones to choose from: the usual ones, and the exam's own when it was scheduled in another. */
  protected readonly zones = signal(TIME_ZONES);
  /** The exam's own zone when this browser does not know it, so the page can say the times are shown in another and what saving will do. */
  protected readonly unknownZone = signal<string | null>(null);

  /**
   * The moments the exam was loaded with, each with the clock text it was shown as. A field left as it was is sent as it was: turning the text
   * back into an instant loses the seconds, and picks one of the two moments of a clock that was set back an hour, so a save that changed
   * nothing would otherwise change the schedule.
   */
  private loaded: { zone: string; fields: Partial<Record<TimeField, { local: string; iso: string }>> } | null = null;

  protected readonly form = this.formBuilder.nonNullable.group({
    startTime: ['', Validators.required],
    endTime: ['', Validators.required],
    timeZone: [defaultTimeZone()],
    durationMinutes: this.formBuilder.control<number | null>(null),
    lateEntryDeadline: [''],
  });

  constructor() {
    // An exam that already has a schedule opens with it, so changing it is a matter of editing, not of typing it all in again.
    this.examApi.getExamById(this.examId).subscribe({
      next: (exam) => this.show(exam),
      error: () => this.loadFailed.set(true),
    });
  }

  private show(exam: ExamDto): void {
    if (!exam.isScheduled) {
      return;
    }

    const known = isKnownTimeZone(exam.timeZone);
    const zone = known ? exam.timeZone : this.form.controls.timeZone.value;
    this.unknownZone.set(known ? null : exam.timeZone);
    if (!this.zones().some((option) => option.id === zone)) {
      this.zones.update((all) => [...all, { id: zone, label: zone }]);
    }
    const minutes = exam.config.totalTimeSeconds ? Math.round(exam.config.totalTimeSeconds / 60) : null;
    const startTime = utcToLocalInput(exam.scheduledStartTime, zone);
    const endTime = utcToLocalInput(exam.scheduledEndTime, zone);
    const lateEntryDeadline = exam.lateEntryDeadline ? utcToLocalInput(exam.lateEntryDeadline, zone) : '';
    this.loaded = {
      zone,
      fields: {
        startTime: { local: startTime, iso: new Date(exam.scheduledStartTime).toISOString() },
        endTime: { local: endTime, iso: new Date(exam.scheduledEndTime).toISOString() },
        ...(exam.lateEntryDeadline ? { lateEntryDeadline: { local: lateEntryDeadline, iso: new Date(exam.lateEntryDeadline).toISOString() } } : {}),
      },
    };
    this.form.patchValue({ timeZone: zone, startTime, endTime, durationMinutes: minutes, lateEntryDeadline });
  }

  /**
   * What is wrong with a field, in words, once Save has been pressed; null while it is right or not yet asked about. The window must close
   * after it opens, a latest start must fall inside the window, and a duration must be a whole number of minutes, 1 or more.
   */
  protected errorOf(field: 'startTime' | 'endTime' | 'durationMinutes' | 'lateEntryDeadline'): string | null {
    const key = this.attempted() ? this.problemWith(field) : null;
    return key === null ? null : this.i18n.t(key);
  }

  private problemWith(field: 'startTime' | 'endTime' | 'durationMinutes' | 'lateEntryDeadline'): MessageKey | null {
    const { startTime, endTime, durationMinutes, lateEntryDeadline, timeZone } = this.form.getRawValue();
    switch (field) {
      case 'startTime':
        if (!startTime) {
          return 'exams.schedule.error.opens';
        }
        return this.isMissing(startTime, timeZone) ? 'exams.schedule.error.noSuchTime' : null;
      case 'endTime':
        if (!endTime) {
          return 'exams.schedule.error.closes';
        }
        if (this.isMissing(endTime, timeZone)) {
          return 'exams.schedule.error.noSuchTime';
        }
        return startTime && endTime <= startTime ? 'exams.schedule.error.endBeforeStart' : null;
      case 'durationMinutes':
        return durationMinutes === null || (Number.isInteger(durationMinutes) && durationMinutes >= 1) ? null : 'exams.schedule.error.minutes';
      case 'lateEntryDeadline':
        if (!lateEntryDeadline) {
          return null;
        }
        if (this.isMissing(lateEntryDeadline, timeZone)) {
          return 'exams.schedule.error.noSuchTime';
        }
        if (!startTime || !endTime) {
          return null;
        }
        return lateEntryDeadline < startTime || lateEntryDeadline > endTime ? 'exams.schedule.error.lateEntry' : null;
    }
  }

  /** Whether the zone's clocks never show this reading, because they went forward over it. A reading left as the exam had it is always one that was. */
  private isMissing(local: string, zone: string): boolean {
    return !this.wasLoaded(local, zone) && !existsInZone(local, zone);
  }

  private wasLoaded(local: string, zone: string): boolean {
    return this.loaded?.zone === zone && Object.values(this.loaded.fields).some((field) => field.local === local);
  }

  /** The instant of a field's reading in the chosen zone: the one the exam came with when the field was not touched. */
  private instantOf(field: TimeField, local: string, zone: string): string {
    const was = this.loaded?.fields[field];
    return was && this.loaded?.zone === zone && was.local === local ? was.iso : localToUtcIso(local, zone);
  }

  protected submit(): void {
    this.attempted.set(true);
    const fields = ['startTime', 'endTime', 'durationMinutes', 'lateEntryDeadline'] as const;
    if (fields.some((field) => this.problemWith(field) !== null)) {
      // Save is not switched off with no reason given: pressing it names what is wrong and goes to the first field that is.
      focusFirstInvalidFieldAfterRender(this.host, this.injector);
      return;
    }
    if (this.saving()) {
      return;
    }

    const value = this.form.getRawValue();
    // A datetime-local field holds a wall-clock reading with no zone; the API takes UTC instants. The reading is in the zone chosen below it.
    const request: ScheduleExamRequest = {
      scheduledStartTime: this.instantOf('startTime', value.startTime, value.timeZone),
      scheduledEndTime: this.instantOf('endTime', value.endTime, value.timeZone),
      timeZone: value.timeZone,
      durationMinutes: value.durationMinutes,
      lateEntryDeadline: value.lateEntryDeadline ? this.instantOf('lateEntryDeadline', value.lateEntryDeadline, value.timeZone) : null,
    };

    this.saving.set(true);
    this.errorMessage.set(null);
    this.examApi.scheduleExam(this.examId, request).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(this.i18n.t('exams.schedule.saved'));
        void this.router.navigate(['/exams', this.examId]);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
