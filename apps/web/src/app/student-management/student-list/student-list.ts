import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EMPTY, Observable, Subject, catchError, debounceTime, distinctUntilChanged, forkJoin, map, merge, of, startWith, switchMap, tap } from 'rxjs';
import { Permission } from '../../auth/admin-sections';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AuthSessionService } from '../../auth/auth-session.service';
import { InviteApiService } from '../../invite-management/invite-api.service';
import { InviteDto, InviteStatus } from '../../invite-management/invite.models';
import { ModalDialog } from '../../shared/modal-dialog/modal-dialog';
import { extractErrorMessage } from '../../shared/problem-details';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { RowAction, RowActions } from '../../shared/row-actions/row-actions';
import { StatusBadge, StatusTone } from '../../shared/status-badge/status-badge';
import { InvitedAddress, InvitedAddressChange } from '../invited-address/invited-address';
import { inviteStatusTone } from '../invite-status';
import { StudentApiService } from '../student-api.service';
import { ListToolbar } from '../../shared/list-shell/list-toolbar';
import { syncQueryParam } from '../../shared/list-shell/query-state';
import { StudentInvitations } from '../student-invitations/student-invitations';
import { StudentProfile } from '../student-profile/student-profile';
import { StudentSummaryDto, UpdateStudentResult } from '../student.models';
import { StudentRow, buildStudentRows, invitedAddressesWithoutAccount } from '../student-rows';

/** How long after the last key press the search is sent. */
const SEARCH_DELAY_MS = 300;

/** How many students the API lists at most for one search. */
const LISTING_LIMIT = 200;

/**
 * Staff page: the students, each with their invitations and a profile. Staff who may manage students get an edit action that opens the
 * profile in a popup, and a deactivate action that asks first (icons with a tooltip with a mouse, words in the open row on a touch
 * screen); the others read the profile in the row. A student is an account, found
 * by name, email address or mobile number; an address that was invited and has no account yet is listed too, as "Not registered yet",
 * so every invitation is somewhere on the page.
 *
 * The list of accounts is the API's; the invitations are brought together with it here, by the address they were sent to. An invited
 * address the list does not hold is asked about by address, since the list stops at {@link LISTING_LIMIT} students and an address past
 * that point must not be taken for one without an account.
 */
@Component({
  selector: 'app-student-list',
  imports: [RouterLink, ListToolbar, StudentProfile, StudentInvitations, InvitedAddress, ModalDialog, RefreshButton, RowActions, StatusBadge, TranslatePipe],
  templateUrl: './student-list.html',
  styleUrl: './student-list.css',
})
export class StudentList {
  private readonly studentApi = inject(StudentApiService);
  private readonly inviteApi = inject(InviteApiService);
  private readonly i18n = inject(I18nService);
  private readonly authSession = inject(AuthSessionService);
  private readonly injector = inject(Injector);

  /** Whether the staff member may change a student's profile; the API re-checks it on every call. */
  protected readonly canManage = this.authSession.hasPermission(Permission.StudentManage);
  protected readonly canInvite = this.authSession.hasPermission(Permission.InviteManage);

  protected readonly search = signal('');
  protected readonly loading = signal(true);
  /** Whether the list is being read, for the refresh icon to turn: after it was pressed, and after a change that makes the list stale. */
  protected readonly refreshing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** What the last change did to an invited address, said above the list because the row it was on is gone. */
  protected readonly notice = signal<string | null>(null);

  private readonly accounts = signal<readonly StudentSummaryDto[]>([]);
  private readonly holders = signal<readonly StudentSummaryDto[]>([]);
  private readonly invites = signal<readonly InviteDto[]>([]);

  /** Students staff changed since the search last changed, kept on the page: see {@link StudentRowSources.kept}. */
  private readonly kept = signal<readonly StudentSummaryDto[]>([]);

  protected readonly rows = computed(() =>
    buildStudentRows({
      accounts: this.accounts(),
      holdersOfInvitedAddresses: this.holders(),
      invites: this.invites(),
      search: this.search(),
      kept: this.kept(),
    }),
  );

  /** The student whose profile is open in the edit popup, or null when none is. */
  protected readonly editing = signal<{ id: string } | null>(null);

  /** The student the deactivate popup is asking about, or null when it is closed. */
  protected readonly deactivating = signal<{ id: string; name: string } | null>(null);
  protected readonly deactivateBusy = signal(false);
  protected readonly deactivateError = signal<string | null>(null);

