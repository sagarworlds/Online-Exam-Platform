import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { BatchApiService } from '../batch-api.service';

@Component({
  selector: 'app-batch-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="page">
      <h1>Create New Batch</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="field">
          <label for="examId">Exam *</label>
          <input
            type="text"
            id="examId"
            formControlName="examId"
            placeholder="Enter exam ID"
          />
          @if (form.get('examId')?.invalid && form.get('examId')?.touched) {
            <div class="field-error">Exam ID is required</div>
          }
        </div>

        <div class="field">
          <label for="name">Batch Name *</label>
          <input
            type="text"
            id="name"
            formControlName="name"
            placeholder="Enter batch name"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <div class="field-error">Batch name is required</div>
          }
        </div>

        <div class="field">
          <label for="description">Description</label>
          <textarea
            id="description"
            formControlName="description"
            placeholder="Enter batch description"
            rows="3"
          ></textarea>
        </div>

        <div class="field">
          <label for="maxMembers">Maximum Members *</label>
          <input
            type="number"
            id="maxMembers"
            formControlName="maxMembers"
            placeholder="e.g., 50"
            min="1"
          />
          @if (form.get('maxMembers')?.invalid && form.get('maxMembers')?.touched) {
            <div class="field-error">Maximum members must be greater than 0</div>
          }
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn--primary">
            {{ loading ? 'Creating...' : 'Create Batch' }}
          </button>
          <a routerLink="/batches" class="btn">Cancel</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </form>
    </div>
  `
})
export class BatchCreate implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
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

    const request = {
      examId: this.form.value.examId,
      name: this.form.value.name,
      description: this.form.value.description,
      maxMembers: parseInt(this.form.value.maxMembers),
    };

    this.batchApi.createBatch(request).subscribe({
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
