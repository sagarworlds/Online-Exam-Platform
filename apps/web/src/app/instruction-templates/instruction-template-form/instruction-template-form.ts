import { Component, ElementRef, OnInit, input, output, signal, viewChild } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import {
  InstructionTemplateDto,
  InstructionTemplateRequest,
  MAX_INSTRUCTIONS_LENGTH,
  MAX_TEMPLATE_TITLE_LENGTH,
} from '../instruction-template.models';

/**
 * The form to create or change an instruction template (FR-41). It refuses a blank title or text on the spot and moves focus to the
 * first field that needs attention. The lengths are checked by the API, which has the last word.
 */
@Component({
  selector: 'app-instruction-template-form',
  imports: [TranslatePipe],
  templateUrl: './instruction-template-form.html',
  styleUrl: './instruction-template-form.css',
})
export class InstructionTemplateForm implements OnInit {
  /** The template being changed, or null for a new one. */
  readonly template = input<InstructionTemplateDto | null>(null);
  /** True while the save is running, so the buttons cannot be pressed twice. */
  readonly busy = input(false);
  /** Why the last save failed, shown on the form. */
  readonly errorMessage = input<string | null>(null);

  /** The author asked to save; the page sends the trimmed title and text. */
  readonly saved = output<InstructionTemplateRequest>();
  /** The author left the form without saving. */
  readonly cancelled = output<void>();

  protected readonly maxTitleLength = MAX_TEMPLATE_TITLE_LENGTH;
  protected readonly maxBodyLength = MAX_INSTRUCTIONS_LENGTH;
  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly titleMissing = signal(false);
  protected readonly bodyMissing = signal(false);

  private readonly titleField = viewChild<ElementRef<HTMLInputElement>>('titleField');
  private readonly bodyField = viewChild<ElementRef<HTMLTextAreaElement>>('bodyField');

  /** Starts the fields from the template being changed, or empty for a new one. */
  ngOnInit(): void {
    this.title.set(this.template()?.title ?? '');
    this.body.set(this.template()?.body ?? '');
  }

  /** Sends the template when both parts are written; otherwise marks the blank one and moves focus to it. */
  protected submit(): void {
    if (this.busy()) {
      return;
    }

    const title = this.title().trim();
    const body = this.body().trim();
    this.titleMissing.set(title === '');
    this.bodyMissing.set(body === '');

    if (title === '') {
      this.titleField()?.nativeElement.focus();
      return;
    }

    if (body === '') {
      this.bodyField()?.nativeElement.focus();
      return;
    }

    this.saved.emit({ title, body });
  }
}
