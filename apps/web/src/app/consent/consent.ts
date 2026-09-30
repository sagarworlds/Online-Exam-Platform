import { Component, inject, signal } from '@angular/core';
import { AuthSessionService } from '../auth/auth-session.service';
import { extractErrorMessage } from '../shared/problem-details';
import { ConsentApiService } from './consent-api.service';
import { ConsentPurpose, ConsentStatusDto } from './consent.models';

interface ConsentRow {
  purpose: ConsentPurpose;
  label: string;
  status: ConsentStatusDto | null;
  busy: boolean;
  error: string | null;
}

const PURPOSES: readonly { purpose: ConsentPurpose; label: string }[] = [
  { purpose: 'TermsOfService', label: 'Terms of Service' },
  { purpose: 'PrivacyNotice', label: 'Privacy Notice' },
  { purpose: 'ProctoringDataProcessing', label: 'Proctoring Data Processing' },
];

/**
 * Lets the signed-in user manage their own consent (FR-44). Scoped to acting
 * on one's own behalf only - the backend doesn't verify guardian relationships
 * yet, so a "consent on behalf of a minor" picker would be misleading UI
 * (see the Consent module's Contracts docs).
 */
@Component({
  selector: 'app-consent',
  templateUrl: './consent.html',
})
export class Consent {
  private readonly authSession = inject(AuthSessionService);
  private readonly consentApi = inject(ConsentApiService);
  private readonly subjectId = this.authSession.session()!.userId;

  protected readonly rows = signal<ConsentRow[]>(
    PURPOSES.map(({ purpose, label }) => ({ purpose, label, status: null, busy: true, error: null })),
  );

  constructor() {
    for (const { purpose } of PURPOSES) {
      this.refresh(purpose);
    }
  }

  protected grant(purpose: ConsentPurpose): void {
    const noticeVersionId = this.findRow(purpose).status?.currentNoticeVersionId;
    if (!noticeVersionId) {
      this.updateRow(purpose, { error: 'No notice version is available to grant against yet.' });
      return;
    }

    this.updateRow(purpose, { busy: true, error: null });
    this.consentApi.grant({ subjectId: this.subjectId, purpose, noticeVersionId }).subscribe({
      next: () => this.refresh(purpose),
      error: (error: unknown) => this.updateRow(purpose, { busy: false, error: extractErrorMessage(error) }),
    });
  }

  protected withdraw(purpose: ConsentPurpose): void {
    const consentRecordId = this.findRow(purpose).status?.consentRecordId;
    if (!consentRecordId) {
      return;
    }

    this.updateRow(purpose, { busy: true, error: null });
    this.consentApi.withdraw(consentRecordId).subscribe({
      next: () => this.refresh(purpose),
      error: (error: unknown) => this.updateRow(purpose, { busy: false, error: extractErrorMessage(error) }),
    });
  }

  private refresh(purpose: ConsentPurpose): void {
    this.consentApi.getStatus(this.subjectId, purpose).subscribe({
      next: (status) => this.updateRow(purpose, { status, busy: false, error: null }),
      error: (error: unknown) => this.updateRow(purpose, { busy: false, error: extractErrorMessage(error) }),
    });
  }

  private findRow(purpose: ConsentPurpose): ConsentRow {
    return this.rows().find((row) => row.purpose === purpose)!;
  }

  private updateRow(purpose: ConsentPurpose, patch: Partial<ConsentRow>): void {
    this.rows.update((rows) => rows.map((row) => (row.purpose === purpose ? { ...row, ...patch } : row)));
  }
}
