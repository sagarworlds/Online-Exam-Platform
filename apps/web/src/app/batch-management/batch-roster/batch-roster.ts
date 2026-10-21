import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { BatchApiService } from '../batch-api.service';
import { BatchMemberDto } from '../batch.models';
import { RosterImport } from './roster-import/roster-import';

@Component({
  selector: 'app-batch-roster',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, RosterImport, RefreshButton],
  template: `
    <div class="page page--wide">
      <div class="page-header">
        <h1>Batch Roster</h1>
        <div class="page-header__actions">
          <app-refresh-button [busy]="refreshing()" (refresh)="onRefresh()" />
        </div>
      </div>

      <div class="card">
        <h3>Add Member</h3>
        <form [formGroup]="form" (ngSubmit)="onAddMember()" class="form-row">
          <div class="field">
            <label for="email">Email *</label>
            <input
              type="email"
              id="email"
              formControlName="email"
              placeholder="member@example.com"
            />
          </div>

          <div class="field">
            <label for="phone">Phone</label>
            <input
              type="tel"
              id="phone"
              formControlName="phone"
              placeholder="(Optional)"
            />
          </div>

          <button type="submit" [disabled]="!form.valid || loading" class="btn btn--primary">
            {{ loading ? 'Adding...' : 'Add Member' }}
          </button>
        </form>
        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </div>

      <app-roster-import [batchId]="batchId" (imported)="loadMembers()" />

      <div>
        <h3>Members ({{ members().length }})</h3>
        @if (membersError(); as message) {
          <div class="error-message" role="alert">{{ message }}</div>
        }
        @if (membersLoading()) {
          <div class="empty-state">Loading members...</div>
        }
        @if (!membersLoading() && members().length === 0 && !membersError()) {
          <div class="empty-state">
            No members added yet
          </div>
        }
        @if (!membersLoading() && members().length > 0) {
          <div class="table-wrap" role="region" tabindex="0" aria-label="Batch members">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Email</th>
                  <th scope="col">Phone</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                @for (member of members(); track member.id) {
                <tr>
                  <td>{{ member.email }}</td>
                  <td>{{ member.phone || '-' }}</td>
                  <td><span class="badge" [class]="'status-' + member.status">{{ member.status }}</span></td>
                </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="actions">
        <a routerLink="/batches" class="btn">Back to Batches</a>
      </div>
    </div>
  `
})
export class BatchRoster implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  batchId = '';
  form!: FormGroup;
  loading = false;
  error = '';

  // Signals, so that the list repaints when a read lands whatever else is going on in the page.
  protected readonly members = signal<BatchMemberDto[]>([]);
  /** True for the first read only: later reads keep the members on screen until the answer replaces them. */
  protected readonly membersLoading = signal(true);
  /** Whether the members are being read again, after the refresh icon was pressed or after one was added or imported. */
  protected readonly refreshing = signal(false);
  protected readonly membersError = signal<string | null>(null);

  ngOnInit() {
    this.batchId = this.route.snapshot.params['id'];
    if (!this.batchId) {
      this.router.navigate(['/batches']);
      return;
    }

    this.form = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      phone: [''],
    });

    this.loadMembers();
  }

  /** Reads the members again, so one who registered since shows without reloading the tab. A failed read leaves the list and says why. */
  protected onRefresh() {
    if (!this.refreshing()) {
      this.loadMembers();
    }
  }

  loadMembers() {
    this.refreshing.set(true);
    this.membersError.set(null);
    this.batchApi.getBatchMembers(this.batchId).subscribe({
      next: (data) => {
        this.members.set(data);
        this.membersLoading.set(false);
        this.refreshing.set(false);
      },
      error: (err) => {
        console.error(err);
        this.membersLoading.set(false);
        this.refreshing.set(false);
        this.membersError.set(extractErrorMessage(err, 'The members could not be loaded. Please try again.'));
      },
    });
  }

  onAddMember() {
    if (!this.form.valid) return;

    this.loading = true;
    this.error = '';

    const request = {
      email: this.form.value.email,
      phone: this.form.value.phone,
    };

    this.batchApi.addMember(this.batchId, request).subscribe({
      next: () => {
        this.form.reset();
        this.loading = false;
        this.loadMembers();
      },
      error: (err) => {
        this.error = 'Failed to add member';
        this.loading = false;
        console.error(err);
      },
    });
  }
}
