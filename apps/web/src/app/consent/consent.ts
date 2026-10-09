import { Component, inject, signal } from '@angular/core';
import { AuthSessionService } from '../auth/auth-session.service';
import { I18nService } from '../i18n/i18n.service';
import { MessageKey } from '../i18n/messages.en';
import { TranslatePipe } from '../i18n/translate.pipe';
import { extractErrorMessage } from '../shared/problem-details';
import { ConsentApiService } from './consent-api.service';
import { ConsentPurpose, ConsentStatusDto } from './consent.models';

interface ConsentRow {
  purpose: ConsentPurpose;
  status: ConsentStatusDto | null;
  busy: boolean;
  error: string | null;
}

/** The purposes, in the order the page lists them. */
const PURPOSES: readonly ConsentPurpose[] = ['TermsOfService', 'PrivacyNotice', 'ProctoringDataProcessing'];

/**
 * Lets the signed-in user manage their own consent (FR-44). Scoped to acting
 * on one's own behalf only - the backend doesn't verify guardian relationships
 * yet, so a "consent on behalf of a minor" picker would be misleading UI
 * (see the Consent module's Contracts docs).
 *
 * Each purpose has its own card, so the candidate reads what it covers before choosing. Agreeing and withdrawing are
 * separate actions; neither is taken without the candidate pressing its button.
 */
@Component({
  selector: 'app-consent',
  imports: [TranslatePipe],
  templateUrl: './consent.html',
  styleUrl: './consent.css',
})
export class Consent {
  private readonly authSession = inject(AuthSessionService);
  private readonly consentApi = inject(ConsentApiService);
  private readonly i18n = inject(I18nService);
  private readonly subjectId = this.authSession.session()!.userId;

  /** What each purpose is called and what it covers, as message keys so they are translated. */
  protected readonly copy: Readonly<Record<ConsentPurpose, { name: MessageKey; explain: MessageKey }>> = {
    TermsOfService: { name: 'consent.name.TermsOfService', explain: 'consent.explain.TermsOfService' },
    PrivacyNotice: { name: 'consent.name.PrivacyNotice', explain: 'consent.explain.PrivacyNotice' },
    ProctoringDataProcessing: { name: 'consent.name.ProctoringDataProcessing', explain: 'consent.explain.ProctoringDataProcessing' },
  };

  protected readonly rows = signal<ConsentRow[]>(PURPOSES.map((purpose) => ({ purpose, status: null, busy: true, error: null })));

  constructor() {
    for (const purpose of PURPOSES) {
      this.refresh(purpose);
    }
  }

  protected grant(purpose: ConsentPurpose): void {
    const noticeVersionId = this.findRow(purpose).status?.currentNoticeVersionId;
    if (!noticeVersionId) {
      this.updateRow(purpose, { error: this.i18n.t('consent.unavailable') });
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
