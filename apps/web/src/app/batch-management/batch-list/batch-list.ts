import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterModule } from '@angular/router';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { BatchApiService } from '../batch-api.service';
import { BatchDto } from '../batch.models';

@Component({
  selector: 'app-batch-list',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterModule, RefreshButton],
  template: `
    <div class="page page--wide">
      <div class="page-header">
        <h1>Batches</h1>
        <div class="page-header__actions">
          <a routerLink="/batches/create" class="btn btn--primary">Create New Batch</a>
          <app-refresh-button [busy]="refreshing()" (refresh)="onRefresh()" />
        </div>
      </div>

      @if (loading()) {
        <div class="empty-state">Loading batches...</div>
      }
      @if (error()) {
        <div class="error-message">{{ error() }}</div>
      }

      @if (!loading() && batches().length === 0 && !error()) {
        <div class="empty-state">
          No batches found. <a routerLink="/batches/create">Create one now</a>
        </div>
      }

      @if (!loading() && batches().length > 0) {
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
              @for (batch of batches(); track batch.id) {
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

  // Signals, so that the list repaints when a read lands whatever else is going on in the page.
  protected readonly batches = signal<BatchDto[]>([]);
  /** True for the first read only: a refresh keeps the batches on screen until the answer replaces them. */
  protected readonly loading = signal(true);
  /** Whether the batches are being read again because the refresh icon was pressed. */
  protected readonly refreshing = signal(false);
  protected readonly error = signal('');

  ngOnInit() {
    this.loadBatches();
  }

  /** Reads the batches again, so a change made elsewhere shows without reloading the tab. A failed read leaves the list and says why. */
  protected onRefresh() {
    if (!this.refreshing()) {
      this.refreshing.set(true);
      this.loadBatches();
    }
  }

  loadBatches() {
    this.error.set('');
    this.batchApi.getBatches().subscribe({
      next: (data) => {
        this.batches.set(data);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load batches');
        this.loading.set(false);
        this.refreshing.set(false);
        console.error(err);
      },
    });
  }

  viewBatch(id: string) {
    console.log('View batch:', id);
  }
}
