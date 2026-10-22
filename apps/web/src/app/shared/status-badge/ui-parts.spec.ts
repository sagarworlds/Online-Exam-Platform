import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EmptyState } from '../empty-state/empty-state';
import { Skeleton } from '../skeleton/skeleton';
import { StatusBadge } from './status-badge';

@Component({
  imports: [StatusBadge, EmptyState, Skeleton],
  template: `
    <app-status-badge tone="success" label="Published" />
    <app-status-badge tone="danger" label="Revoked" />
    <app-status-badge label="Draft" />
    <app-empty-state title="No books yet" message="Books group questions into chapters."><button type="button">Create a book</button></app-empty-state>
    <app-empty-state title="Nothing here" />
    <app-skeleton [cards]="3" [lines]="4" />
    <app-skeleton label="Loading the exams…" [cards]="1" />
  `,
})
class Host {}

describe('the small shared parts', () => {
  const make = () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  describe('StatusBadge', () => {
    it('shows the word and, for every tone but the plain one, a mark, so the meaning is not colour alone', () => {
      const badges = Array.from(make().querySelectorAll('.status-badge'));

      expect(badges.map((b) => b.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['✓ Published', '✕ Revoked', 'Draft']);
      expect(badges.map((b) => b.getAttribute('data-tone'))).toEqual(['success', 'danger', 'neutral']);
      expect(badges[0].querySelector('.status-badge__mark')?.getAttribute('aria-hidden')).toBe('true');
      expect(badges[2].querySelector('.status-badge__mark')).toBeNull();
    });
  });

  describe('EmptyState', () => {
    it('names the space, says what it is for, and holds the action', () => {
      const states = Array.from(make().querySelectorAll('app-empty-state'));

      expect(states[0].querySelector('.empty-state__title')?.textContent).toBe('No books yet');
      expect(states[0].querySelector('.empty-state__message')?.textContent).toBe('Books group questions into chapters.');
      expect(states[0].querySelector('.empty-state__action button')?.textContent).toBe('Create a book');
    });

    it('leaves out the line and the action when there are none, and uses plain text rather than a heading', () => {
      const state = Array.from(make().querySelectorAll('app-empty-state'))[1];

      expect(state.querySelector('.empty-state__message')).toBeNull();
      expect(state.querySelector('h1, h2, h3, h4')).toBeNull();
    });
  });

  describe('Skeleton', () => {
    it('draws as many cards and lines as asked for, hidden from a screen reader', () => {
      const skeletons = Array.from(make().querySelectorAll('app-skeleton'));

      expect(skeletons[0].querySelectorAll('.skeleton')).toHaveLength(3);
      expect(skeletons[0].querySelectorAll('.skeleton')[0].querySelectorAll('.skeleton__line')).toHaveLength(4);
      expect(skeletons[0].querySelector('.skeleton')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('says once that the page is loading, in the page’s language unless it is told what is loading', () => {
      const skeletons = Array.from(make().querySelectorAll('app-skeleton'));

      expect(skeletons[0].querySelector('[role="status"] .visually-hidden')?.textContent).toBe('Loading…');
      expect(skeletons[1].querySelector('[role="status"] .visually-hidden')?.textContent).toBe('Loading the exams…');
    });
  });
});