  /** The student whose account is being turned back on, so the action cannot be used twice while the answer is on its way. */
  protected readonly reactivatingId = signal<string | null>(null);

  /** What staff can do to each row that has an account, by row key. A row with no account has nothing, and nor has any row for staff who may not manage. */
  protected readonly rowActions = computed(() => {
    const byRow = new Map<string, readonly RowAction[]>();
    if (!this.canManage) {
      return byRow;
    }

    const reactivating = this.reactivatingId();
    for (const row of this.rows()) {
      if (row.account === null) {
        continue;
      }
      byRow.set(row.key, [
        { id: 'edit', label: this.i18n.t('students.action.edit'), icon: 'edit' },
        row.account.status === 'Deactivated'
          ? {
              id: 'reactivate',
              label: this.i18n.t('students.action.reactivate'),
              icon: 'restore',
              disabled: reactivating === row.account.id,
              disabledReason: this.i18n.t('students.action.reactivating'),
            }
          : { id: 'deactivate', label: this.i18n.t('students.action.deactivate'), icon: 'user-off', danger: true },
      ]);
    }
    return byRow;
  });

  /** The rows that are open, by {@link StudentRow.key}. Only the staff member's choices change it. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  /**
   * How much the page shows, above the rows: "3 students · 2 invitations pending". It counts the rows on the page and not everything the
   * API holds, so a search that narrows the list to one student says so.
   */
  protected readonly summary = computed(() => {
    const rows = this.rows();
    const pending = rows.reduce((count, row) => count + (row.invitations?.invites.filter((invite) => invite.status === 'Pending').length ?? 0), 0);
    return `${this.i18n.plural('students.studentCount', rows.length)} · ${this.i18n.plural('students.pendingCount', pending)}`;
  });

  /** Whether the API stopped at its limit, so there may be students the page does not show. */
  protected readonly truncated = computed(() => this.accounts().length >= LISTING_LIMIT);

  private readonly noticeElement = viewChild<ElementRef<HTMLElement>>('noticeElement');
  private readonly typed = new Subject<string>();
  private readonly reload = new Subject<void>();

