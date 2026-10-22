import { Component, inject, input, output, signal } from '@angular/core';
import { I18nService } from '../../../i18n/i18n.service';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { extractErrorMessage } from '../../../shared/problem-details';
import { BatchApiService } from '../../batch-api.service';
import { ImportRosterResult } from '../../batch.models';

/** The most rows one import takes; the API refuses a larger file, and this is the number it says. */
const MAX_ROWS = 1000;

function readText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsText(file);
  });
}

/**
 * Importing a CSV roster into a batch (FR-50). The file is read here and sent as text; the API judges every row under the
 * rules the single add form follows, and answers with a report of who was added and which rows were left out and why, since
 * one bad row never stops the others. The report names rows by number and never repeats an address.
 */
@Component({
  selector: 'app-roster-import',
  imports: [TranslatePipe],
  templateUrl: './roster-import.html',
})
export class RosterImport {
  /** The batch the candidates are added to. */
  readonly batchId = input.required<string>();
  /** An import added at least one candidate, so the member list is out of date. */
  readonly imported = output<void>();

  protected readonly maxRows = MAX_ROWS;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<ImportRosterResult | null>(null);

  private readonly api = inject(BatchApiService);
  private readonly i18n = inject(I18nService);

  /** What an import did, in a sentence: how many candidates it added, and how many rows it left out if any. */
  protected reportText(report: ImportRosterResult): string {
    const added = report.added.length;
    return report.skipped.length > 0
      ? this.i18n.plural('roster.import.addedLeftOut', added, { left: this.i18n.plural('roster.import.rows', report.skipped.length) })
      : this.i18n.plural('roster.import.added', added);
  }

  protected async onFilePicked(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Cleared so choosing the same file again still fires a change.
    input.value = '';
    if (file === undefined) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.result.set(null);

    if (!file.name.toLowerCase().endsWith('.csv')) {
      this.fail(this.i18n.t('roster.import.notCsv'));
      return;
    }

    let csv: string;
    try {
      csv = await readText(file);
    } catch {
      this.fail(this.i18n.t('roster.import.unreadable'));
      return;
    }

    this.api.importRoster(this.batchId(), csv).subscribe({
      next: (report) => {
        this.result.set(report);
        this.busy.set(false);
        if (report.added.length > 0) {
          this.imported.emit();
        }
      },
      error: (error: unknown) => this.fail(extractErrorMessage(error, this.i18n.t('common.somethingWrong'))),
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.busy.set(false);
  }
}
