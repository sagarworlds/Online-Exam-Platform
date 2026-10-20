import { Component, input } from '@angular/core';

/** The icon keys an {@link Icon} knows how to draw. */
export type IconName =
  | 'edit'
  | 'trash'
  | 'restore'
  | 'language'
  | 'chart'
  | 'folder'
  | 'upload'
  | 'download'
  | 'transfer'
  | 'filter'
  | 'plus'
  | 'close'
  | 'pin'
  | 'search'
  | 'choice'
  | 'text'
  | 'chevron-down'
  | 'chevron-up';

/**
 * An action icon, drawn as a small inline outline in the same stroke style as the admin sidebar's ({@link AdminIcon}), which keeps its own
 * set so the sidebar, which is always loaded, does not carry icons only some pages use. Each icon is a visual cue only: the button or link
 * around it carries the real name as its `aria-label`, and a tooltip says the same words.
 */
@Component({
  selector: 'app-icon',
  styleUrl: './icon.css',
  template: `
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      @switch (name()) {
        @case ('edit') {
          <path d="M4 20h4L19.5 8.5a2.1 2.1 0 0 0-3-3L5 17v3Z" />
          <path d="m14.5 7.5 3 3" />
        }
        @case ('trash') {
          <path d="M4 7h16" />
          <path d="M9 7V4.5h6V7" />
          <path d="M6.5 7l1 13h9l1-13" />
          <path d="M10 11v6" />
          <path d="M14 11v6" />
        }
        @case ('restore') {
          <path d="M4 12a8 8 0 1 0 2.4-5.7" />
          <path d="M4 4v4.5h4.5" />
        }
        @case ('language') {
          <circle cx="12" cy="12" r="9" />
          <path d="M3 12h18" />
          <path d="M12 3c2.5 2.7 3.8 5.7 3.8 9s-1.3 6.3-3.8 9c-2.5-2.7-3.8-5.7-3.8-9S9.5 5.7 12 3Z" />
        }
        @case ('chart') {
          <path d="M5 20v-8" />
          <path d="M12 20V5" />
          <path d="M19 20v-11" />
        }
        @case ('folder') {
          <path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z" />
        }
        @case ('upload') {
          <path d="M12 15V4" />
          <path d="M7.5 8.5 12 4l4.5 4.5" />
          <path d="M5 19h14" />
        }
        @case ('download') {
          <path d="M12 4v11" />
          <path d="M7.5 10.5 12 15l4.5-4.5" />
          <path d="M5 19h14" />
        }
        @case ('transfer') {
          <path d="M4 8h15" />
          <path d="m15.5 4.5 3.5 3.5-3.5 3.5" />
          <path d="M20 16H5" />
          <path d="m8.5 12.5-3.5 3.5 3.5 3.5" />
        }
        @case ('filter') {
          <path d="M4 5h16l-6 7.5V19l-4-2v-4.5L4 5Z" />
        }
        @case ('plus') {
          <path d="M12 5v14" />
          <path d="M5 12h14" />
        }
        @case ('close') {
          <path d="m6 6 12 12" />
          <path d="M18 6 6 18" />
        }
        @case ('pin') {
          <path d="M9 4h6l-1 6 3 3H7l3-3-1-6Z" />
          <path d="M12 13v7" />
        }
        @case ('search') {
          <circle cx="11" cy="11" r="6" />
          <path d="m20 20-4.2-4.2" />
        }
        @case ('choice') {
          <path d="m4 6 1.5 1.5L8 5" />
          <path d="m4 12 1.5 1.5L8 11" />
          <path d="m4 18 1.5 1.5L8 17" />
          <path d="M11 6h9" />
          <path d="M11 12h9" />
          <path d="M11 18h9" />
        }
        @case ('text') {
          <path d="M5 6V4h14v2" />
          <path d="M12 4v16" />
          <path d="M9 20h6" />
        }
        @case ('chevron-down') {
          <path d="m6 9 6 6 6-6" />
        }
        @case ('chevron-up') {
          <path d="m6 15 6-6 6 6" />
        }
      }
    </svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
}
