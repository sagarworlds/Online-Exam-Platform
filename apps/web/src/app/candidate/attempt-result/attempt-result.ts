import { DOCUMENT, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptResultDto } from '../candidate.models';

/**
 * What the result page shows at any moment: the result is being read, it is here, it is held until the exam's author releases it, it was
 * invalidated and so has no certificate, or it could not be read and the candidate may try again.
 */
export type ResultView = 'loading' | 'ready' | 'locked' | 'invalidated' | 'error';

/**
 * Where a candidate reads their result (FR-32): the score, where it stands among the exam's released results (rank and percentile), and the
 * marks by section. The API answers only once the exam's author has released the results. Until then this page says the result is held, and
 * when it opens if that is already decided. The PDF certificate (FR-34) is offered here too, enabled when the result is out and with its reason
 * when it is not.
 */
@Component({
  selector: 'app-attempt-result',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './attempt-result.html',
  styleUrl: './attempt-result.css',
})
export class AttemptResult {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);
  protected readonly attemptId = inject(ActivatedRoute).snapshot.paramMap.get('attemptId');

  protected readonly view = signal<ResultView>('loading');
  protected readonly result = signal<AttemptResultDto | null>(null);
  /** When a held result opens, if the exam's author has already decided it (Scheduled); null when released by hand or not yet known. */
  protected readonly availableFromUtc = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  /** Whether the certificate is being prepared, so its button is disabled while it is. */
  protected readonly certificateBusy = signal(false);
  /** Why the certificate could not be downloaded, in the candidate's language; null when it has not been asked for or succeeded. */
  protected readonly certificateError = signal<string | null>(null);

  constructor() {
    if (this.attemptId === null) {
      this.errorMessage.set(this.i18n.t('result.noAttempt'));
      this.view.set('error');
      return;
    }
    this.load();
  }

  /** Reads the result again, e.g. after a failed read. */
  protected retry(): void {
    this.load();
  }

  /** Asks for the certificate and saves it as a PDF file; a refusal is explained, not shown as a bare failure. */
  protected downloadCertificate(): void {
    const attemptId = this.attemptId;
    if (attemptId === null || this.certificateBusy()) {
      return;
    }

    this.certificateBusy.set(true);
    this.certificateError.set(null);
    this.api.getCertificate(attemptId).subscribe({
      next: (file) => {
        this.certificateBusy.set(false);
        this.saveFile(file, `certificate-${attemptId}.pdf`);
      },
      error: (error: unknown) => {
        this.certificateBusy.set(false);
        void this.explainCertificateRefusal(error);
      },
    });
  }

  private load(): void {
    const attemptId = this.attemptId;
    if (attemptId === null) {
      return;
    }

    this.view.set('loading');
    this.errorMessage.set(null);
    this.api.getAttemptResult(attemptId).subscribe({
      next: (result) => {
        this.result.set(result);
        this.view.set('ready');
      },
      error: (error: unknown) => {
        // A held result is an expected state, not a failure: the page says so and when it opens.
        if (extractProblemCode(error) === 'results_not_released') {
          this.loadReleaseTime(attemptId);
          return;
        }
        // An invalidated result has no score and no certificate, so there is nothing to retry.
        if (extractProblemCode(error) === 'attempt_invalidated') {
          this.view.set('invalidated');
          return;
        }
        // The API's own reason is the most useful thing to show the candidate; the page offers a retry.
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('result.loadError')));
        this.view.set('error');
      },
    });
  }

  /**
   * Reads when a held result opens from the attempt, which the API answers without the release check. The date is a convenience: if that
   * read fails the page still says the result is held, just without the date, and the failure is logged rather than hidden.
   */
  private loadReleaseTime(attemptId: string): void {
    this.api.getAttempt(attemptId).subscribe({
      next: (attempt) => {
        this.availableFromUtc.set(attempt.review?.availableFromUtc ?? null);
        this.view.set('locked');
      },
      error: (error: unknown) => {
        console.error('Reading when the result opens failed', error);
        this.availableFromUtc.set(null);
        this.view.set('locked');
      },
    });
  }

  /**
   * Turns a refused certificate into the candidate's reason. The refusal's body is a problem document sent as a file, so it is read as text
   * first; a body that is not a problem document still gets the general message, rather than none.
   */
  private async explainCertificateRefusal(error: unknown): Promise<void> {
    const code = await this.problemCodeOf(error);
    switch (code) {
      case 'results_not_released':
        this.certificateError.set(this.i18n.t('certificate.reason.held'));
        return;
      case 'attempt_invalidated':
        this.certificateError.set(this.i18n.t('certificate.reason.invalidated'));
        return;
      case 'certificate_name_missing':
        this.certificateError.set(this.i18n.t('certificate.nameMissing'));
        return;
      case 'certificate_text_unsupported':
        this.certificateError.set(this.i18n.t('certificate.unsupported'));
        return;
      default:
        this.certificateError.set(this.i18n.t('certificate.error'));
    }
  }

  /** The API's problem code, read from a refusal whose body arrives as a file (a blob) or as a JSON object. */
  private async problemCodeOf(error: unknown): Promise<string | undefined> {
    if (!(error instanceof HttpErrorResponse)) {
      return undefined;
    }
    if (error.error instanceof Blob) {
      try {
        const problem = JSON.parse(await error.error.text()) as { title?: unknown };
        return typeof problem.title === 'string' ? problem.title : undefined;
      } catch (parseError: unknown) {
        // A body that is not JSON is not a problem document, so there is no code to read; the general message stands in for it.
        console.error('The certificate refusal was not a problem document', parseError);
        return undefined;
      }
    }
    return extractProblemCode(error);
  }

  /** Saves a file the candidate asked for, through a temporary link, so it goes to their downloads. */
  private saveFile(file: Blob, name: string): void {
    const url = URL.createObjectURL(file);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  }

  /** `+4`, `0` or `-1`: the sign is always shown so a gain and a loss cannot be mistaken for each other. */
  protected formatMarks(marks: number): string {
    return marks > 0 ? `+${marks}` : `${marks}`;
  }
}
