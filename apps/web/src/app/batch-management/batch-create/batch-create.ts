import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { BatchApiService } from '../batch-api.service';
import { AuthSessionService } from '../../auth/auth-session.service';

@Component({
  selector: 'app-batch-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="batch-create-container">
      <h2>Create New Batch</h2>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="batch-form">
        <div class="form-group">
          <label for="examId">Exam *</label>
          <input
            type="text"
            id="examId"
            formControlName="examId"
            placeholder="Enter exam ID"
            class="form-control"
          />
          @if (form.get('examId')?.invalid && form.get('examId')?.touched) {
            <div class="error-text">Exam ID is required</div>
          }
        </div>

        <div class="form-group">
          <label for="name">Batch Name *</label>
          <input
            type="text"
            id="name"
            formControlName="name"
            placeholder="Enter batch name"
            class="form-control"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <div class="error-text">Batch name is required</div>
          }
        </div>

        <div class="form-group">
          <label for="description">Description</label>
          <textarea
            id="description"
            formControlName="description"
            placeholder="Enter batch description"
            class="form-control"
            rows="3"
          ></textarea>
        </div>

        <div class="form-group">
          <label for="maxMembers">Maximum Members *</label>
          <input
            type="number"
            id="maxMembers"
            formControlName="maxMembers"
            placeholder="e.g., 50"
            class="form-control"
            min="1"
          />
          @if (form.get('maxMembers')?.invalid && form.get('maxMembers')?.touched) {
            <div class="error-text">Maximum members must be greater than 0</div>
          }
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Creating...' : 'Create Batch' }}
          </button>
          <a routerLink="/batches" class="btn btn-secondary">Cancel</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </form>
    </div>
  `,
  styles: [`
    .batch-create-container { max-width: 600px; margin: 0 auto; padding: 2rem; }
    .batch-form { background: white; padding: 2rem; border-radius: 8px; border: 1px solid #ddd; }
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
export class BatchCreate implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
  private authSession = inject(AuthSessionService);
  private router = inject(Router);

  form!: FormGroup;
  loading = false;
  error = '';

  ngOnInit() {
    this.form = this.fb.group({
      examId: ['', Validators.required],
      name: ['', Validators.required],
      description: [''],
      maxMembers: [50, [Validators.required, Validators.min(1)]],
    });
  }

  onSubmit() {
    if (!this.form.valid) return;

    this.loading = true;
    this.error = '';

    const session = this.authSession.session();
    if (!session?.sub) {
      this.error = 'Not authenticated';
      this.loading = false;
      return;
    }

    const request = {
      examId: this.form.value.examId,
      name: this.form.value.name,
      description: this.form.value.description,
      maxMembers: parseInt(this.form.value.maxMembers),
    };

    this.batchApi.createBatch(request, session.sub).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/batches']);
      },
      error: (err) => {
        this.error = 'Failed to create batch';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
