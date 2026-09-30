import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterModule } from '@angular/router';
import { InviteApiService } from '../invite-api.service';
import { InviteDto } from '../invite.models';

@Component({
  selector: 'app-invite-list',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterModule],
  template: `
    <div class="invite-list-container">
      <h2>Invitations</h2>

      <div class="actions">
        <a routerLink="/invites/create" class="btn btn-primary">Create New Invite</a>
      </div>

      <div *ngIf="loading" class="loading">Loading invites...</div>
      <div *ngIf="error" class="error">{{ error }}</div>

      <div *ngIf="!loading && invites.length === 0" class="empty">
        No invitations found. <a routerLink="/invites/create">Create one now</a>
      </div>

      <div *ngIf="!loading && invites.length > 0" class="invite-table">
        <table>
          <thead>
            <tr>
              <th>Email</th>
              <th>Status</th>
              <th>Sent</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let invite of invites">
              <td>{{ invite.email }}</td>
              <td><span class="status" [class]="'status-' + invite.status">{{ invite.status }}</span></td>
              <td>{{ invite.sentAt | date: 'short' }}</td>
              <td>
                <button *ngIf="invite.status === 'Sent'" class="btn btn-tertiary" (click)="generateCode(invite.id)">Generate Code</button>
                <button *ngIf="invite.status === 'Sent'" class="btn btn-danger" (click)="revokeInvite(invite.id)">Revoke</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [`
    .invite-list-container { padding: 2rem; }
    .actions { margin: 1rem 0; }
    .btn { padding: 0.5rem 1rem; margin-right: 0.5rem; text-decoration: none; display: inline-block; border: none; cursor: pointer; border-radius: 4px; font-size: 0.875rem; }
    .btn-primary { background: #007bff; color: white; }
    .btn-tertiary { background: #f0f0f0; color: #333; }
    .btn-danger { background: #dc3545; color: white; }
    .invite-table { margin-top: 2rem; overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; background: white; border: 1px solid #ddd; }
    thead { background: #f8f9fa; }
    th, td { padding: 1rem; text-align: left; border-bottom: 1px solid #ddd; }
    th { font-weight: 600; }
    .status { padding: 0.25rem 0.5rem; border-radius: 4px; font-weight: bold; font-size: 0.875rem; }
    .status-Pending { background: #ffc107; }
    .status-Sent { background: #17a2b8; color: white; }
    .status-Accepted { background: #28a745; color: white; }
    .status-Declined { background: #6c757d; color: white; }
    .loading, .error, .empty { padding: 2rem; text-align: center; }
    .error { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; border-radius: 4px; }
  `]
})
export class InviteList implements OnInit {
  private inviteApi = inject(InviteApiService);

  invites: InviteDto[] = [];
  loading = true;
  error = '';

  ngOnInit() {
    this.loadInvites();
  }

  loadInvites() {
    this.loading = true;
    this.error = '';
    this.inviteApi.getInvites().subscribe({
      next: (data) => {
        this.invites = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load invitations';
        this.loading = false;
        console.error(err);
      },
    });
  }

  generateCode(inviteId: string) {
    this.inviteApi.generateCode(inviteId).subscribe({
      next: () => {
        this.loadInvites();
      },
      error: (err) => {
        console.error('Failed to generate code', err);
      },
    });
  }

  revokeInvite(inviteId: string) {
    if (!confirm('Are you sure you want to revoke this invitation?')) return;
    this.inviteApi.revokeInvite(inviteId).subscribe({
      next: () => {
        this.loadInvites();
      },
      error: (err) => {
        console.error('Failed to revoke invitation', err);
      },
    });
  }
}
