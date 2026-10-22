import { ChangeDetectionStrategy, Component, ElementRef, input, linkedSignal, model, output, viewChild } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { Icon } from '../icon/icon';
import { Tooltip } from '../tooltip/tooltip';

/** One filter that is on, as the words its chip shows. `key` says which filter a click on the chip's cross takes off. */
export interface ListChip {
  readonly key: string;
  readonly label: string;
}

let nextId = 0;

/**
 * The strip above an admin list: a search box, a filter button that shows how many filters are on, room for the page's own actions, the
 * filters themselves in a panel that opens under the strip, a chip for each filter that is on, and the count of what is listed. Every list
 * has the same strip, so staff learn it once.
 *
 * The page owns the data. It passes the text searched for and the chips, and acts on `searchChange`, `chipRemoved` and `cleared`; the
 * strip only draws them. The filters go in as content marked `listFilters`, the page's buttons as content marked `listActions`:
 *
 *     <app-list-toolbar [search]="q()" [chips]="chips()" (searchChange)="onSearch($event)" ...>
 *       <div listFilters class="field">...</div>
 *       <a listActions class="btn btn--primary">New</a>
 *     </app-list-toolbar>
 *
 * With `liveSearch` the search is sent on every key (the page waits out the typing); without it only on Enter or when the box is left, which
 * suits a list that is read from the server for each search.
 */
@Component({
  selector: 'app-list-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, Tooltip, TranslatePipe],
  styleUrl: './list-toolbar.css',
  template: `
    <div class="list-toolbar">
      @if (searchable()) {
        <div class="list-toolbar__search" role="search">
          <label class="visually-hidden" [for]="inputId() || searchId">{{ searchLabel() }}</label>
          <input
            [id]="inputId() || searchId"
            type="search"
            autocomplete="off"
            [placeholder]="placeholder() || searchLabel()"
            [value]="search()"
            (input)="onInput($any($event.target).value)"
            (keydown.enter)="commit($any($event.target).value)"
            (change)="commit($any($event.target).value)"
          />
        </div>
      }
      @if (filterable()) {
        <button
          type="button"
          #filtersButton
          class="icon-btn list-toolbar__filters"
          [appTooltip]="'list.filters' | t"
          [attr.aria-label]="'list.filters' | t"
          [attr.aria-controls]="panelId"
          [attr.aria-expanded]="filtersOpen()"
          (click)="filtersOpen.set(!filtersOpen())"
        >
          <app-icon name="filter" />
          @if (chips().length > 0) {
            <span class="icon-btn__count" aria-hidden="true">{{ chips().length }}</span>
          }
        </button>
      }
      <span class="list-toolbar__spacer"></span>
      <ng-content select="[listActions]" />
    </div>

    @if (filterable()) {
      <div [id]="panelId" class="list-toolbar__panel" [hidden]="!filtersOpen()">
        <ng-content select="[listFilters]" />
      </div>
    }

    @if (chips().length > 0) {
      <div class="list-chips" role="group" [attr.aria-label]="'list.activeFilters' | t">
        @for (chip of chips(); track chip.key) {
          <span class="list-chip">
            {{ chip.label }}
            <button
              type="button"
              class="icon-btn"
              [appTooltip]="'list.removeFilter' | t: { filter: chip.label }"
              [attr.aria-label]="'list.removeFilter' | t: { filter: chip.label }"
              (click)="chipRemoved.emit(chip.key); keepFocus()"
            >
              <app-icon name="close" />
            </button>
          </span>
        }
        @if (chips().length > 1) {
          <button type="button" class="btn--small" (click)="cleared.emit(); keepFocus()">{{ 'list.clearAll' | t }}</button>
        }
      </div>
    }

    @if (count()) {
      <p class="list-toolbar__count" aria-live="polite">{{ count() }}</p>
    }
  `,
})
export class ListToolbar {
  /** The text searched for. */
  readonly search = input('');
  /** What the search box is called to a screen reader, and its placeholder unless `placeholder` says otherwise. */
  readonly searchLabel = input('');
  readonly placeholder = input('');
  /** The id of the search box, for a page whose tests or links name it; one is made up when empty. */
  readonly inputId = input('');
  /** False for a list that has no search. */
  readonly searchable = input(true);
  readonly liveSearch = input(false);
  /** True when the list has filters: the button and its panel appear. */
  readonly filterable = input(false);
  readonly chips = input<readonly ListChip[]>([]);
  /** The words under the strip that say how much is listed, such as "12 of 40"; nothing when empty. */
  readonly count = input('');
  /** Whether the filters panel is open; two-way, so a page can open it to show a filter that was set from the address. */
  readonly filtersOpen = model(false);

  /** The search text to look for now (trimmed). */
  readonly searchChange = output<string>();
  readonly chipRemoved = output<string>();
  readonly cleared = output<void>();

  /** The text last searched for: what the page passed in, or what this box last sent before the page has caught up. */
  private readonly known = linkedSignal(() => this.search().trim());

  protected readonly searchId = `list-search-${nextId}`;
  protected readonly panelId = `list-filters-${nextId++}`;

  private readonly filtersButton = viewChild<ElementRef<HTMLButtonElement>>('filtersButton');

  /** The chip that was pressed is about to go; focus moves to the Filters icon, so a keyboard user is not sent back to the top of the page. */
  protected keepFocus(): void {
    this.filtersButton()?.nativeElement.focus();
  }

  protected onInput(value: string): void {
    if (this.liveSearch()) {
      this.known.set(value.trim());
      this.searchChange.emit(value.trim());
    }
  }

  /** Enter and leaving the box both land here, so it says nothing when the text is the one already searched for. */
  protected commit(value: string): void {
    const text = value.trim();
    if (text !== this.known()) {
      this.known.set(text);
      this.searchChange.emit(text);
    }
  }
}
