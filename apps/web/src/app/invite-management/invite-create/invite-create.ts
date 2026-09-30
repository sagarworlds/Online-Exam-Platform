import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { InviteApiService } from '../invite-api.service';
import { AuthSessionService } from '../../auth/auth-session.service';

@Component({
  selector: 'app-invite-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="invite-create-container">
      <h2>Create New Invitation</h2>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="invite-form">
        <div class="form-group">
          <label for="examId">Exam ID *</label>
          <input
            type="text"
            id="examId"
            formControlName="examId"
            placeholder="Enter exam ID"
            class="form-control"
          />
          <div *ngIf="form.get('examId')?.invalid && form.get('examId')?.touched" class="error-text">
            Exam ID is required
          </div>
        </div>

        <div class="form-group">
          <label for="batchMemberId">Batch Member ID *</label>
          <input
            type="text"
            id="batchMemberId"
            formControlName="batchMemberId"
            placeholder="Enter batch member ID"
            class="form-control"
          />
          <div *ngIf="form.get('batchMemberId')?.invalid && form.get('batchMemberId')?.touched" class="error-text">
            Batch member ID is required
          </div>
        </div>

        <div class="form-group">
          <label for="email">Email *</label>
          <input
            type="email"
            id="email"
            formControlName="email"
            placeholder="candidate@example.com"
            class="form-control"
          />
          <div *ngIf="form.get('email')?.invalid && form.get('email')?.touched" class="error-text">
            Valid email is required
          </div>
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Creating...' : 'Create Invitation' }}
          </button>
          <a routerLink="/invites" class="btn btn-secondary">Cancel</a>
        </div>

        <div *ngIf="error" class="error-message">{{ error }}</div>
      </form>
    </div>
  `,
  styles: [`
    .invite-create-container { max-width: 600px; margin: 0 auto; padding: 2rem; }
    .invite-form { background: white; padding: 2rem; border-radius: 8px; border: 1px solid #ddd; }
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
export class InviteCreate implements OnInit {
  private fb = inject(FormBuilder);
  private inviteApi = inject(InviteApiService);
  private authSession = inject(AuthSessionService);
  private router = inject(Router);

  form!: FormGroup;
  loading = false;
  error = '';

  ngOnInit() {
    this.form = this.fb.group({
      examId: ['', Validators.required],
      batchMemberId: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
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
      batchMemberId: this.form.value.batchMemberId,
      email: this.form.value.email,
    };

    this.inviteApi.createInvite(request, session.sub).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/invites']);
      },
      error: (err) => {
        this.error = 'Failed to create invitation';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
