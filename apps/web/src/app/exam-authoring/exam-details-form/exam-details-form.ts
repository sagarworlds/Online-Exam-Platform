import { Component, OnInit, input, output, signal } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { UpdateExamDetailsRequest } from '../exam.models';

/** The longest an exam name and description may be; the API refuses more, so the fields stop the typing instead. */
const MAX_NAME_LENGTH = 255;
const MAX_DESCRIPTION_LENGTH = 1000;

/**
 * The name and description of an exam, as a small form (FR-11). It is opened for one edit and starts from what the exam
 * has now; it only collects the new values, and the editor page sends them and closes it, so the form never has to know
 * whether the exam is a draft or already published.
 */
@Component({
  selector: 'app-exam-details-form',
  imports: [TranslatePipe],
  templateUrl: './exam-details-form.html',
})
export class ExamDetailsForm implements OnInit {
  /** The exam's name now. */
  readonly name = input.required<string>();
  /** The exam's description now, if it has one. */
  readonly description = input<string | null>(null);
  /** True while the save is running, so it cannot be sent twice. */
  readonly busy = input(false);

  /** The author pressed Save; the page sends it. */
  readonly saved = output<UpdateExamDetailsRequest>();
  /** The author backed out. */
  readonly cancelled = output<void>();

  protected readonly maxName = MAX_NAME_LENGTH;
  protected readonly maxDescription = MAX_DESCRIPTION_LENGTH;
  protected readonly nameDraft = signal('');
  protected readonly descriptionDraft = signal('');

  ngOnInit(): void {
    this.nameDraft.set(this.name());
    this.descriptionDraft.set(this.description() ?? '');
  }

  protected save(event: Event): void {
    event.preventDefault();
    const name = this.nameDraft().trim();
    if (name === '' || this.busy()) {
      return;
    }

    // A blank description means "none": the API clears it, rather than storing whitespace.
    const description = this.descriptionDraft().trim();
    this.saved.emit({ name, description: description === '' ? null : description });
  }
}
