import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterModule } from '@angular/router';
import { BatchApiService } from '../batch-api.service';
import { BatchDto } from '../batch.models';

@Component({
  selector: 'app-batch-list',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterModule],
  template: `
    <div class="batch-list-container">
      <h2>Batches</h2>

      <div class="actions">
        <a routerLink="/batches/create" class="btn btn-primary">Create New Batch</a>
      </div>

      @if (loading) {
        <div class="loading">Loading batches...</div>
      }
      @if (error) {
        <div class="error">{{ error }}</div>
      }

      @if (!loading && batches.length === 0) {
        <div class="empty">
          No batches found. <a routerLink="/batches/create">Create one now</a>
        </div>
      }

      @if (!loading && batches.length > 0) {
        <div class="batch-table">
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Status</th>
                <th>Members</th>
                <th>Created</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (batch of batches; track batch.id) {
              <tr>
              <td><strong>{{ batch.name }}</strong></td>
              <td><span class="status" [class]="'status-' + batch.status">{{ batch.status }}</span></td>
              <td>{{ batch.activeMemberCount }} / {{ batch.maxMembers }}</td>
              <td>{{ batch.createdAt | date: 'short' }}</td>
              <td>
                <a [routerLink]="['/batches', batch.id, 'roster']" class="btn btn-secondary">Manage</a>
                <button class="btn btn-tertiary" (click)="viewBatch(batch.id)">View</button>
              </td>
            </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
  styles: [`
    .batch-list-container { padding: 2rem; }
    .actions { margin: 1rem 0; }
    .btn { padding: 0.5rem 1rem; margin-right: 0.5rem; text-decoration: none; display: inline-block; border: none; cursor: pointer; border-radius: 4px; font-size: 0.875rem; }
    .btn-primary { background: #007bff; color: white; }
    .btn-secondary { background: #6c757d; color: white; }
    .btn-tertiary { background: #f0f0f0; color: #333; }
    .batch-table { margin-top: 2rem; overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; background: white; border: 1px solid #ddd; }
    thead { background: #f8f9fa; }
    th, td { padding: 1rem; text-align: left; border-bottom: 1px solid #ddd; }
    th { font-weight: 600; }
    .status { padding: 0.25rem 0.5rem; border-radius: 4px; font-weight: bold; font-size: 0.875rem; }
    .status-Draft { background: #ffc107; }
    .status-Active { background: #28a745; color: white; }
    .status-Closed { background: #6c757d; color: white; }
    .loading, .error, .empty { padding: 2rem; text-align: center; }
    .error { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; border-radius: 4px; }
  `]
})
export class BatchList implements OnInit {
  private batchApi = inject(BatchApiService);

  batches: BatchDto[] = [];
  loading = true;
  error = '';

  ngOnInit() {
    this.loadBatches();
  }

  loadBatches() {
    this.loading = true;
    this.error = '';
    this.batchApi.getBatches().subscribe({
      next: (data) => {
        this.batches = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load batches';
        this.loading = false;
        console.error(err);
      },
    });
  }

  viewBatch(id: string) {
    console.log('View batch:', id);
  }
}
