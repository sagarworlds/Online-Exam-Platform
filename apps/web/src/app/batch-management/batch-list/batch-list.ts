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
    <div class="page page--wide">
      <h1>Batches</h1>

      <div class="actions">
        <a routerLink="/batches/create" class="btn btn--primary">Create New Batch</a>
      </div>

      @if (loading) {
        <div class="empty-state">Loading batches...</div>
      }
      @if (error) {
        <div class="error-message">{{ error }}</div>
      }

      @if (!loading && batches.length === 0) {
        <div class="empty-state">
          No batches found. <a routerLink="/batches/create">Create one now</a>
        </div>
      }

      @if (!loading && batches.length > 0) {
        <div class="table-wrap">
          <table class="table">
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
              <td><span class="badge" [class]="'status-' + batch.status">{{ batch.status }}</span></td>
              <td>{{ batch.activeMemberCount }} / {{ batch.maxMembers }}</td>
              <td>{{ batch.createdAt | date: 'short' }}</td>
              <td>
                <a [routerLink]="['/batches', batch.id, 'roster']" class="btn">Manage</a>
                <button class="btn" (click)="viewBatch(batch.id)">View</button>
              </td>
            </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `
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
