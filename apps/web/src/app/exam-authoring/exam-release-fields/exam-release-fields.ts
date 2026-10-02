import { Component, input, model } from '@angular/core';
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
  templateUrl: './exam-release-fields.html',
})
export class ExamReleaseFields {
  readonly disabled = input(false);
  /** The choice, two-way bound so the page always has the latest. */
  readonly selection = model<ReleaseSelection>(INSTANT_RELEASE);

  protected readonly uid = `release-${nextId++}`;
  protected readonly modes: readonly { value: ResultReleaseMode; label: string; hint: string }[] = [
    { value: 'Instant', label: 'Right after they submit', hint: 'Each candidate sees their result as soon as they finish.' },
    { value: 'Scheduled', label: 'From a set time', hint: 'For example once the whole exam window has closed.' },
    { value: 'Manual', label: 'When I release them', hint: 'Held back until you press "Release answers now" on this page.' },
  ];

  protected chooseMode(mode: ResultReleaseMode): void {
    this.selection.update((current) => ({ ...current, mode }));
  }

  protected chooseTime(localTime: string): void {
    this.selection.update((current) => ({ ...current, localTime }));
  }
}
