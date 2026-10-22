import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ToastService } from '../toast/toast.service';
import { Tooltip } from './tooltip';

@Component({
  imports: [Tooltip],
  template: `
    <button type="button" id="one" [appTooltip]="text()" aria-label="Edit">x</button>
    <button type="button" id="two" appTooltip="Delete" aria-label="Delete">y</button>
    <button type="button" id="three" appTooltip="Edit: the exam has started" aria-label="Edit" aria-disabled="true">z</button>
  `,
})
class Host {
  readonly text = signal('Edit');
}

describe('Tooltip', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    // jsdom has no layout, so it reports a viewport 0 pixels high; give it a screen to scroll the icon on and off.
    Object.defineProperty(document.documentElement, 'clientHeight', { configurable: true, value: 800 });
  });
  afterEach(() => {
    vi.useRealTimers();
    delete (document.documentElement as { clientHeight?: number }).clientHeight;
    document.querySelectorAll('.app-tooltip').forEach((bubble) => bubble.remove());
  });

  function render() {
    TestBed.configureTestingModule({ imports: [Host] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      one: root.querySelector<HTMLElement>('#one')!,
      two: root.querySelector<HTMLElement>('#two')!,
      three: root.querySelector<HTMLElement>('#three')!,
    };
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

  it('stays and follows its icon when the page scrolls, as it does when focusing an off-screen icon makes the browser scroll', () => {
    const { one } = render();
    const rect = (top: number) => ({ top, bottom: top + 36, left: 100, right: 136, width: 36, height: 36, x: 100, y: top, toJSON: () => ({}) }) as DOMRect;
    const place = vi.spyOn(one, 'getBoundingClientRect').mockReturnValue(rect(400));
    one.dispatchEvent(new Event('focus'));
    const before = (bubbles()[0] as HTMLElement).style.top;

    place.mockReturnValue(rect(300));
    document.dispatchEvent(new Event('scroll'));

    expect(bubbles()).toHaveLength(1);
    expect((bubbles()[0] as HTMLElement).style.top).not.toBe(before);
  });

  it('goes once its icon has scrolled out of view', () => {
    const { one } = render();
    const rect = (top: number) => ({ top, bottom: top + 36, left: 100, right: 136, width: 36, height: 36, x: 100, y: top, toJSON: () => ({}) }) as DOMRect;
    const place = vi.spyOn(one, 'getBoundingClientRect').mockReturnValue(rect(400));
    one.dispatchEvent(new Event('focus'));

    place.mockReturnValue(rect(-200));
    document.dispatchEvent(new Event('scroll'));

    expect(bubbles()).toHaveLength(0);
    // And a scroll with no tooltip showing does nothing.
    document.dispatchEvent(new Event('scroll'));
    expect(bubbles()).toHaveLength(0);
  });

  it('goes inside the dialog its icon is in, since a modal dialog covers everything outside it', () => {
    const { one } = render();
    const dialog = document.createElement('dialog');
    one.parentElement!.appendChild(dialog);
    dialog.appendChild(one);

    one.dispatchEvent(new Event('focus'));

    expect(bubbles()[0]!.parentElement).toBe(dialog);
    one.dispatchEvent(new Event('blur'));
    expect(bubbles()).toHaveLength(0);
  });

  it('goes in the page’s body when its icon is not in a dialog', () => {
    const { one } = render();

    one.dispatchEvent(new Event('focus'));

    expect(bubbles()[0]!.parentElement).toBe(document.body);
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

  describe('on a touch screen, where there is no hover', () => {
    const touch = (matches: boolean) =>
      vi.stubGlobal('matchMedia', (query: string) => ({ matches: matches && query.includes('pointer: coarse'), media: query }));
    afterEach(() => vi.unstubAllGlobals());

    it('says why a control is unavailable when it is tapped, as a message, since the tip cannot be hovered', () => {
      touch(true);
      const { three } = render();

      three.click();

      expect(TestBed.inject(ToastService).toasts().map((t) => [t.kind, t.message])).toEqual([['info', 'Edit: the exam has started']]);
    });

    it('says nothing for a tap on a control that works, which simply does what it says', () => {
      touch(true);
      const { one } = render();

      one.click();

      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
    });

    it('leaves the reason to the hover tip when there is a mouse', () => {
      touch(false);
      const { three } = render();

      three.click();

      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
    });

    it('does not fail where the browser cannot tell (no matchMedia)', () => {
      const { three } = render();

      expect(() => three.click()).not.toThrow();
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
    });
  });
});
