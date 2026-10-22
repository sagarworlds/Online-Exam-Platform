import { Component, ElementRef, Injector, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { focusFirstInvalidFieldAfterRender } from '../../shared/focus-first-invalid-field';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
import { extractErrorMessage } from '../../shared/problem-details';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { StatusBadge, StatusTone } from '../../shared/status-badge/status-badge';
import { ToastService } from '../../shared/toast/toast.service';
import { BatchApiService } from '../batch-api.service';
import { BatchMemberDto, MemberRegistrationStatus } from '../batch.models';
import { RosterImport } from './roster-import/roster-import';

/** A member who has not registered yet is waiting on them; one who has is a fact; one whose address is confirmed is good. */
const MEMBER_TONES: Record<MemberRegistrationStatus, StatusTone> = { Pending: 'neutral', Registered: 'info', Verified: 'success', Inactive: 'neutral' };

@Component({
  selector: 'app-batch-roster',
  imports: [ReactiveFormsModule, RouterLink, RosterImport, RefreshButton, StatusBadge, TranslatePipe, FormField, FieldControl],
  template: `
    <div class="page page--admin">
      <div class="page-header">
        <!-- The heading names the batch, so a person who followed a link here, or has two roster tabs open, knows which one this is. -->
        <h1>{{ batchName() ? ('batches.roster.titleNamed' | t: { name: batchName() }) : ('batches.roster.title' | t) }}</h1>
        <div class="page-header__actions">
          <app-refresh-button [busy]="refreshing()" (refresh)="onRefresh()" />
        </div>
      </div>

      <div class="card">
        <h2 class="card__title">{{ 'batches.roster.addTitle' | t }}</h2>
        <form [formGroup]="form" (ngSubmit)="onAddMember()" class="form-row form-row--pair" novalidate>
          <app-form-field [label]="'batches.roster.email' | t" [required]="true" [error]="emailError()">
            <input appFieldControl type="email" formControlName="email" placeholder="member@example.com" autocomplete="off" />
          </app-form-field>

          <app-form-field [label]="'batches.roster.phone' | t" [optionalText]="'common.optional' | t">
            <input appFieldControl type="tel" formControlName="phone" />
          </app-form-field>

          <div class="actions">
            <button type="submit" class="btn btn--primary" [attr.aria-busy]="loading()">
              {{ (loading() ? 'batches.roster.adding' : 'batches.roster.add') | t }}
            </button>
          </div>
        </form>
        @if (error()) {
          <div class="error-message" role="alert">{{ error() }}</div>
        }
      </div>

      <app-roster-import [batchId]="batchId" (imported)="loadMembers()" />

      <section aria-labelledby="members-heading">
        <h2 id="members-heading">{{ membersHeading() }}</h2>
        @if (membersError(); as message) {
          <div class="error-message" role="alert">{{ message }}</div>
        }
        @if (membersLoading()) {
          <p class="empty-state" role="status">{{ 'batches.roster.loading' | t }}</p>
        }
        @if (!membersLoading() && members().length === 0 && !membersError()) {
          <p class="empty-state">{{ 'batches.roster.empty' | t }}</p>
        }
        @if (!membersLoading() && members().length > 0) {
          <app-form-field [label]="'batches.roster.search' | t" [hint]="'batches.roster.searchHint' | t">
            <input appFieldControl type="search" [value]="query()" (input)="onSearch($event)" autocomplete="off" />
          </app-form-field>

          @if (shown().length === 0) {
            <p class="empty-state" role="status">
              {{ 'batches.roster.noMatch' | t }}
              <button type="button" class="btn btn--link" (click)="query.set('')">{{ 'batches.roster.clearSearch' | t }}</button>
            </p>
          } @else {
            <div class="table-wrap" role="region" tabindex="0" [attr.aria-label]="'batches.roster.tableLabel' | t">
              <table class="table">
                <thead>
                  <tr>
                    <th scope="col">{{ 'batches.roster.email' | t }}</th>
                    <th scope="col">{{ 'batches.roster.phone' | t }}</th>
                    <th scope="col">{{ 'batches.col.status' | t }}</th>
                    <th scope="col">{{ 'batches.col.actions' | t }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (member of shown(); track member.id) {
                    <tr>
                      <th scope="row">{{ member.email }}</th>
                      <td>{{ member.phone || '-' }}</td>
                      <td><app-status-badge [tone]="toneOf(member.status)" [label]="statusLabel(member.status)" /></td>
                      <td>
                        <!-- Removing is not undone by this page, so it keeps its word at every width, and asks first. -->
                        <button
                          type="button"
                          class="btn btn--small btn--danger"
                          [attr.aria-label]="'batches.roster.removeNamed' | t: { email: member.email }"
                          [attr.aria-disabled]="removingId() !== null ? 'true' : null"
                          (click)="remove(member)"
                        >
                          {{ 'batches.roster.remove' | t }}
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </section>

      <div class="actions">
        <a routerLink="/batches" class="btn">{{ 'batches.roster.back' | t }}</a>
      </div>
    </div>
  `,
})
export class BatchRoster implements OnInit {
  private fb = inject(FormBuilder);
  private batchApi = inject(BatchApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private i18n = inject(I18nService);
  private toast = inject(ToastService);
  private confirm = inject(ConfirmService);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private injector = inject(Injector);

  batchId = '';
  form!: FormGroup;
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  /** Whether Add member has been pressed, so the address is named as wrong only from then on. */
  private readonly attempted = signal(false);

  // Signals, so that the list repaints when a read lands whatever else is going on in the page.
  protected readonly members = signal<BatchMemberDto[]>([]);
  /** The batch's name, for the heading. Empty until it is read, and when it cannot be, so the heading falls back to a plain one. */
  protected readonly batchName = signal('');
  /** What is typed in the search box. */
  protected readonly query = signal('');
  /** The members the search leaves, by email or phone, in the order the API gave them. */
  protected readonly shown = computed(() => {
    const needle = this.query().trim().toLowerCase();
    if (needle === '') {
      return this.members();
    }
    return this.members().filter((member) => member.email.toLowerCase().includes(needle) || (member.phone ?? '').toLowerCase().includes(needle));
  });
  /** True for the first read only: later reads keep the members on screen until the answer replaces them. */
  protected readonly membersLoading = signal(true);
  /** Whether the members are being read again, after the refresh icon was pressed or after one was added or imported. */
  protected readonly refreshing = signal(false);
  protected readonly membersError = signal<string | null>(null);
  /** The member being removed, so a second press cannot start another while the answer is on its way. */
  protected readonly removingId = signal<string | null>(null);
  /** Where in the list the member just removed was, so focus can go to the row that takes their place once the list is read again. */
  private focusRowAfterLoad: number | null = null;

  protected readonly membersHeading = computed(() => {
    const count = this.members().length;
    const shown = this.shown().length;
    return shown === count
      ? this.i18n.t('batches.roster.members', { count })
      : this.i18n.t('batches.roster.membersFiltered', { shown, count });
  });

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
    // There is no read of one batch, so the name comes from the list. When that fails the heading is the plain one: the roster still works.
    this.batchApi.getBatches().subscribe({
      next: (batches) => this.batchName.set(batches.find((batch) => batch.id === this.batchId)?.name ?? ''),
      error: () => undefined,
    });
  }

  protected emailError(): string | null {
    return this.attempted() && this.form?.get('email')?.invalid ? this.i18n.t('batches.roster.emailError') : null;
  }

  protected onSearch(event: Event) {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected toneOf(status: MemberRegistrationStatus): StatusTone {
    return MEMBER_TONES[status] ?? 'neutral';
  }

  protected statusLabel(status: MemberRegistrationStatus): string {
    return this.i18n.t(`batches.member.status.${status}` as MessageKey);
  }

  /** Reads the members again, so one who registered since shows without reloading the tab. A failed read leaves the list and says why. */
  protected onRefresh() {
    if (!this.refreshing()) {
      this.loadMembers();
    }
  }

  /**
   * Takes a member out of the batch, after asking: the seat is given up and they leave this batch's rankings, though nothing they did at an
   * exam is touched and the address can be added again. The list is read again for what the server says is left.
   */
  protected async remove(member: BatchMemberDto): Promise<void> {
    if (this.removingId() !== null) {
      return;
    }

    const confirmed = await this.confirm.ask({
      title: this.i18n.t('batches.roster.removeTitle', { email: member.email }),
      message: this.i18n.t('batches.roster.removeMessage'),
      consequences: [this.i18n.t('batches.roster.removeLeaves'), this.i18n.t('batches.roster.removeAgain')],
      confirmLabel: this.i18n.t('batches.roster.remove'),
      danger: true,
    });
    if (!confirmed || this.removingId() !== null) {
      return;
    }

    const at = this.shown().findIndex((candidate) => candidate.id === member.id);
    this.removingId.set(member.id);
    this.batchApi.removeMember(this.batchId, member.id).subscribe({
      next: () => {
        this.removingId.set(null);
        this.toast.success(this.i18n.t('batches.roster.removed', { email: member.email }));
        this.focusRowAfterLoad = at >= 0 ? at : 0;
        this.loadMembers();
      },
      error: (err: unknown) => {
        this.removingId.set(null);
        this.toast.error(extractErrorMessage(err, this.i18n.t('batches.roster.removeFailed')));
      },
    });
  }

  /** The row's button is gone with the row, which would leave focus at the top of the page: it goes to the row that took its place, or to the add form. */
  private restoreFocusAfterRemoval(): void {
    const at = this.focusRowAfterLoad;
    if (at === null) {
      return;
    }
    this.focusRowAfterLoad = null;
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        const buttons = root.querySelectorAll<HTMLButtonElement>('tbody button');
        const next = buttons.length > 0 ? buttons[Math.min(at, buttons.length - 1)] : root.querySelector<HTMLInputElement>('input[type="email"]');
        next?.focus();
      },
      { injector: this.injector },
    );
  }

  loadMembers() {
    this.refreshing.set(true);
    this.membersError.set(null);
    this.batchApi.getBatchMembers(this.batchId).subscribe({
      next: (data) => {
        this.members.set(data);
        this.membersLoading.set(false);
        this.refreshing.set(false);
        this.restoreFocusAfterRemoval();
      },
      error: (err) => {
        console.error(err);
        this.membersLoading.set(false);
        this.refreshing.set(false);
        this.membersError.set(extractErrorMessage(err, this.i18n.t('batches.roster.loadFailed')));
      },
    });
  }

  onAddMember() {
    this.attempted.set(true);
    if (!this.form.valid) {
      // Add is never switched off with no reason given: pressing it says what is wrong and goes to the field.
      focusFirstInvalidFieldAfterRender(this.host, this.injector);
      return;
    }
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set('');

    const request = {
      email: this.form.value.email,
      phone: this.form.value.phone,
    };

    this.batchApi.addMember(this.batchId, request).subscribe({
      next: () => {
        this.form.reset();
        this.attempted.set(false);
        this.loading.set(false);
        this.toast.success(this.i18n.t('batches.roster.added'));
        this.loadMembers();
      },
      error: (err) => {
        this.error.set(extractErrorMessage(err, this.i18n.t('batches.roster.addFailed')));
        this.loading.set(false);
        console.error(err);
      },
    });
  }
}
