import { Component, inject, input, output, signal } from '@angular/core';
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
      this.fail('Choose a .csv file. A spreadsheet can be saved as CSV from its File menu.');
      return;
    }

    let csv: string;
    try {
      csv = await readText(file);
    } catch {
      this.fail('The file could not be read.');
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
      error: (error: unknown) => this.fail(extractErrorMessage(error)),
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.busy.set(false);
  }
}
