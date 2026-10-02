import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ScheduleExamRequest } from '../exam.models';

/** Admin page that sets when one exam runs: its window, how long an attempt lasts, and the late-entry cutoff (FR-13). */
@Component({
  selector: 'app-exam-scheduler',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './exam-scheduler.html',
})
export class ExamScheduler {
  private readonly formBuilder = inject(FormBuilder);
  private readonly examApi = inject(ExamApiService);
  private readonly router = inject(Router);
  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    startTime: ['', Validators.required],
    endTime: ['', Validators.required],
    timeZone: ['Asia/Kolkata'],
    durationMinutes: this.formBuilder.control<number | null>(null, [Validators.min(1)]),
    lateEntryDeadline: [''],
  });

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      return;
    }

    const value = this.form.getRawValue();
    // A datetime-local field holds the browser's local time with no offset; the API takes UTC instants.
    const request: ScheduleExamRequest = {
      scheduledStartTime: new Date(value.startTime).toISOString(),
      scheduledEndTime: new Date(value.endTime).toISOString(),
      timeZone: value.timeZone,
      durationMinutes: value.durationMinutes,
      lateEntryDeadline: value.lateEntryDeadline ? new Date(value.lateEntryDeadline).toISOString() : null,
    };

    this.saving.set(true);
    this.errorMessage.set(null);
    this.examApi.scheduleExam(this.examId, request).subscribe({
      next: () => {
        this.saving.set(false);
        void this.router.navigate(['/exams', this.examId]);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
