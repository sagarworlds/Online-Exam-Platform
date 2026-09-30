import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ExamApiService } from '../exam-api.service';

@Component({
  selector: 'app-exam-scheduler',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="scheduler-container">
      <h2>Schedule Exam</h2>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="scheduler-form">
        <div class="form-group">
          <label for="startTime">Start Time *</label>
          <input
            type="datetime-local"
            id="startTime"
            formControlName="startTime"
            class="form-control"
          />
          <div *ngIf="form.get('startTime')?.invalid && form.get('startTime')?.touched" class="error-text">
            Start time is required
          </div>
        </div>

        <div class="form-group">
          <label for="endTime">End Time *</label>
          <input
            type="datetime-local"
            id="endTime"
            formControlName="endTime"
            class="form-control"
          />
          <div *ngIf="form.get('endTime')?.invalid && form.get('endTime')?.touched" class="error-text">
            End time is required
          </div>
        </div>

        <div class="form-group">
          <label for="timeZone">Time Zone *</label>
          <select formControlName="timeZone" class="form-control">
            <option value="">Select time zone</option>
            <option value="UTC">UTC (GMT+0)</option>
            <option value="Asia/Kolkata">India (GMT+5:30)</option>
            <option value="US/Eastern">US Eastern</option>
            <option value="US/Central">US Central</option>
            <option value="US/Mountain">US Mountain</option>
            <option value="US/Pacific">US Pacific</option>
            <option value="Europe/London">Europe/London</option>
            <option value="Europe/Paris">Europe/Paris</option>
            <option value="Australia/Sydney">Australia/Sydney</option>
          </select>
        </div>

        <div class="form-group">
          <label for="lateEntry">Late Entry Deadline</label>
          <input
            type="datetime-local"
            id="lateEntry"
            formControlName="lateEntryDeadline"
            class="form-control"
          />
          <small>Optional: Allow students to join after start time until this deadline</small>
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Scheduling...' : 'Schedule Exam' }}
          </button>
          <a routerLink="/exams" class="btn btn-secondary">Cancel</a>
        </div>

        <div *ngIf="error" class="error-message">{{ error }}</div>
      </form>
    </div>
  `,
  styles: [`
    .scheduler-container { max-width: 500px; margin: 0 auto; padding: 2rem; }
    .scheduler-form { background: white; padding: 2rem; border-radius: 8px; border: 1px solid #ddd; }
    .form-group { margin-bottom: 1.5rem; }
    .form-group label { display: block; margin-bottom: 0.5rem; font-weight: 500; }
    .form-control { width: 100%; padding: 0.5rem; border: 1px solid #ddd; border-radius: 4px; font-size: 1rem; }
    .form-control:focus { outline: none; border-color: #007bff; box-shadow: 0 0 0 3px rgba(0, 123, 255, 0.25); }
    .error-text { color: #dc3545; font-size: 0.875rem; margin-top: 0.25rem; }
    small { color: #666; }
    .actions { display: flex; gap: 1rem; margin-top: 2rem; }
    .btn { padding: 0.5rem 1rem; border: none; border-radius: 4px; cursor: pointer; text-decoration: none; display: inline-block; }
    .btn-primary { background: #007bff; color: white; }
    .btn-primary:disabled { background: #6c757d; cursor: not-allowed; }
    .btn-secondary { background: #6c757d; color: white; }
    .error-message { color: #dc3545; background: #f8d7da; padding: 1rem; border-radius: 4px; border: 1px solid #f5c6cb; margin-top: 1rem; }
  `]
})
export class ExamScheduler implements OnInit {
  private fb = inject(FormBuilder);
  private examApi = inject(ExamApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  examId = '';
  form!: FormGroup;
  loading = false;
  error = '';

  ngOnInit() {
    this.examId = this.route.snapshot.params['id'];
    if (!this.examId) {
      this.router.navigate(['/exams']);
      return;
    }

    this.form = this.fb.group({
      startTime: ['', Validators.required],
      endTime: ['', Validators.required],
      timeZone: ['Asia/Kolkata', Validators.required],
      lateEntryDeadline: [''],
    });
  }

  onSubmit() {
    if (!this.form.valid || !this.examId) return;

    this.loading = true;
    this.error = '';

    const startTime = new Date(this.form.value.startTime);
    const endTime = new Date(this.form.value.endTime);

    this.examApi.scheduleExam(this.examId, startTime, endTime, this.form.value.timeZone).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/exams']);
      },
      error: (err) => {
        this.error = 'Failed to schedule exam';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
