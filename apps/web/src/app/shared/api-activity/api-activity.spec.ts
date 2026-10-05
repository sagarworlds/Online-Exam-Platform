import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ApiActivity } from './api-activity';
import { ApiActivityService } from './api-activity.service';

describe('ApiActivity', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function render() {
    const fixture = TestBed.createComponent(ApiActivity);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      bar: root.querySelector('.api-activity__bar') as HTMLElement,
      note: root.querySelector('.api-activity__note') as HTMLElement,
      announcement: root.querySelector('[role="status"]') as HTMLElement,
    };
  }

  it('is invisible and silent when nothing is being waited on', () => {
    const { bar, note, announcement } = render();

    expect(bar.classList).not.toContain('api-activity__bar--on');
    expect(note.classList).not.toContain('api-activity__note--on');
    expect(announcement.textContent?.trim()).toBe('');
  });

  it('shows the bar and announces "Loading" politely after a short wait', () => {
    const { fixture, bar, announcement } = render();

    TestBed.inject(ApiActivityService).begin();
    vi.advanceTimersByTime(ApiActivityService.SHOW_AFTER_MS);
    fixture.detectChanges();

    expect(bar.classList).toContain('api-activity__bar--on');
    expect(announcement.getAttribute('aria-live')).toBe('polite');
    expect(announcement.textContent?.trim()).toBe('Loading');
  });

  it('explains a long wait, in the same words for everyone', () => {
    const { fixture, note, announcement } = render();

    TestBed.inject(ApiActivityService).begin();
    vi.advanceTimersByTime(ApiActivityService.SLOW_AFTER_MS);
    fixture.detectChanges();

    expect(note.classList).toContain('api-activity__note--on');
    expect(note.getAttribute('aria-hidden')).toBe('true');
    expect(announcement.textContent).toContain('Still working on it');
    expect(note.textContent).toContain('Still working on it');
  });

  it('keeps the decorative bar away from assistive technology and never takes focus', () => {
    const { bar } = render();

    expect(bar.getAttribute('aria-hidden')).toBe('true');
    expect(bar.querySelector('button, a, input')).toBeNull();
  });
});
