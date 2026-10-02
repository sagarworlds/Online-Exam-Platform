import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Accepts a blank value (no series) or a GUID. The API reads this field as a GUID and answers any other text
 * with an unhelpful "request could not be read", so the form says what is wrong before anything is sent.
 */
export function optionalGuid(control: AbstractControl): ValidationErrors | null {
  const value = String(control.value ?? '').trim();
  return value === '' || GUID_PATTERN.test(value) ? null : { guid: true };
}

@Component({
  selector: 'app-exam-builder',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="page">
      <h1>Create New Exam</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="field">
          <label for="name">Exam Name *</label>
          <input
            type="text"
            id="name"
            formControlName="name"
            placeholder="Enter exam name"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <div class="field-error">Exam name is required</div>
          }
        </div>

        <div class="field">
          <label for="description">Description</label>
          <textarea
            id="description"
            formControlName="description"
            placeholder="Enter exam description"
            rows="4"
          ></textarea>
        </div>

        <div class="field">
          <label for="seriesId">Series ID (Optional)</label>
          <input
            type="text"
            id="seriesId"
            formControlName="seriesId"
            placeholder="Leave blank for a standalone exam"
          />
          @if (form.get('seriesId')?.hasError('guid')) {
            <div class="field-error">
              A series ID looks like 7c9e6679-7425-40de-944b-e07fc1f90ae7. Leave it blank for a standalone exam.
            </div>
          }
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn--primary">
            {{ loading ? 'Creating...' : 'Create Exam' }}
          </button>
          <a routerLink="/exams" class="btn">Cancel</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </form>
    </div>
  `
})
export class ExamBuilder implements OnInit {
  private fb = inject(FormBuilder);
  private examApi = inject(ExamApiService);
  private router = inject(Router);

  form!: FormGroup;
  loading = false;
  error = '';

  ngOnInit() {
    this.form = this.fb.group({
      name: ['', Validators.required],
      description: [''],
      seriesId: ['', optionalGuid],
    });
  }

  onSubmit() {
    if (!this.form.valid) return;

    this.loading = true;
    this.error = '';

    // The API takes null for "no series" and refuses the empty string, which is what a
    // blank field holds, so a blank (or whitespace-only) value is sent as null.
    const seriesId = (this.form.value.seriesId ?? '').trim();

    const request = {
      name: this.form.value.name,
      description: this.form.value.description,
      seriesId: seriesId === '' ? null : seriesId,
    };

    this.examApi.createExam(request).subscribe({
      next: (exam) => {
        this.loading = false;
        // Straight to the editor, where sections and questions are added.
        this.router.navigate(['/exams', exam.id]);
      },
      error: (err: unknown) => {
        this.error = extractErrorMessage(err, 'Failed to create exam');
        this.loading = false;
        console.error(err);
      },
    });
  }
}
