import { Component, computed, input, output, signal } from '@angular/core';
import { ContentProtectionRequest } from '../exam.models';

/**
 * Whether the exam page turns off copying, pasting, right-click and printing while candidates sit the exam (FR-23), as a small
 * card. Like the other setting cards it only collects the choice and the editor page sends it. It may change after publishing,
 * because it changes nothing that is asked or scored, only what the page allows.
 */
@Component({
  selector: 'app-exam-content-protection',
  templateUrl: './exam-content-protection.html',
})
export class ExamContentProtection {
  /** Whether the protection is on now. */
  readonly enabled = input.required<boolean>();
  /** Whether the choice can be changed; false only for an archived exam. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);

  /** The author saved a new choice; the page sends it. */
  readonly changed = output<ContentProtectionRequest>();

  /** What the author has ticked but not saved yet; null means "as the exam has it now". */
  private readonly pending = signal<boolean | null>(null);

  protected readonly ticked = computed(() => this.pending() ?? this.enabled());
  protected readonly dirty = computed(() => this.ticked() !== this.enabled());

  protected toggle(ticked: boolean): void {
    this.pending.set(ticked);
  }

  protected save(): void {
    if (this.dirty() && this.editable() && !this.busy()) {
      this.changed.emit({ contentProtection: this.ticked() });
    }
  }

  protected discard(): void {
    this.pending.set(null);
  }
}
