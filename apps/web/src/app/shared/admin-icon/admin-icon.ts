import { Component, input } from '@angular/core';

/** The icon keys an {@link AdminIcon} knows how to draw; see {@link AdminSection.icon}. */
export type AdminIconName = 'question' | 'book' | 'exam' | 'attempt-request' | 'dispute' | 'issue' | 'invite' | 'batch' | 'guardian' | 'otp' | 'whatsapp' | 'brand' | 'template';

/**
 * One of the admin sidebar's icons, drawn as a small inline outline (the same hand-drawn stroke style as the
 * brand mark in the top nav), rather than a letter standing in for the section's name. Each icon is a visual
 * cue only: the link it sits in still carries the full, real label as its accessible name and title.
 */
@Component({
  selector: 'app-admin-icon',
  styleUrl: './admin-icon.css',
  template: `
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      @switch (name()) {
        @case ('question') {
          <circle cx="12" cy="12" r="9" />
          <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .9-1 1.7v.3" />
          <path d="M12 17h.01" />
        }
        @case ('book') {
          <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H19v15H6.5A2.5 2.5 0 0 0 4 20.5V5.5Z" />
          <path d="M19 18H6.5A2.5 2.5 0 0 0 4 20.5" />
        }
        @case ('exam') {
          <path d="M7 3h7l4 4v13a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z" />
          <path d="M14 3v4h4" />
          <path d="m9 14 1.8 1.8L15 11.6" />
        }
        @case ('attempt-request') {
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3.5 2" />
        }
        @case ('dispute') {
          <path d="M4 5h16v11H9.5L4 20.5V5Z" />
          <path d="M12 8.5v3" />
          <path d="M12 13.5h.01" />
        }
        @case ('issue') {
          <path d="M5 21V4" />
          <path d="M5 5h12l-2.5 3.5L17 12H5" />
        }
        @case ('invite') {
          <rect x="3" y="5" width="18" height="14" rx="2" />
          <path d="m3.5 6.5 8.5 7 8.5-7" />
        }
        @case ('batch') {
          <circle cx="9" cy="8" r="3" />
          <path d="M3.5 19a5.5 5.5 0 0 1 11 0" />
          <path d="M16 8a3 3 0 1 1 0 6" />
          <path d="M15 13a5 5 0 0 1 5.5 6" />
        }
        @case ('guardian') {
          <path d="M12 3l7 3v6c0 4.5-3 7.5-7 9-4-1.5-7-4.5-7-9V6l7-3Z" />
          <path d="m9.5 12 1.8 1.8L14.5 10" />
        }
        @case ('otp') {
          <rect x="4" y="11" width="16" height="9" rx="2" />
          <path d="M8 11V8a4 4 0 0 1 8 0v3" />
        }
        @case ('whatsapp') {
          <path d="M5 4h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-7l-4.5 4v-4H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z" />
          <path d="M8 9h8" />
          <path d="M8 12.5h5" />
        }
        @case ('brand') {
          <circle cx="12" cy="12" r="9" />
          <circle cx="8.5" cy="10" r="1.2" />
          <circle cx="12" cy="7.8" r="1.2" />
          <circle cx="15.5" cy="10" r="1.2" />
        }
        @case ('template') {
          <rect x="4" y="4" width="16" height="16" rx="2" />
          <path d="M8 9h8" />
          <path d="M8 13h8" />
          <path d="M8 17h5" />
        }
      }
    </svg>
  `,
})
export class AdminIcon {
  readonly name = input.required<AdminIconName>();
}
