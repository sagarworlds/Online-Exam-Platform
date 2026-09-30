import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { GuardianApiService } from '../guardian-api.service';
import { AuthSessionService } from '../../auth/auth-session.service';

@Component({
  selector: 'app-guardian-link',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="guardian-link-container">
      <h2>Link Candidate</h2>
      <p class="description">Link a candidate to your guardian account for consent management.</p>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="link-form">
        <div class="form-group">
          <label for="candidateId">Candidate ID *</label>
          <input
            type="text"
            id="candidateId"
            formControlName="candidateId"
            placeholder="Enter candidate ID"
            class="form-control"
          />
          <div *ngIf="form.get('candidateId')?.invalid && form.get('candidateId')?.touched" class="error-text">
            Candidate ID is required
          </div>
        </div>

        <div class="form-group">
          <label for="candidateEmail">Candidate Email *</label>
          <input
            type="email"
            id="candidateEmail"
            formControlName="candidateEmail"
            placeholder="candidate@example.com"
            class="form-control"
          />
          <div *ngIf="form.get('candidateEmail')?.invalid && form.get('candidateEmail')?.touched" class="error-text">
            Valid email is required
          </div>
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Linking...' : 'Link Candidate' }}
          </button>
          <a routerLink="/guardian" class="btn btn-secondary">Cancel</a>
        </div>

        <div *ngIf="error" class="error-message">{{ error }}</div>
        <div *ngIf="success" class="success-message">
          Candidate linked successfully! A verification link has been sent to the candidate.
        </div>
      </form>
    </div>
  `,
  styles: [`
    .guardian-link-container { max-width: 600px; margin: 0 auto; padding: 2rem; }
    .description { color: #666; margin-bottom: 2rem; }
    .link-form { background: white; padding: 2rem; border-radius: 8px; border: 1px solid #ddd; }
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
    .success-message { color: #155724; background: #d4edda; padding: 1rem; border-radius: 4px; border: 1px solid #c3e6cb; margin-top: 1rem; }
  `]
})
export class GuardianLink implements OnInit {
  private fb = inject(FormBuilder);
  private guardianApi = inject(GuardianApiService);
  private authSession = inject(AuthSessionService);
  private router = inject(Router);

  form!: FormGroup;
  loading = false;
  error = '';
  success = false;

  ngOnInit() {
    this.form = this.fb.group({
      candidateId: ['', Validators.required],
      candidateEmail: ['', [Validators.required, Validators.email]],
    });
  }

  onSubmit() {
    if (!this.form.valid) return;

    const session = this.authSession.session();
    if (!session?.sub) {
      this.error = 'Not authenticated';
      return;
    }

    this.loading = true;
    this.error = '';
    this.success = false;

    const request = {
      candidateId: this.form.value.candidateId,
      candidateEmail: this.form.value.candidateEmail,
    };

    this.guardianApi.linkCandidate(session.sub, request).subscribe({
      next: () => {
        this.loading = false;
        this.success = true;
        this.form.reset();
        setTimeout(() => {
          this.router.navigate(['/guardian']);
        }, 2000);
      },
      error: (err) => {
        this.error = 'Failed to link candidate';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
