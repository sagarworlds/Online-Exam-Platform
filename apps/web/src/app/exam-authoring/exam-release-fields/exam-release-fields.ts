import { Component, input, model } from '@angular/core';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ResultReleaseMode } from '../exam.models';
import { INSTANT_RELEASE, ReleaseSelection } from './exam-release';

let nextId = 0;

/**
 * The "answer review" choice of an exam (FR-12, FR-33): when candidates may see which of their answers were right, with
 * the correct options. It only collects the choice; the page that holds it decides when it is complete, and the API checks
 * it again.
 */
@Component({
  selector: 'app-exam-release-fields',
  imports: [TranslatePipe],
  templateUrl: './exam-release-fields.html',
})
export class ExamReleaseFields {
  readonly disabled = input(false);
  /** The choice, two-way bound so the page always has the latest. */
  readonly selection = model<ReleaseSelection>(INSTANT_RELEASE);

  protected readonly uid = `release-${nextId++}`;
  protected readonly modes: readonly { value: ResultReleaseMode; label: MessageKey; hint: MessageKey }[] = [
    { value: 'Instant', label: 'exams.release.instant', hint: 'exams.release.instantHint' },
    { value: 'Scheduled', label: 'exams.release.scheduled', hint: 'exams.release.scheduledHint' },
    { value: 'Manual', label: 'exams.release.manual', hint: 'exams.release.manualHint' },
  ];

  protected chooseMode(mode: ResultReleaseMode): void {
    this.selection.update((current) => ({ ...current, mode }));
  }

  protected chooseTime(localTime: string): void {
    this.selection.update((current) => ({ ...current, localTime }));
  }
}
