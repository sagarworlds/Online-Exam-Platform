import { Component, computed, inject, input } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';

/**
 * Grey lines in the shape of what is being read, in place of a bare "Loading…": the page keeps its height while the data comes, so nothing
 * jumps when it arrives. The lines are hidden from a screen reader, which is told once, politely, that the page is loading.
 *
 * `cards` is how many card-shaped blocks to draw, each of `lines` lines. They stand still for a visitor who asked for less motion.
 */
@Component({
  selector: 'app-skeleton',
  template: `
    <div role="status">
      <span class="visually-hidden">{{ loadingText() }}</span>
      @for (card of cardList(); track card) {
        <div class="card skeleton" aria-hidden="true">
          @for (line of lineList(); track line) {
            <span class="skeleton__line" [class.skeleton__line--title]="line === 0" [class.skeleton__line--short]="line === lineList().length - 1 && line > 0"></span>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './skeleton.css',
})
export class Skeleton {
  private readonly i18n = inject(I18nService);

  readonly cards = input(2);
  readonly lines = input(3);
  /** What a screen reader is told; "Loading…" in the page's language unless the page says what is loading. */
  readonly label = input<string | null>(null);

  protected readonly loadingText = computed(() => this.label() ?? this.i18n.t('common.loading'));
  protected readonly cardList = computed(() => Array.from({ length: this.cards() }, (_, i) => i));
  protected readonly lineList = computed(() => Array.from({ length: this.lines() }, (_, i) => i));
}
