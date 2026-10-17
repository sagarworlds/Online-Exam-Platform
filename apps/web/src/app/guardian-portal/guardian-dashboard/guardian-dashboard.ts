import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { GuardianApiService } from '../guardian-api.service';
import { GuardianLinkDto } from '../guardian.models';
import { AuthSessionService } from '../../auth/auth-session.service';

@Component({
  selector: 'app-guardian-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="page page--wide">
      <h1>Guardian Portal</h1>

      <div class="actions">
        <a routerLink="/guardian/link-candidate" class="btn btn--primary">Link New Candidate</a>
      </div>

      <div class="stat-grid">
        <div class="stat-card">
          <div class="stat-value">{{ links().length }}</div>
          <div class="stat-label">Linked Candidates</div>
        </div>
        <div class="stat-card">
          <div class="stat-value">{{ verifiedCount() }}</div>
          <div class="stat-label">Verified Links</div>
        </div>
      </div>

      @if (loading()) {
        <div class="empty-state">Loading candidates...</div>
      }
      @if (error()) {
        <div class="error-message">{{ error() }}</div>
      }

      @if (!loading() && links().length === 0) {
        <div class="empty-state">
          No candidates linked yet. <a routerLink="/guardian/link-candidate">Link a candidate</a>
        </div>
      }

      @if (!loading() && links().length > 0) {
        <div>
          <h3>Linked Candidates</h3>
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Candidate Email</th>
                  <th>Status</th>
                  <th>Verified</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (link of links(); track link.id) {
                <tr>
                  <td>{{ link.candidateEmail }}</td>
                  <td><span class="badge" [class]="'status-' + link.status">{{ link.status }}</span></td>
                  <td>{{ link.verifiedAt ? (link.verifiedAt | date: 'short') : '-' }}</td>
                  <td>
                    @if (link.status !== 'Revoked') {
                      <button class="btn btn--danger btn--small" (click)="revokeLink(link)">Revoke</button>
                    }
                  </td>
                </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </div>
  `
})
export class GuardianDashboard implements OnInit {
  private guardianApi = inject(GuardianApiService);
  private authSession = inject(AuthSessionService);

  // Signals, not plain fields: the app runs without zone.js, so the page is redrawn only for what a signal tells it about. A plain field set
  // when the answer arrives would leave "Loading candidates..." on screen for good.
  protected readonly links = signal<GuardianLinkDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly verifiedCount = computed(() => this.links().filter(l => l.status === 'Verified').length);
  guardianId = '';

  ngOnInit() {
    const session = this.authSession.session();
    if (!session?.userId) {
      this.error.set('Not authenticated');
      return;
    }
    this.guardianId = session.userId;
    this.loadLinks();
  }

  loadLinks() {
    this.loading.set(true);
    this.error.set('');
    this.guardianApi.getGuardianLinks(this.guardianId).subscribe({
      next: (data) => {
        this.links.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load linked candidates');
        this.loading.set(false);
        console.error(err);
      },
    });
  }

  revokeLink(link: GuardianLinkDto) {
    if (!confirm(`Revoke link for ${link.candidateEmail}?`)) return;
    this.guardianApi.revokeLink(this.guardianId, link.candidateId).subscribe({
      next: () => {
        this.loadLinks();
      },
      error: (err) => {
        console.error('Failed to revoke link', err);
      },
    });
  }
}
