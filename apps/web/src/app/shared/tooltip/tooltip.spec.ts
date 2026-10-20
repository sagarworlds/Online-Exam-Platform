import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Tooltip } from './tooltip';

@Component({
  imports: [Tooltip],
  template: `
    <button type="button" id="one" [appTooltip]="text()" aria-label="Edit">x</button>
    <button type="button" id="two" appTooltip="Delete" aria-label="Delete">y</button>
  `,
})
class Host {
  readonly text = signal('Edit');
}

describe('Tooltip', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    document.querySelectorAll('.app-tooltip').forEach((bubble) => bubble.remove());
  });

  function render() {
    TestBed.configureTestingModule({ imports: [Host] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, one: root.querySelector<HTMLElement>('#one')!, two: root.querySelector<HTMLElement>('#two')! };
  }

  const bubbles = () => Array.from(document.querySelectorAll('.app-tooltip'));

  it('shows its words at once when the icon is focused, so it is not only for a mouse', () => {
    const { one } = render();

    one.dispatchEvent(new Event('focus'));

    expect(bubbles().map((b) => b.textContent)).toEqual(['Edit']);
    expect(bubbles()[0]!.getAttribute('role')).toBe('tooltip');
  });

  it('shows after the pointer has rested on the icon a moment, not as it passes over', () => {
    const { one } = render();

    one.dispatchEvent(new Event('mouseenter'));
    vi.advanceTimersByTime(200);
    expect(bubbles()).toHaveLength(0);

    vi.advanceTimersByTime(200);
    expect(bubbles()).toHaveLength(1);
  });

  it('shows nothing when the pointer leaves before it appears', () => {
    const { one } = render();

    one.dispatchEvent(new Event('mouseenter'));
    one.dispatchEvent(new Event('mouseleave'));
    vi.advanceTimersByTime(1000);

    expect(bubbles()).toHaveLength(0);
  });

  it('goes when focus leaves, when the pointer leaves, and when the icon is clicked', () => {
    const { one } = render();

    one.dispatchEvent(new Event('focus'));
    one.dispatchEvent(new Event('blur'));
    expect(bubbles()).toHaveLength(0);

    one.dispatchEvent(new Event('focus'));
    one.dispatchEvent(new Event('mouseleave'));
    expect(bubbles()).toHaveLength(0);

    one.dispatchEvent(new Event('focus'));
    one.click();
    expect(bubbles()).toHaveLength(0);
  });

  it('is put away by Escape', () => {
    const { one } = render();
    one.dispatchEvent(new Event('focus'));

    one.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(bubbles()).toHaveLength(0);
  });

  it('goes when the page scrolls', () => {
    const { one } = render();
    one.dispatchEvent(new Event('focus'));

    document.dispatchEvent(new Event('scroll'));

    expect(bubbles()).toHaveLength(0);
  });

  it('shows one bubble at a time for one icon, however often it is focused', () => {
    const { one } = render();

    one.dispatchEvent(new Event('focus'));
    one.dispatchEvent(new Event('focus'));

    expect(bubbles()).toHaveLength(1);
  });

  it('shows nothing for empty words', () => {
    const { fixture, one } = render();
    fixture.componentInstance.text.set('  ');
    fixture.detectChanges();

    one.dispatchEvent(new Event('focus'));

    expect(bubbles()).toHaveLength(0);
  });

  it('is removed with the icon, so no bubble is left behind', () => {
    const { fixture, one } = render();
    one.dispatchEvent(new Event('focus'));

    fixture.destroy();

    expect(bubbles()).toHaveLength(0);
  });

  it('is placed on screen, clear of the icon', () => {
    const { one } = render();

    one.dispatchEvent(new Event('focus'));

    const bubble = bubbles()[0] as HTMLElement;
    expect(bubble.style.top).toMatch(/^-?\d+px$/);
    expect(bubble.style.left).toMatch(/^\d+px$/);
  });
});
