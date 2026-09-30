import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { GuardianApiService } from '../guardian-api.service';
import { AuthSessionService } from '../../auth/auth-session.service';

@Component({
  selector: 'app-guardian-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="guardian-register-container">
      <h2>Register as Guardian</h2>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="guardian-form">
        <div class="form-group">
          <label for="email">Email *</label>
          <input
            type="email"
            id="email"
            formControlName="email"
            placeholder="guardian@example.com"
            class="form-control"
          />
          @if (form.get('email')?.invalid && form.get('email')?.touched) {
            <div class="error-text">Valid email is required</div>
          }
        </div>

        <div class="form-group">
          <label for="fullName">Full Name *</label>
          <input
            type="text"
            id="fullName"
            formControlName="fullName"
            placeholder="Enter your full name"
            class="form-control"
          />
          @if (form.get('fullName')?.invalid && form.get('fullName')?.touched) {
            <div class="error-text">Full name is required</div>
          }
        </div>

        <div class="form-group">
          <label for="phone">Phone (Optional)</label>
          <input
            type="tel"
            id="phone"
            formControlName="phone"
            placeholder="+1 (555) 123-4567"
            class="form-control"
          />
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Registering...' : 'Register' }}
          </button>
          <a routerLink="/login" class="btn btn-secondary">Back to Login</a>
        </div>

        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
        @if (success) {
          <div class="success-message">
            Registration successful! You can now link candidates to your account.
          </div>
        }
      </form>
    </div>
  `,
  styles: [`
    .guardian-register-container { max-width: 600px; margin: 0 auto; padding: 2rem; }
    .guardian-form { background: white; padding: 2rem; border-radius: 8px; border: 1px solid #ddd; }
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
export class GuardianRegister implements OnInit {
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
      email: ['', [Validators.required, Validators.email]],
      fullName: ['', Validators.required],
      phone: [''],
    });
  }

  onSubmit() {
    if (!this.form.valid) return;

    this.loading = true;
    this.error = '';
    this.success = false;

    const request = {
      email: this.form.value.email,
      fullName: this.form.value.fullName,
      phone: this.form.value.phone,
    };

    this.guardianApi.registerGuardian(request).subscribe({
      next: (guardian) => {
        this.loading = false;
        this.success = true;
        this.authSession.login(guardian.id);
        setTimeout(() => {
          this.router.navigate(['/guardian']);
        }, 2000);
      },
      error: (err) => {
        this.error = 'Failed to register guardian';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
