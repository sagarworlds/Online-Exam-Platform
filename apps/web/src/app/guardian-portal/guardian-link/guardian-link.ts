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
    <div class="page">
      <h1>Link Candidate</h1>
      <p class="hint">Link a candidate to your guardian account for consent management.</p>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="field">
          <label for="candidateId">Candidate ID *</label>
          <input
            type="text"
            id="candidateId"
            formControlName="candidateId"
            placeholder="Enter candidate ID"
          />
          @if (form.get('candidateId')?.invalid && form.get('candidateId')?.touched) {
            <div class="field-error">Candidate ID is required</div>
          }
        </div>

        <div class="field">
          <label for="candidateEmail">Candidate Email *</label>
          <input
            type="email"
            id="candidateEmail"
            formControlName="candidateEmail"
            placeholder="candidate@example.com"
          />
          @if (form.get('candidateEmail')?.invalid && form.get('candidateEmail')?.touched) {
            <div class="field-error">Valid email is required</div>
          }
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn--primary">
            {{ loading ? 'Linking...' : 'Link Candidate' }}
          </button>
          <a routerLink="/guardian" class="btn">Cancel</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
        @if (success) {
          <div class="success-message">
            Candidate linked successfully! A verification link has been sent to the candidate.
          </div>
        }
      </form>
    </div>
  `
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
    if (!session?.userId) {
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

    this.guardianApi.linkCandidate(session.userId, request).subscribe({
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
