import { Component, inject, signal } from '@angular/core';
import { I18nService } from '../i18n/i18n.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { extractErrorMessage } from '../shared/problem-details';
import { InstructionTemplateApiService } from './instruction-template-api.service';
import { InstructionTemplateForm } from './instruction-template-form/instruction-template-form';
import { InstructionTemplateDto, InstructionTemplateRequest } from './instruction-template.models';

/** Puts a saved template into a list, in title order, replacing the one it was before. */
function withSaved(list: readonly InstructionTemplateDto[], saved: InstructionTemplateDto): InstructionTemplateDto[] {
  return [...list.filter((item) => item.id !== saved.id), saved].sort((a, b) => a.title.localeCompare(b.title));
}

/**
 * Admin page: the instruction templates staff start an exam's instructions from (FR-41). A template is written once, then copied into
 * exams. The page says that a change or a delete does not reach an exam that already copied the text, since that is the rule staff rely on.
 */
@Component({
  selector: 'app-instruction-templates',
  imports: [TranslatePipe, InstructionTemplateForm],
  templateUrl: './instruction-templates.html',
  styleUrl: './instruction-templates.css',
})
export class InstructionTemplates {
  private readonly api = inject(InstructionTemplateApiService);
  private readonly i18n = inject(I18nService);

  protected readonly templates = signal<InstructionTemplateDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  /** What the last action did, as a short line above the list. */
  protected readonly message = signal<string | null>(null);

  /** The template the form is open for, 'new' for a new one, or null while the form is closed. */
  protected readonly editing = signal<InstructionTemplateDto | 'new' | null>(null);
  protected readonly formBusy = signal(false);
  protected readonly formError = signal<string | null>(null);

  /** The template whose delete waits for a confirmation, and the one being deleted now. */
  protected readonly confirmingId = signal<string | null>(null);
  protected readonly deletingId = signal<string | null>(null);
  protected readonly rowError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  /** Reads the templates. Used on opening the page and by the retry after a failed read. */
  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.list().subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(extractErrorMessage(error, this.i18n.t('templates.loadFailed')));
      },
    });
  }

  /** Opens the empty form for a new template. */
  protected openNew(): void {
    this.message.set(null);
    this.formError.set(null);
    this.confirmingId.set(null);
    this.editing.set('new');
  }

  /** Opens the form filled in with a template, to change it. */
  protected openEdit(template: InstructionTemplateDto): void {
    this.message.set(null);
    this.formError.set(null);
    this.confirmingId.set(null);
    this.editing.set(template);
  }

  /** Closes the form without saving anything. */
  protected closeForm(): void {
    this.formError.set(null);
    this.editing.set(null);
  }

  /** Saves the form: creates a template, or changes the one it was opened for. */
  protected saveForm(request: InstructionTemplateRequest): void {
    const current = this.editing();
    if (current === null || this.formBusy()) {
      return;
    }

    this.formBusy.set(true);
    this.formError.set(null);
    const call = current === 'new' ? this.api.create(request) : this.api.update(current.id, request);
    call.subscribe({
      next: (saved) => {
        this.formBusy.set(false);
        this.editing.set(null);
        this.templates.update((list) => withSaved(list, saved));
        this.message.set(this.i18n.t('templates.saved'));
      },
      error: (error: unknown) => {
        this.formBusy.set(false);
        this.formError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Asks for a confirmation before a template is deleted, so a stray click removes nothing. */
  protected askDelete(template: InstructionTemplateDto): void {
    this.message.set(null);
    this.rowError.set(null);
    this.confirmingId.set(template.id);
  }

  /** Keeps the template: the confirmation is closed without deleting anything. */
  protected keepTemplate(): void {
    this.confirmingId.set(null);
  }

  /** Deletes a template after it was confirmed. Exams that copied its text keep their own copy. */
  protected confirmDelete(template: InstructionTemplateDto): void {
    if (this.deletingId() !== null) {
      return;
    }

    this.deletingId.set(template.id);
    this.rowError.set(null);
    this.api.remove(template.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.confirmingId.set(null);
        this.templates.update((list) => list.filter((item) => item.id !== template.id));
        this.message.set(this.i18n.t('templates.deleted'));
      },
      error: (error: unknown) => {
        this.deletingId.set(null);
        this.rowError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }
}
