import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ExamApiService } from '../exam-api.service';

@Component({
  selector: 'app-exam-builder',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="exam-builder-container">
      <h2>Create New Exam</h2>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="exam-form">
        <div class="form-group">
          <label for="name">Exam Name *</label>
          <input
            type="text"
            id="name"
            formControlName="name"
            placeholder="Enter exam name"
            class="form-control"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <div class="error-text">Exam name is required</div>
          }
        </div>

        <div class="form-group">
          <label for="description">Description</label>
          <textarea
            id="description"
            formControlName="description"
            placeholder="Enter exam description"
            class="form-control"
            rows="4"
          ></textarea>
        </div>

        <div class="form-group">
          <label for="seriesId">Series ID (Optional)</label>
          <input
            type="text"
            id="seriesId"
            formControlName="seriesId"
            placeholder="Enter series ID"
            class="form-control"
          />
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Creating...' : 'Create Exam' }}
          </button>
          <a routerLink="/exams" class="btn btn-secondary">Cancel</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </form>
    </div>
  `,
  styles: [`
    .exam-builder-container { max-width: 600px; margin: 0 auto; padding: 2rem; }
    .exam-form { background: white; padding: 2rem; border-radius: 8px; border: 1px solid #ddd; }
    .form-group { margin-bottom: 1.5rem; }
    .form-group label { display: block; margin-bottom: 0.5rem; font-weight: 500; }
    .form-control { width: 100%; padding: 0.5rem; border: 1px solid #ddd; border-radius: 4px; font-size: 1rem; }
    .form-control:focus { outline: none; border-color: #007bff; box-shadow: 0 0 0 3px rgba(0, 123, 255, 0.25); }
    .error-text { color: #dc3545; font-size: 0.875rem; margin-top: 0.25rem; }
    .actions { display: flex; gap: 1rem; margin-top: 2rem; }
    .btn { padding: 0.5rem 1rem; border: none; border-radius: 4px; cursor: pointer; text-decoration: none; display: inline-block; }
    .btn-primary { background: #007bff; color: white; }
    .btn-primary:disabled { background: #6c757d; cursor: not-allowed; }
    .btn-secondary { background: #6c757d; color: white; }
    .error-message { color: #dc3545; background: #f8d7da; padding: 1rem; border-radius: 4px; border: 1px solid #f5c6cb; margin-top: 1rem; }
  `]
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
      seriesId: [''],
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
      error: (err) => {
        this.error = 'Failed to create exam';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
