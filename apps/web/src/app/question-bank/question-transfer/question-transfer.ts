import { Component, inject, input, output, signal } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { QuestionApiService } from '../question-api.service';
import { ImportQuestionsResult, QuestionFileFormat, QuestionFilter } from '../question.models';

/** The format a file is read as, from its name; null for anything the bank cannot read. */
export function formatOfFile(name: string): QuestionFileFormat | null {
  const extension = name.toLowerCase().split('.').pop();
  return extension === 'csv' || extension === 'json' ? extension : extension === 'xlsx' ? 'xlsx' : null;
}

function read(file: File, as: 'text' | 'buffer'): Promise<string | ArrayBuffer> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string | ArrayBuffer);
    reader.onerror = () => reject(reader.error);
    if (as === 'text') reader.readAsText(file);
    else reader.readAsArrayBuffer(file);
  });
}

function toBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  // In slices, so a large workbook does not overflow the argument limit of fromCharCode.
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(binary);
}

/**
 * Importing questions from, and exporting them to, CSV, Excel and JSON (FR-6). The export is whatever the list below is
 * showing (its filters apply); the import reads a file of any of the three formats and reports which rows it
 * created and which it left out and why, since one bad row never stops the others.
 */
@Component({
  selector: 'app-question-transfer',
  imports: [TranslatePipe],
  templateUrl: './question-transfer.html',
})
export class QuestionTransfer {
  /** The list's current filter, so the export matches what is on screen. */
  readonly filter = input<QuestionFilter>({});
  /** An import created at least one question. */
  readonly imported = output<void>();

  protected readonly formats: readonly { value: QuestionFileFormat; label: string }[] = [
    { value: 'csv', label: 'CSV' },
    { value: 'xlsx', label: 'Excel' },
    { value: 'json', label: 'JSON' },
  ];
  protected readonly exportFormat = signal<QuestionFileFormat>('csv');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly exportNote = signal<string | null>(null);
  protected readonly result = signal<ImportQuestionsResult | null>(null);
  /** Whether a question already in the bank is imported again; off, so importing a file twice does not double the bank. */
  protected readonly allowDuplicates = signal(false);

  private readonly api = inject(QuestionApiService);
  private readonly i18n = inject(I18nService);

  /** What an import did, in a sentence: how many questions it created, and how many rows it left out if any. */
  protected reportText(report: ImportQuestionsResult): string {
    const created = report.created.length;
    return report.rejected.length > 0
      ? this.i18n.plural('qb.transfer.createdLeftOut', created, { left: report.rejected.length })
      : this.i18n.plural('qb.transfer.created', created);
  }

  protected onFormatChanged(value: string): void {
    this.exportFormat.set(value as QuestionFileFormat);
  }

  protected download(): void {
    this.begin();
    const format = this.exportFormat();
    this.api.export(this.filter(), format).subscribe({
      next: (file) => {
        const url = URL.createObjectURL(file.blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = file.fileName;
        link.click();
        URL.revokeObjectURL(url);
        if (file.skipped > 0) {
          this.exportNote.set(this.i18n.plural('qb.transfer.skipped', file.skipped));
        }
        this.busy.set(false);
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  protected async onFilePicked(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Cleared so choosing the same file again still fires a change.
    input.value = '';
    if (file === undefined) {
      return;
    }

    this.begin();
    const format = formatOfFile(file.name);
    if (format === null) {
      this.error.set(this.i18n.t('qb.transfer.badFile'));
      this.busy.set(false);
      return;
    }

    try {
      const content = format === 'xlsx' ? toBase64((await read(file, 'buffer')) as ArrayBuffer) : ((await read(file, 'text')) as string);
      this.api.import(format, content, this.allowDuplicates()).subscribe({
        next: (result) => {
          this.result.set(result);
          this.busy.set(false);
          if (result.created.length > 0) {
            this.imported.emit();
          }
        },
        error: (error: unknown) => this.fail(error),
      });
    } catch {
      this.fail(new Error('The file could not be read.'));
    }
  }

  private begin(): void {
    this.busy.set(true);
    this.error.set(null);
    this.exportNote.set(null);
    this.result.set(null);
  }

  private fail(error: unknown): void {
    this.error.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
    this.busy.set(false);
  }
}
