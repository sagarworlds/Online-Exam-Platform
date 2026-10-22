import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ListChip, ListToolbar } from './list-toolbar';
import { LoadMore } from './load-more';

@Component({
  imports: [ListToolbar, LoadMore],
  template: `
    <app-list-toolbar
      [search]="search()"
      searchLabel="Search things"
      [liveSearch]="live()"
      [filterable]="filterable()"
      [chips]="chips()"
      [count]="count()"
      (searchChange)="searches.push($event)"
      (chipRemoved)="removed.push($event)"
      (cleared)="clearedCount = clearedCount + 1"
    >
      <select listFilters id="the-filter"><option>x</option></select>
      <a listActions href="#new">New</a>
    </app-list-toolbar>
    <app-load-more [hasMore]="more()" [busy]="busy()" (more)="loaded = loaded + 1" />
  `,
})
class Host {
  readonly search = signal('');
  readonly live = signal(false);
  readonly filterable = signal(true);
  readonly chips = signal<ListChip[]>([]);
  readonly count = signal('');
  readonly more = signal(true);
  readonly busy = signal(false);
  readonly searches: string[] = [];
  readonly removed: string[] = [];
  clearedCount = 0;
  loaded = 0;
}

describe('ListToolbar', () => {
  function open() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      host: fixture.componentInstance,
      root,
      box: () => root.querySelector<HTMLInputElement>('input[type="search"]')!,
      filtersButton: () => root.querySelector<HTMLButtonElement>('.list-toolbar__filters')!,
      panel: () => root.querySelector<HTMLElement>('.list-toolbar__panel')!,
    };
  }

  it('names the search box for a screen reader and keeps the page’s own actions beside it', () => {
    const { root, box } = open();

    expect(root.querySelector('label')?.textContent).toBe('Search things');
    expect(root.querySelector('label')?.getAttribute('for')).toBe(box().id);
    expect(root.querySelector('a[listActions]')?.textContent).toBe('New');
  });

  describe('the search', () => {
    it('is sent on Enter, trimmed, and not again for the text already searched', () => {
      const { fixture, host, box } = open();

      box().value = '  asha  ';
      box().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      box().dispatchEvent(new Event('change'));
      expect(host.searches).toEqual(['asha']);

      host.search.set('asha');
      fixture.detectChanges();
      box().value = 'asha ';
      box().dispatchEvent(new Event('change'));
      expect(host.searches).toEqual(['asha']);
    });

    it('is not sent on every key unless the page asked for a live search', () => {
      const { fixture, host, box } = open();

      box().value = 'a';
      box().dispatchEvent(new Event('input'));
      expect(host.searches).toEqual([]);

      host.live.set(true);
      fixture.detectChanges();
      box().value = 'as';
      box().dispatchEvent(new Event('input'));
      expect(host.searches).toEqual(['as']);
    });
  });

  describe('the filters', () => {
    it('are folded away until the Filters icon is pressed, which says whether they are open', () => {
      const { fixture, filtersButton, panel } = open();

      expect(panel().hidden).toBe(true);
      expect(filtersButton().getAttribute('aria-expanded')).toBe('false');
      expect(filtersButton().getAttribute('aria-controls')).toBe(panel().id);

      filtersButton().click();
      fixture.detectChanges();

      expect(panel().hidden).toBe(false);
      expect(filtersButton().getAttribute('aria-expanded')).toBe('true');
      expect(panel().querySelector('#the-filter')).not.toBeNull();
    });

    it('have no icon and no panel on a list that has none', () => {
      const { fixture, host, root } = open();

      host.filterable.set(false);
      fixture.detectChanges();

      expect(root.querySelector('.list-toolbar__filters')).toBeNull();
      expect(root.querySelector('.list-toolbar__panel')).toBeNull();
    });

    it('show as chips, with their number on the icon, each taken off by its own cross', () => {
      const { fixture, host, root, filtersButton } = open();

      host.chips.set([{ key: 'status', label: 'Status: Draft' }]);
      fixture.detectChanges();

      expect(root.querySelector('.list-chip')?.textContent).toContain('Status: Draft');
      expect(filtersButton().textContent).toContain('1');
      expect(root.textContent).not.toContain('Clear all');

      root.querySelector<HTMLButtonElement>('.list-chip button')!.click();
      expect(host.removed).toEqual(['status']);
    });

    it('leave focus on the Filters icon when a chip goes, so a keyboard user keeps their place', () => {
      const { fixture, host, root, filtersButton } = open();
      host.chips.set([{ key: 'status', label: 'Status: Draft' }]);
      fixture.detectChanges();

      root.querySelector<HTMLButtonElement>('.list-chip button')!.focus();
      root.querySelector<HTMLButtonElement>('.list-chip button')!.click();

      expect(document.activeElement).toBe(filtersButton());
    });

    it('offer Clear all only when there is more than one', () => {
      const { fixture, host, root } = open();

      host.chips.set([
        { key: 'status', label: 'Status: Draft' },
        { key: 'language', label: 'Language: Hindi' },
      ]);
      fixture.detectChanges();
      const clear = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Clear all')!;
      clear.click();

      expect(host.clearedCount).toBe(1);
    });
  });

  it('says how much is listed, politely, and nothing when there is nothing to say', () => {
    const { fixture, host, root } = open();
    expect(root.querySelector('.list-toolbar__count')).toBeNull();

    host.count.set('Showing 3 of 9');
    fixture.detectChanges();

    expect(root.querySelector('.list-toolbar__count')?.textContent).toBe('Showing 3 of 9');
    expect(root.querySelector('.list-toolbar__count')?.getAttribute('aria-live')).toBe('polite');
  });

  describe('Load more', () => {
    it('is there while there is more, asks for the next page, and says it is busy meanwhile', () => {
      const { fixture, host, root } = open();
      const button = () => root.querySelector<HTMLButtonElement>('app-load-more button')!;

      expect(button().textContent?.trim()).toBe('Load more');
      button().click();
      expect(host.loaded).toBe(1);

      host.busy.set(true);
      fixture.detectChanges();
      expect(button().disabled).toBe(true);
      expect(button().getAttribute('aria-busy')).toBe('true');
      expect(button().textContent?.trim()).toBe('Loading…');
    });

    it('is gone once everything is shown', () => {
      const { fixture, host, root } = open();

      host.more.set(false);
      fixture.detectChanges();

      expect(root.querySelector('app-load-more button')).toBeNull();
    });
  });
});
