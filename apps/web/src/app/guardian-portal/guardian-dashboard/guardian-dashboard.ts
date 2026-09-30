import { Component, OnInit, inject } from '@angular/core';
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
    <div class="guardian-dashboard-container">
      <h2>Guardian Portal</h2>

      <div class="actions">
        <a routerLink="/guardian/link-candidate" class="btn btn-primary">Link New Candidate</a>
      </div>

      <div class="dashboard-grid">
        <div class="stat-card">
          <div class="stat-value">{{ links.length }}</div>
          <div class="stat-label">Linked Candidates</div>
        </div>
        <div class="stat-card">
          <div class="stat-value">{{ verifiedCount }}</div>
          <div class="stat-label">Verified Links</div>
        </div>
      </div>

      @if (loading) {
        <div class="loading">Loading candidates...</div>
      }
      @if (error) {
        <div class="error">{{ error }}</div>
      }

      @if (!loading && links.length === 0) {
        <div class="empty">
          No candidates linked yet. <a routerLink="/guardian/link-candidate">Link a candidate</a>
        </div>
      }

      @if (!loading && links.length > 0) {
        <div class="candidates-section">
          <h3>Linked Candidates</h3>
          <div class="candidates-table">
            <table>
              <thead>
                <tr>
                  <th>Candidate Email</th>
                  <th>Status</th>
                  <th>Verified</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (link of links; track link.id) {
                <tr>
                  <td>{{ link.candidateEmail }}</td>
                  <td><span class="status" [class]="'status-' + link.status">{{ link.status }}</span></td>
                  <td>{{ link.verifiedAt ? (link.verifiedAt | date: 'short') : '-' }}</td>
                  <td>
                    @if (link.status !== 'Revoked') {
                      <button class="btn btn-danger" (click)="revokeLink(link)">Revoke</button>
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
  `,
  styles: [`
    .guardian-dashboard-container { padding: 2rem; max-width: 1000px; margin: 0 auto; }
    .actions { margin: 1rem 0 2rem 0; }
    .btn { padding: 0.5rem 1rem; text-decoration: none; display: inline-block; border: none; cursor: pointer; border-radius: 4px; }
    .btn-primary { background: #007bff; color: white; }
    .btn-danger { background: #dc3545; color: white; font-size: 0.875rem; padding: 0.25rem 0.5rem; }
    .dashboard-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; margin: 2rem 0; }
    .stat-card { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #ddd; text-align: center; }
    .stat-value { font-size: 2rem; font-weight: bold; color: #007bff; }
    .stat-label { color: #666; font-size: 0.875rem; margin-top: 0.5rem; }
    .candidates-section { margin-top: 2rem; }
    .candidates-table { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; background: white; border: 1px solid #ddd; }
    thead { background: #f8f9fa; }
    th, td { padding: 1rem; text-align: left; border-bottom: 1px solid #ddd; }
    th { font-weight: 600; }
    .status { padding: 0.25rem 0.5rem; border-radius: 4px; font-weight: bold; font-size: 0.875rem; }
    .status-Pending { background: #ffc107; }
    .status-Verified { background: #28a745; color: white; }
    .status-Revoked { background: #6c757d; color: white; }
    .loading, .error, .empty { padding: 2rem; text-align: center; }
    .error { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; border-radius: 4px; }
  `]
})
export class GuardianDashboard implements OnInit {
  private guardianApi = inject(GuardianApiService);
  private authSession = inject(AuthSessionService);

  links: GuardianLinkDto[] = [];
  loading = true;
  error = '';
  guardianId = '';

  get verifiedCount(): number {
    return this.links.filter(l => l.status === 'Verified').length;
  }

  ngOnInit() {
    const session = this.authSession.session();
    if (!session?.sub) {
      this.error = 'Not authenticated';
      return;
    }
    this.guardianId = session.sub;
    this.loadLinks();
  }

  loadLinks() {
    this.loading = true;
    this.error = '';
    this.guardianApi.getGuardianLinks(this.guardianId).subscribe({
      next: (data) => {
        this.links = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load linked candidates';
        this.loading = false;
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
