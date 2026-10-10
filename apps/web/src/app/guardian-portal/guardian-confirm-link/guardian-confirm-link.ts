import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { GuardianApiService } from '../guardian-api.service';
import { GuardianLinkDto } from '../guardian.models';

/**
 * The page a guardian opens from the e-mail to confirm a candidate link. A guardian has no account, so the code in the link is the
 * only proof. Confirming takes a button press rather than happening on page load: mail scanners open links by themselves, and a page
 * that confirmed on load would confirm links that nobody read.
 */
@Component({
  selector: 'app-guardian-confirm-link',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page">
      <h1>Confirm guardianship</h1>

      @if (!token) {
        <div class="error-message" role="alert">
          This page needs the confirmation link from the e-mail. Open the link exactly as it was sent to you.
        </div>
      } @else if (confirmed(); as link) {
        <div class="success-message" role="status">
          Thank you. You have confirmed that you are the guardian of {{ link.candidateEmail }}.
        </div>
      } @else {
        <p class="hint">Confirm only if you are the guardian of the candidate named in the e-mail.</p>
        <div class="actions">
          <button type="button" class="btn btn--primary" [disabled]="loading()" (click)="confirm()">
            {{ loading() ? 'Confirming...' : 'Confirm guardianship' }}
          </button>
        </div>
        @if (error(); as message) {
          <div class="error-message" role="alert">{{ message }}</div>
        }
      }
    </div>
  `,
})
export class GuardianConfirmLink {
  private guardianApi = inject(GuardianApiService);
  private route = inject(ActivatedRoute);

  /** The one-time code from the link; empty when the page was opened without one. */
  protected readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';
  /** The link once the API has confirmed it; the page then says so instead of offering the button again. */
  protected readonly confirmed = signal<GuardianLinkDto | null>(null);
  // Signals, not plain fields: the app runs without zone.js, so a response must mark the page for redraw to show its outcome.
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  /** Sends the confirmation. A second press while one is in flight sends nothing, so the code is used once. */
  confirm(): void {
    if (!this.token || this.loading()) return;

    this.loading.set(true);
    this.error.set('');
    this.guardianApi.verifyLink(this.token).subscribe({
      next: (link) => {
        this.loading.set(false);
        this.confirmed.set(link);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'The link could not be confirmed. Please try again.'));
        console.error(err);
      },
    });
  }
}
