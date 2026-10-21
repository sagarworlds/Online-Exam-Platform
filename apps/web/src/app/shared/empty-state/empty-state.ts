import { Component, input } from '@angular/core';

/**
 * What a list says when it has nothing to show: an invitation to begin, not an apology. A title naming the space ("No books yet"), a line
 * saying what it is for, and, when there is something to press, the action (a verb: "Create a book"), put inside it.
 *
 * ```html
 * <app-empty-state title="No books yet" message="Books group questions into chapters.">
 *   <a class="btn btn--primary" routerLink="/admin/books">Create a book</a>
 * </app-empty-state>
 * ```
 *
 * The title is plain text, not a heading: where it sits among the page's headings is the page's to say.
 */
@Component({
  selector: 'app-empty-state',
  template: `
    <div class="card empty-state">
      <p class="empty-state__title">{{ title() }}</p>
      @if (message()) {
        <p class="empty-state__message">{{ message() }}</p>
      }
      <div class="empty-state__action">
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .empty-state__title {
      margin: 0 0 var(--space-1);
      font-size: var(--text-lg);
      font-weight: 600;
    }

    .empty-state__message {
      margin: 0 auto;
      max-width: 32rem;
      color: var(--text-muted);
    }

    .empty-state__action:not(:empty) {
      margin-top: var(--space-4);
    }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly message = input('');
}
