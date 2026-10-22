import { Component, input, output } from '@angular/core';
import { Icon, IconName } from '../icon/icon';
import { Tooltip } from '../tooltip/tooltip';

let nextId = 0;

/** One thing that can be done to a row. */
export interface RowAction {
  /** What the page is told when it is pressed. */
  id: string;
  /** What it is called, in words: "Edit", "Deactivate". Read aloud, shown beside the icon on a phone and in the tooltip with a mouse. */
  label: string;
  icon: IconName;
  /** Whether it cannot be undone, or is a step toward that: drawn in the danger colour, and never icon-only on a phone. */
  danger?: boolean;
  /** Whether it is unavailable right now. It stays on screen and reachable, so the reason can be given. */
  disabled?: boolean;
  /** Why it is unavailable, said in the tooltip, as a message when tapped on a phone, and under the button where the words are shown. */
  disabledReason?: string;
}

/**
 * The actions of one row of a list, in the form that suits how the screen is used (WCAG 2.5.5, and no hover on a phone):
 * - with a mouse and room: a row of icon buttons with a tooltip, quick to scan and light on the page;
 * - on a phone, or any touch screen: a grid of buttons with an icon **and** the word, because a tooltip cannot be had without hover and
 *   an icon alone is a guess (the trash icon that deactivates a student, say). The page puts this inside the row once it is opened (a
 *   `Disclosure` is the usual row on a phone).
 *
 * Which of the two shows is the stylesheet's choice, by `pointer` and width, so there is no script to get wrong and no layout that depends
 * on a measurement. Only one is in the accessibility tree at a time (the other is `display: none`).
 *
 * Every button is named for what it acts on ("Edit Asha K."), by `rowLabel`, so a list of identical "Edit" buttons is not read out as such.
 * An unavailable action keeps its place, marked `aria-disabled` and not `disabled`, so it can still be focused and its reason read.
 */
@Component({
  selector: 'app-row-actions',
  imports: [Icon, Tooltip],
  templateUrl: './row-actions.html',
  styleUrl: './row-actions.css',
})
export class RowActions {
  readonly actions = input.required<readonly RowAction[]>();

  /** What the row is, for the accessible names: the student's name, the exam's title. */
  readonly rowLabel = input('');

  /** The id of the action that was pressed. Not emitted for an unavailable one. */
  readonly chosen = output<string>();

  private readonly uid = nextId++;

  /** The id of the line that says why an action is unavailable, for `aria-describedby` on its button. */
  protected reasonId(action: RowAction): string {
    return `row-actions-${this.uid}-${action.id}-reason`;
  }

  protected nameOf(action: RowAction): string {
    return this.rowLabel() ? `${action.label} ${this.rowLabel()}` : action.label;
  }

  /** The words of the tooltip: the action's name, and why it cannot be used when it cannot. */
  protected tipOf(action: RowAction): string {
    return action.disabled && action.disabledReason ? `${action.label}: ${action.disabledReason}` : action.label;
  }

  protected choose(action: RowAction): void {
    if (!action.disabled) {
      this.chosen.emit(action.id);
    }
  }
}
