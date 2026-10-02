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
    <div class="page">
      <h1>Register as Guardian</h1>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="field">
          <label for="email">Email *</label>
          <input
            type="email"
            id="email"
            formControlName="email"
            placeholder="guardian@example.com"
          />
          @if (form.get('email')?.invalid && form.get('email')?.touched) {
            <div class="field-error">Valid email is required</div>
          }
        </div>

        <div class="field">
          <label for="fullName">Full Name *</label>
          <input
            type="text"
            id="fullName"
            formControlName="fullName"
            placeholder="Enter your full name"
          />
          @if (form.get('fullName')?.invalid && form.get('fullName')?.touched) {
            <div class="field-error">Full name is required</div>
          }
        </div>

        <div class="field">
          <label for="phone">Phone (Optional)</label>
          <input
            type="tel"
            id="phone"
            formControlName="phone"
            placeholder="+1 (555) 123-4567"
          />
        </div>

        <div class="actions">
          <button type="submit" [disabled]="!form.valid || loading" class="btn btn--primary">
            {{ loading ? 'Registering...' : 'Register' }}
          </button>
          <a routerLink="/login" class="btn">Back to Login</a>
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
  `
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
