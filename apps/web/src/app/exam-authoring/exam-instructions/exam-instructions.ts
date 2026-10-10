import { Component, computed, input, output, signal } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { InstructionTemplateDto, MAX_INSTRUCTIONS_LENGTH } from '../../instruction-templates/instruction-template.models';

/**
 * The instructions a candidate reads before starting this exam, as a card in the exam editor (FR-41). The author writes them here, or
 * copies a template's text into them. Only a draft can change them, because a candidate acknowledges them before each attempt. The card
 * collects the author's choice; the editor page sends it.
 */
@Component({
  selector: 'app-exam-instructions',
  imports: [TranslatePipe],
  templateUrl: './exam-instructions.html',
  styleUrl: './exam-instructions.css',
})
export class ExamInstructions {
  /** The text the exam has now, or null for none. */
  readonly instructions = input<string | null>(null);
  /** Whether the text can be changed; true only for a draft. */
  readonly editable = input(true);
  /** True while a request about the exam runs, so nothing can be sent twice. */
  readonly busy = input(false);
  /** The templates the author can copy from. */
  readonly templates = input<readonly InstructionTemplateDto[]>([]);
  readonly templatesLoading = input(false);
  readonly templatesError = input<string | null>(null);

  /** The author saved the text; blank means none. The page sends it. */
  readonly changed = output<string>();
  /** The author asked to copy a template into the exam; the page sends the template's id. */
  readonly templateChosen = output<string>();

  protected readonly maxLength = MAX_INSTRUCTIONS_LENGTH;

  /** What the author has typed but not saved; null means the text the exam has now. */
  private readonly pending = signal<string | null>(null);
  /** The template chosen in the list, or an empty string for none. */
  protected readonly pickedTemplate = signal('');

  /** The text the box shows. */
  protected readonly draft = computed(() => this.pending() ?? this.instructions() ?? '');

  /** Whether the typed text differs from what the exam has, so there is something to save. */
  protected readonly dirty = computed(() => {
    const pending = this.pending();
    return pending !== null && pending.trim() !== (this.instructions() ?? '').trim();
  });

  protected edit(text: string): void {
    this.pending.set(text);
  }

  protected save(): void {
    if (this.dirty() && this.editable() && !this.busy()) {
      this.changed.emit(this.draft().trim());
    }
  }

  protected chooseTemplate(templateId: string): void {
    this.pickedTemplate.set(templateId);
  }

  /** Copies the chosen template into the exam. It replaces what the box holds, so anything typed and not saved is set aside with it. */
  protected copyTemplate(): void {
    const templateId = this.pickedTemplate();
    if (templateId === '' || !this.editable() || this.busy()) {
      return;
    }

    this.pending.set(null);
    this.templateChosen.emit(templateId);
  }
}
