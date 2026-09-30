import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { BatchApiService } from '../batch-api.service';
import { BatchMemberDto } from '../batch.models';

@Component({
  selector: 'app-batch-roster',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="roster-container">
      <h2>Batch Roster</h2>

      <div class="add-member-section">
        <h3>Add Member</h3>
        <form [formGroup]="form" (ngSubmit)="onAddMember()" class="add-member-form">
          <div class="form-group">
            <label for="email">Email *</label>
            <input
              type="email"
              id="email"
              formControlName="email"
              placeholder="member@example.com"
              class="form-control"
            />
          </div>

          <div class="form-group">
            <label for="phone">Phone</label>
            <input
              type="tel"
              id="phone"
              formControlName="phone"
              placeholder="(Optional)"
              class="form-control"
            />
          </div>

          <button type="submit" [disabled]="!form.valid || loading" class="btn btn-primary">
            {{ loading ? 'Adding...' : 'Add Member' }}
          </button>
        </form>
        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
      </div>

      <div class="members-section">
        <h3>Members ({{ members.length }})</h3>
        @if (membersLoading) {
          <div class="loading">Loading members...</div>
        }
        @if (!membersLoading && members.length === 0) {
          <div class="empty">
            No members added yet
          </div>
        }
        @if (!membersLoading && members.length > 0) {
          <div class="members-table">
            <table>
              <thead>
                <tr>
                  <th>Email</th>
                  <th>Phone</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                @for (member of members; track member.id) {
                <tr>
                  <td>{{ member.email }}</td>
                  <td>{{ member.phone || '-' }}</td>
                  <td><span class="status" [class]="'status-' + member.status">{{ member.status }}</span></td>
                </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="actions">
        <a routerLink="/batches" class="btn btn-secondary">Back to Batches</a>
      </div>
    </div>
  `,
  styles: [`
    .roster-container { padding: 2rem; max-width: 800px; margin: 0 auto; }
    .add-member-section { background: #f8f9fa; padding: 2rem; border-radius: 8px; margin-bottom: 2rem; }
    .add-member-form { display: grid; grid-template-columns: 1fr 1fr auto; gap: 1rem; align-items: flex-end; }
    .form-group { }
    .form-group label { display: block; margin-bottom: 0.25rem; font-weight: 500; font-size: 0.875rem; }
    .form-control { width: 100%; padding: 0.5rem; border: 1px solid #ddd; border-radius: 4px; font-size: 0.875rem; }
    .form-control:focus { outline: none; border-color: #007bff; box-shadow: 0 0 0 3px rgba(0, 123, 255, 0.25); }
    .btn { padding: 0.5rem 1rem; border: none; border-radius: 4px; cursor: pointer; text-decoration: none; display: inline-block; }
    .btn-primary { background: #007bff; color: white; }
    .btn-primary:disabled { background: #6c757d; cursor: not-allowed; }
    .btn-secondary { background: #6c757d; color: white; }
    .error-message { color: #dc3545; background: #f8d7da; padding: 0.75rem; border-radius: 4px; border: 1px solid #f5c6cb; margin-top: 1rem; font-size: 0.875rem; }
    .members-section { margin-bottom: 2rem; }
    .members-table { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; background: white; border: 1px solid #ddd; }
    thead { background: #f8f9fa; }
    th, td { padding: 1rem; text-align: left; border-bottom: 1px solid #ddd; }
    th { font-weight: 600; font-size: 0.875rem; }
    .status { padding: 0.25rem 0.5rem; border-radius: 4px; font-weight: bold; font-size: 0.75rem; }
    .status-Pending { background: #ffc107; }
    .status-Registered { background: #28a745; color: white; }
    .status-Verified { background: #007bff; color: white; }
    .loading, .empty { padding: 1rem; text-align: center; color: #666; }
    .actions { display: flex; gap: 1rem; }
  `]
})
export class BatchRoster implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  batchId = '';
  form!: FormGroup;
  members: BatchMemberDto[] = [];
  loading = false;
  membersLoading = true;
  error = '';

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

  loadMembers() {
    this.membersLoading = true;
    this.batchApi.getBatchMembers(this.batchId).subscribe({
      next: (data) => {
        this.members = data;
        this.membersLoading = false;
      },
      error: (err) => {
        console.error(err);
        this.membersLoading = false;
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
