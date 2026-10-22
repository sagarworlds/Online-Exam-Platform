import { Component, computed, input } from '@angular/core';

/** What a status means to the person reading it, which decides its colour and its mark. */
export type StatusTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

const MARKS: Record<StatusTone, string> = { neutral: '', success: '✓', warning: '!', danger: '✕', info: 'i' };

/**
 * A short status as a tag: "Published", "Pending", "Revoked". One look and one set of colours, in place of each list building its own
 * `badge badge--active` and `status-Registered` classes. The tone says what the status means (good, needs attention, bad, or only a fact),
 * and each tone but the plain one carries a mark, so the meaning is in a shape and the word, never in the colour alone.
 *
 * ```html
 * <app-status-badge tone="success" label="Published" />
 * ```
 */
@Component({
  selector: 'app-status-badge',
  template: `<span class="status-badge" [attr.data-tone]="tone()">
    @if (mark()) {
      <span class="status-badge__mark" aria-hidden="true">{{ mark() }}</span>
    }
    {{ label() }}
  </span>`,
  styleUrl: './status-badge.css',
})
export class StatusBadge {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');

  protected readonly mark = computed(() => MARKS[this.tone()]);
}
