import { Component, ElementRef, Injector, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
import { focusFirstInvalidFieldAfterRender } from '../../shared/focus-first-invalid-field';
import { extractErrorMessage } from '../../shared/problem-details';
import { BatchApiService } from '../batch-api.service';

@Component({
  selector: 'app-batch-create',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, FormField, FieldControl],
  template: `
    <div class="page">
      <h1>Create New Batch</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
        <p class="hint">Fields marked * are required.</p>

        <app-form-field label="Exam" [required]="true" [error]="errorOf('examId', 'Enter the exam ID.')">
          <input appFieldControl type="text" formControlName="examId" placeholder="Enter exam ID" />
        </app-form-field>

        <app-form-field label="Batch name" [required]="true" [error]="errorOf('name', 'Enter a batch name.')">
          <input appFieldControl type="text" formControlName="name" placeholder="Enter batch name" />
        </app-form-field>

        <app-form-field label="Description" optionalText="(optional)">
          <textarea appFieldControl formControlName="description" placeholder="Enter batch description" rows="3"></textarea>
        </app-form-field>

        <app-form-field
          label="Maximum members"
          [required]="true"
          hint="How many candidates can join the batch."
          [error]="errorOf('maxMembers', 'Enter a number of members, 1 or more.')"
        >
          <input appFieldControl type="number" formControlName="maxMembers" placeholder="e.g., 50" min="1" />
        </app-form-field>

        @if (error()) {
          <div class="error-message" role="alert">{{ error() }}</div>
        }

        <div class="actions">
          <button type="submit" class="btn btn--primary" [attr.aria-busy]="loading()">
            {{ loading() ? 'Creating…' : 'Create batch' }}
          </button>
          <a routerLink="/batches" class="btn">Cancel</a>
        </div>
      </form>
    </div>
  `,
})
export class BatchCreate implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
  private router = inject(Router);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private injector = inject(Injector);

  form!: FormGroup;
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  /** Whether Create has been pressed, so a field is named as wrong only from then on, not while it is still being filled in. */
  protected readonly attempted = signal(false);

  ngOnInit() {
    this.form = this.fb.group({
      examId: ['', Validators.required],
      name: ['', Validators.required],
      description: [''],
      maxMembers: [50, [Validators.required, Validators.min(1)]],
    });
  }

  /** The message for a field that is wrong, once Create has been pressed; null while it is right or not yet asked about. */
  protected errorOf(name: string, message: string): string | null {
    return this.attempted() && this.form?.get(name)?.invalid ? message : null;
  }

  onSubmit() {
    this.attempted.set(true);
    if (this.form.invalid) {
      // Create is never switched off with no reason given: pressing it says what is wrong and goes to the first field that is.
      focusFirstInvalidFieldAfterRender(this.host, this.injector);
      return;
    }
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set('');

    const request = {
      examId: this.form.value.examId,
      name: this.form.value.name,
      description: this.form.value.description,
      maxMembers: parseInt(this.form.value.maxMembers),
    };

    this.batchApi.createBatch(request).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/batches']);
      },
      error: (err: unknown) => {
        this.error.set(extractErrorMessage(err, 'The batch could not be created.'));
        this.loading.set(false);
      },
    });
  }
}
