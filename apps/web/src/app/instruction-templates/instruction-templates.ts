import { Component, computed, inject, signal } from '@angular/core';
import { I18nService } from '../i18n/i18n.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { extractErrorMessage } from '../shared/problem-details';
import { RefreshButton } from '../shared/refresh-button/refresh-button';
import { RowAction, RowActions } from '../shared/row-actions/row-actions';
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
  imports: [TranslatePipe, InstructionTemplateForm, RefreshButton, RowActions],
  templateUrl: './instruction-templates.html',
  styleUrl: './instruction-templates.css',
})
export class InstructionTemplates {
  private readonly api = inject(InstructionTemplateApiService);
  private readonly i18n = inject(I18nService);

  protected readonly templates = signal<InstructionTemplateDto[]>([]);
  protected readonly loading = signal(true);
  /** Whether the templates are being read, for the refresh icon to turn. */
  protected readonly refreshing = signal(false);
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

  /** Edit and delete of a template; both wait while a form is open, and say why. */
  protected readonly templateActions = computed<readonly RowAction[]>(() => {
    const formOpen = this.editing() !== null;
    const reason = this.i18n.t('templates.finishForm');
    return [
      { id: 'edit', label: this.i18n.t('templates.edit'), icon: 'edit', disabled: formOpen, disabledReason: reason },
      { id: 'delete', label: this.i18n.t('templates.delete'), icon: 'trash', danger: true, disabled: formOpen, disabledReason: reason },
    ];
  });

  constructor() {
    this.load();
  }

  protected onTemplateAction(template: InstructionTemplateDto, id: string): void {
    if (id === 'edit') {
      this.openEdit(template);
    } else if (id === 'delete') {
      this.askDelete(template);
    }
  }

  /** Reads the templates. Used on opening the page and by the retry after a failed read. */
  protected load(): void {
    this.loading.set(true);
    this.read();
  }

  /**
   * Reads the templates again, so one added or changed by a colleague shows without reloading the tab. The list is not blanked, and a form
   * that is open stays open. A failed read leaves the list and says why above it.
   */
  protected onRefresh(): void {
    if (!this.refreshing()) {
      this.read();
    }
  }

  private read(): void {
    this.refreshing.set(true);
    this.loadError.set(null);
    this.rowError.set(null);
    this.api.list().subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (error: unknown) => {
        const message = extractErrorMessage(error, this.i18n.t('templates.loadFailed'));
        // Templates already on screen stay, with the reason above them; with none to keep, the error stands in the list's place, with its retry.
        if (this.loading() || this.templates().length === 0) {
          this.loadError.set(message);
        } else {
          this.rowError.set(message);
        }
        this.loading.set(false);
        this.refreshing.set(false);
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
