import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { extractErrorMessage } from '../../shared/problem-details';
import { GuardianApiService } from '../guardian-api.service';

@Component({
  selector: 'app-guardian-link',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="page">
      <h1>Link Candidate</h1>
      <p class="hint">Link a candidate to a guardian. The guardian is e-mailed a request to confirm the link.</p>

      <form class="card" [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="field">
          <label for="guardianEmail">Guardian e-mail *</label>
          <input
            type="email"
            id="guardianEmail"
            formControlName="guardianEmail"
            placeholder="guardian@example.com"
          />
          @if (form.get('guardianEmail')?.invalid && form.get('guardianEmail')?.touched) {
            <div class="field-error">Valid guardian e-mail is required</div>
          }
          <p class="field-hint">The guardian must already be registered.</p>
        </div>

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
          <button type="submit" [disabled]="!form.valid || loading()" class="btn btn--primary">
            {{ loading() ? 'Linking...' : 'Link Candidate' }}
          </button>
          <a routerLink="/guardian" class="btn">Cancel</a>
        </div>

        <div class="outcome">
          @if (error()) {
            <div class="error-message" role="alert">{{ error() }}</div>
          }
          @if (success()) {
            <div class="success-message" role="status">
              Candidate linked. The guardian has been e-mailed a request to confirm the link.
            </div>
          }
          @if (confirmLink(); as link) {
            <div class="warning-message" role="status">
              The candidate is linked, but the e-mail to the guardian could not be sent, so the link is not confirmed yet.
              Pass the confirmation link below to the guardian yourself. It works once and expires in 14 days.
            </div>
            <div class="field">
              <label for="confirmLink">Confirmation link</label>
              <!-- Selected on focus so the whole link can be copied in one step; a textarea wraps where a one-line input would cut it off. -->
              <textarea id="confirmLink" rows="3" readonly [value]="link" (focus)="$any($event.target).select()"></textarea>
              <p class="field-hint">Select the link and copy it.</p>
            </div>
          }
        </div>
      </form>
    </div>
  `,
  styles: ['.outcome { margin-top: 1rem; }', '.actions .btn { min-height: 2.75rem; }'],
})
export class GuardianLink implements OnInit {
  private fb = inject(FormBuilder);
  private guardianApi = inject(GuardianApiService);
  private router = inject(Router);

  form!: FormGroup;
  // Signals rather than plain fields: the app runs without zone.js, so only a signal reliably redraws the page when a response lands.
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal(false);
  /** Set only when no e-mail went out, so the link is shown here for staff to pass on. */
  protected readonly confirmLink = signal('');

  ngOnInit() {
    this.form = this.fb.group({
      guardianEmail: ['', [Validators.required, Validators.email]],
      candidateId: ['', Validators.required],
      candidateEmail: ['', [Validators.required, Validators.email]],
    });
  }

  onSubmit() {
    if (!this.form.valid) return;

    this.loading.set(true);
    this.error.set('');
    this.success.set(false);
    this.confirmLink.set('');

    const { guardianEmail, candidateId, candidateEmail } = this.form.value;
    const request = { candidateId, candidateEmail };

    // Two steps: the guardian's record is found by the address staff have, and the candidate is linked to the record found.
    this.guardianApi
      .findGuardianByEmail(guardianEmail)
      .pipe(switchMap((guardian) => this.guardianApi.linkCandidate(guardian.id, request)))
      .subscribe({
        next: (response) => {
          this.loading.set(false);
          if (response.consentRequestSent) {
            this.success.set(true);
            setTimeout(() => {
              this.router.navigate(['/guardian']);
            }, 2000);
          } else if (response.consentLink) {
            // Nothing was e-mailed. Stay on the page so the link can be copied; leaving would lose it.
            this.confirmLink.set(response.consentLink);
          } else {
            this.error.set('The candidate is linked, but no confirmation link was returned. Ask the platform team to check e-mail setup.');
          }
          this.form.reset();
        },
        error: (err: unknown) => {
          // The API's reason is shown as it is: "no guardian is registered with that address" tells staff what to do next.
          this.error.set(extractErrorMessage(err, 'Failed to link candidate'));
          this.loading.set(false);
          console.error(err);
        },
      });
  }
}
