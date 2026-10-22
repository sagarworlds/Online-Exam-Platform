import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';

/**
 * The foot of a list that is shown in pages: "Load more" while there is more, nothing when all is shown. While the next page is on its way
 * the button says so and keeps its place, so a double click cannot ask twice.
 */
@Component({
  selector: 'app-load-more',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    @if (hasMore()) {
      <p class="load-more">
        <button type="button" [attr.aria-busy]="busy() ? 'true' : null" [disabled]="busy()" (click)="more.emit()">
          {{ busy() ? ('common.loading' | t) : ('list.loadMore' | t) }}
        </button>
      </p>
    }
  `,
  styles: `
    .load-more {
      margin: 0.75rem 0;
      text-align: center;
    }
  `,
})
export class LoadMore {
  readonly hasMore = input(false);
  readonly busy = input(false);
  readonly more = output<void>();
}
