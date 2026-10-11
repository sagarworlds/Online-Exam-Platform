import { Component, inject, signal } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { QuestionApiService } from '../question-api.service';
import { ContentEncryptionStatus } from '../question.models';

/**
 * The admin's view of whether the bank's question content is encrypted at rest (#57). It says how many stored values there are and how many
 * are still plain text, so an administrator can see that the backfill finished before questions are used.
 */
@Component({
  selector: 'app-content-encryption-status',
  imports: [TranslatePipe],
  templateUrl: './content-encryption-status.html',
  styleUrl: './content-encryption-status.css',
})
export class ContentEncryptionStatusPanel {
  private readonly api = inject(QuestionApiService);

  protected readonly status = signal<ContentEncryptionStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    this.load();
  }

  /** Reads the status again, after a failed read. */
  protected reload(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.api.encryptionStatus().subscribe({
      next: (status) => {
        this.status.set(status);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
