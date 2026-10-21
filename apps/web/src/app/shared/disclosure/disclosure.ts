import { Component, OnInit, contentChildren, effect, input, model, untracked } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';

/** How the content behind a disclosure stands, so a closed one still says what a person needs to know about it. */
export type DisclosureStatus = 'none' | 'ok' | 'warning' | 'error';

/** The mark in front of a status. It is a shape as well as a colour, and the words beside it (the status label) say the same. */
const STATUS_MARKS: Record<DisclosureStatus, string> = { none: '', ok: '✓', warning: '!', error: '✕' };

const storageKey = (name: string) => `disclosure.${name}`;

/** Whether this block was left open last time, or null when nothing was remembered or the store is blocked. */
function remembered(name: string): boolean | null {
  try {
    const value = sessionStorage.getItem(storageKey(name));
    return value === null ? null : value === 'open';
  } catch {
    return null;
  }
}

function remember(name: string, open: boolean): void {
  try {
    sessionStorage.setItem(storageKey(name), open ? 'open' : 'closed');
  } catch {
    // A blocked store only means the block is closed again next time.
  }
}

/**
 * A block that opens and closes: a row that says what it is and how it stands, and a body that shows when it is opened. It is the browser's
 * own `<details>`, so the keyboard, the screen reader and "find in page" work without anything added, and nothing about it needs a script.
 *
 * What it is built to do, and why:
 * - The row is a tap target at least 44px tall, and carries the block's status in words and a mark, never colour alone, so a closed block
 *   never hides something a person needs to know ("Unsaved", "2 questions missing", "Overdue").
 * - A block whose status becomes `error` opens itself, so a problem is never behind a closed row.
 * - `rememberAs` keeps open or closed for the rest of the visit (session storage, in a try block), so a page read again is as it was left.
 * - `group` makes a set of blocks exclusive: opening one closes the others in the same group (the browser's `name` on `<details>`).
 * - The title is plain text, not a heading: a heading inside a `<summary>` loses its role. A page that wants a heading puts it outside.
 *
 * ```html
 * <app-disclosure heading="Schedule" summary="Opens Oct 24, 10:00" status="warning" statusLabel="Not scheduled" [(open)]="scheduleOpen">
 *   …the form…
 * </app-disclosure>
 * ```
 */
@Component({
  selector: 'app-disclosure',
  templateUrl: './disclosure.html',
  styleUrl: './disclosure.css',
})
export class Disclosure implements OnInit {
  /** What the block is called: the words on its row. */
  readonly heading = input.required<string>();

  /** One line about what is inside, shown beside the title while the block is closed and open alike: "60 min", "3 sections". */
  readonly summary = input('');

  /** How the block stands. `error` opens it. */
  readonly status = input<DisclosureStatus>('none');

  /** The status in words, shown as a tag on the row. Without it the status is only the mark, so give it whenever a status is set. */
  readonly statusLabel = input('');

  /** A name under which open or closed is kept for this visit; null keeps nothing. */
  readonly rememberAs = input<string | null>(null);

  /** The name of a set of blocks of which only one is open at a time; null leaves the block on its own. */
  readonly group = input<string | null>(null);

  /** Whether the block is open. Bind both ways to open it from the page ("Expand all", a link to an error) and to know when it is. */
  readonly open = model(false);

  /** Set once what was remembered has been read, so the first run of the effect below does not write over it. */
  private ready = false;

  constructor() {
    // A block that reports a problem opens itself, whatever else is open.
    effect(() => {
      if (this.status() === 'error') {
        untracked(() => this.open.set(true));
      }
    });

    // Open or closed is kept however it changed: a press on the row, "Expand all", an error opening it.
    effect(() => {
      const open = this.open();
      const name = this.rememberAs();
      if (this.ready && name !== null) {
        remember(name, open);
      }
    });
  }

  ngOnInit(): void {
    const name = this.rememberAs();
    const saved = name === null ? null : remembered(name);
    if (saved !== null) {
      this.open.set(saved);
    }
    this.ready = true;
  }

  protected mark(): string {
    return STATUS_MARKS[this.status()];
  }

  /** The browser opened or closed the block (a press on its row, or the group closing it): the page's `open` follows. */
  protected onToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.open()) {
      return;
    }
    this.open.set(open);
  }
}

/**
 * A set of disclosures with "Expand all" and "Collapse all" above them: for a page with four or more blocks, where opening each in turn is a
 * chore and a person looking for something wants to see it all. The blocks go inside it as they are.
 */
@Component({
  selector: 'app-disclosure-group',
  imports: [TranslatePipe],
  template: `
    <div class="disclosure-group__tools">
      <button type="button" class="link" (click)="setAll(true)">{{ 'common.expandAll' | t }}</button>
      <button type="button" class="link" (click)="setAll(false)">{{ 'common.collapseAll' | t }}</button>
    </div>
    <ng-content />
  `,
  styles: `
    :host {
      display: block;
    }

    .disclosure-group__tools {
      display: flex;
      justify-content: flex-end;
      gap: 1rem;
      margin-bottom: 0.25rem;
    }
  `,
})
export class DisclosureGroup {
  // Every block inside, however deep: a wrapper around them must not make Expand all do nothing.
  private readonly blocks = contentChildren(Disclosure, { descendants: true });

  protected setAll(open: boolean): void {
    for (const block of this.blocks()) {
      block.open.set(open);
    }
  }
}
