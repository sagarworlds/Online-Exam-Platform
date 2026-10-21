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
import { isKnownTimeZone, localToUtcIso, utcToLocalInput } from './zoned-time';

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

    const zone = isKnownTimeZone(exam.timeZone) ? exam.timeZone : this.form.controls.timeZone.value;
    if (!this.zones().some((known) => known.id === zone)) {
      this.zones.update((all) => [...all, { id: zone, label: zone }]);
    }
    const minutes = exam.config.totalTimeSeconds ? Math.round(exam.config.totalTimeSeconds / 60) : null;
    this.form.patchValue({
      timeZone: zone,
      startTime: utcToLocalInput(exam.scheduledStartTime, zone),
      endTime: utcToLocalInput(exam.scheduledEndTime, zone),
      durationMinutes: minutes,
      lateEntryDeadline: exam.lateEntryDeadline ? utcToLocalInput(exam.lateEntryDeadline, zone) : '',
    });
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
    const { startTime, endTime, durationMinutes, lateEntryDeadline } = this.form.getRawValue();
    switch (field) {
      case 'startTime':
        return startTime ? null : 'exams.schedule.error.opens';
      case 'endTime':
        if (!endTime) {
          return 'exams.schedule.error.closes';
        }
        return startTime && endTime <= startTime ? 'exams.schedule.error.endBeforeStart' : null;
      case 'durationMinutes':
        return durationMinutes === null || (Number.isInteger(durationMinutes) && durationMinutes >= 1) ? null : 'exams.schedule.error.minutes';
      case 'lateEntryDeadline':
        if (!lateEntryDeadline || !startTime || !endTime) {
          return null;
        }
        return lateEntryDeadline < startTime || lateEntryDeadline > endTime ? 'exams.schedule.error.lateEntry' : null;
    }
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
      scheduledStartTime: localToUtcIso(value.startTime, value.timeZone),
      scheduledEndTime: localToUtcIso(value.endTime, value.timeZone),
      timeZone: value.timeZone,
      durationMinutes: value.durationMinutes,
      lateEntryDeadline: value.lateEntryDeadline ? localToUtcIso(value.lateEntryDeadline, value.timeZone) : null,
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
