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
    <div class="page page--wide">
      <h1>Batch Roster</h1>

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

      <div>
        <h3>Members ({{ members.length }})</h3>
        @if (membersLoading) {
          <div class="empty-state">Loading members...</div>
        }
        @if (!membersLoading && members.length === 0) {
          <div class="empty-state">
            No members added yet
          </div>
        }
        @if (!membersLoading && members.length > 0) {
          <div class="table-wrap">
            <table class="table">
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