  constructor() {
    // The search lives in the address (`?q=`), and is taken from it before the first read below.
    syncQueryParam('q', this.search);
    // One stream for every reason to read the list: the first time, a pause in typing, and a change that makes it stale. A newer read
    // replaces one still on its way, so a slow answer to an old search can never overwrite the answer to the current one.
    merge(
      this.typed.pipe(
        debounceTime(SEARCH_DELAY_MS),
        map((text) => text.trim()),
        distinctUntilChanged(),
        // A new search starts the list afresh; only a change made while looking at one is kept in it.
        tap(() => this.kept.set([])),
      ),
      this.reload,
    )
      .pipe(
        startWith(undefined),
        switchMap(() => this.read()),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  /**
   * Reads the students and their invitations again, so a candidate who has registered or accepted an invitation shows without reloading the
   * tab. The rows stay as they are, with the search, the open rows and any open edit, until the answer replaces them.
   */
  protected onRefresh(): void {
    if (!this.refreshing()) {
      this.reload.next();
    }
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    this.typed.next(text);
  }

  protected isOpen(row: StudentRow): boolean {
    return this.expanded().has(row.key);
  }

  protected toggle(row: StudentRow): void {
    this.expanded.update((open) => {
      const next = new Set(open);
      if (!next.delete(row.key)) {
        next.add(row.key);
      }
      return next;
    });
  }

  /** The words for a count of invitations, so the header does not say "1 invitations". */
  protected invitationCount(row: StudentRow): string {
    const count = row.invitations?.invites.length ?? 0;
    return count === 0 ? this.i18n.t('students.noInvitations') : this.i18n.plural('students.invitationCount', count);
  }

  /** An account's status in the staff member's language; one this page has no words for is shown as the API names it. */
  protected accountStatus(status: string): string {
    const key = `students.status.${status}` as MessageKey;
    return this.i18n.has(key) ? this.i18n.t(key) : status;
  }

  protected inviteStatus(status: InviteStatus): string {
    return this.i18n.t(`invites.status.${status}` as MessageKey);
  }

  protected inviteTone(status: InviteStatus): StatusTone {
    return inviteStatusTone(status);
  }

  protected onRowAction(row: StudentRow, action: string): void {
    switch (action) {
      case 'edit':
        this.openEdit(row);
        break;
      case 'deactivate':
        this.openDeactivate(row);
        break;
      case 'reactivate':
        this.reactivate(row);
        break;
    }
  }

  /** The line under a student's name: how to reach them. */
  protected contact(row: StudentRow): string {
    return [row.email, row.phoneNumber].filter((part): part is string => part !== null).join(' · ');
  }

  protected onProfileSaved(result: UpdateStudentResult): void {
    const { id, displayName, email, phoneNumber, status } = result.student;
    this.kept.update((kept) => [...kept.filter((student) => student.id !== id), { id, displayName, email, phoneNumber, status }]);
    this.reload.next();
  }

  protected onInvitationRevoked(): void {
    this.reload.next();
  }

  /** Opens the student's profile in the edit popup. Only a row with an account has one. */
  protected openEdit(row: StudentRow): void {
    if (row.account !== null) {
      this.editing.set({ id: row.account.id });
    }
  }

  protected closeEdit(): void {
    this.editing.set(null);
  }

  /** Asks before deactivating: it signs the student out and they cannot sign in again from this page. */
  protected openDeactivate(row: StudentRow): void {
    if (row.account !== null) {
      this.deactivateError.set(null);
      this.deactivating.set({ id: row.account.id, name: row.title });
    }
  }

  /**
   * Turns a deactivated account back on, without asking: it is the safe direction, and a mistake is undone by deactivating again. The
   * student signs in again with a code sent to their email address or mobile number; nothing is sent to them from here.
   */
  protected reactivate(row: StudentRow): void {
    const account = row.account;
    if (account === null || this.reactivatingId() !== null) {
      return;
    }

    this.reactivatingId.set(account.id);
    this.studentApi.reactivateStudent(account.id).subscribe({
      next: () => {
        this.reactivatingId.set(null);
        this.notice.set(this.i18n.t('students.reactivated', { name: row.title }));
        afterNextRender(() => this.noticeElement()?.nativeElement.focus(), { injector: this.injector });
        this.reload.next();
      },
      error: (error: unknown) => {
        this.reactivatingId.set(null);
        this.notice.set(null);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  protected closeDeactivate(): void {
    this.deactivating.set(null);
    this.deactivateError.set(null);
  }

  protected confirmDeactivate(): void {
    const target = this.deactivating();
    if (target === null || this.deactivateBusy()) {
      return;
    }

    this.deactivateBusy.set(true);
    this.deactivateError.set(null);
    this.studentApi.deactivateStudent(target.id).subscribe({
      next: () => {
        this.deactivateBusy.set(false);
        this.deactivating.set(null);
        // The popup is gone, so the page says what happened, above the list where the eye is.
        this.notice.set(this.i18n.t('students.deactivated', { name: target.name }));
        afterNextRender(() => this.noticeElement()?.nativeElement.focus(), { injector: this.injector });
        this.reload.next();
      },
      error: (error: unknown) => {
        this.deactivateBusy.set(false);
        this.deactivateError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  protected onAddressChanged(row: StudentRow, change: InvitedAddressChange): void {
    // The row was the old address, which no longer has invitations; the page says what happened and reads the list again.
    this.expanded.update((open) => {
      const next = new Set(open);
      next.delete(row.key);
      return next;
    });
    const moved = this.i18n.plural('students.invitationCount', change.invitationsMoved);
    this.notice.set(this.i18n.t('students.moved', { moved, from: change.from, to: change.to }));
    afterNextRender(() => this.noticeElement()?.nativeElement.focus(), { injector: this.injector });
    this.reload.next();
  }

  /** Reads the accounts and the invitations, then asks which invited addresses without a listed account have one. */
  private read(): Observable<void> {
    this.errorMessage.set(null);
    this.refreshing.set(true);
    return forkJoin({
      accounts: this.studentApi.getStudents(this.search()),
      invites: this.canInvite ? this.inviteApi.getInvites() : of<InviteDto[]>([]),
    }).pipe(
      switchMap(({ accounts, invites }) => {
        const unlisted = invitedAddressesWithoutAccount(invites, accounts).slice(0, LISTING_LIMIT);
        const holders$ = unlisted.length === 0 ? of<StudentSummaryDto[]>([]) : this.studentApi.findByEmail(unlisted);
        return holders$.pipe(map((holders) => ({ accounts, invites, holders })));
      }),
      tap(({ accounts, invites, holders }) => {
        this.accounts.set(accounts);
        this.invites.set(invites);
        this.holders.set(holders);
        this.loading.set(false);
        this.refreshing.set(false);
      }),
      map(() => undefined),
      catchError((error: unknown) => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
        return EMPTY;
      }),
    );
  }
}
