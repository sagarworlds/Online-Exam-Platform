import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ApiActivityService } from './api-activity.service';

const { SHOW_AFTER_MS, MIN_VISIBLE_MS, SLOW_AFTER_MS } = ApiActivityService;

describe('ApiActivityService', () => {
  let service: ApiActivityService;

  beforeEach(() => {
    vi.useFakeTimers();
    service = TestBed.inject(ApiActivityService);
  });

  afterEach(() => vi.useRealTimers());

  it('shows nothing for a call that finishes quickly', () => {
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS - 1);
    service.end();
    vi.advanceTimersByTime(10_000);

    expect(service.visible()).toBe(false);
    expect(service.slow()).toBe(false);
  });

  it('shows the indicator once a call has taken a little while', () => {
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS);

    expect(service.visible()).toBe(true);
    expect(service.slow()).toBe(false);
  });

  it('keeps an indicator that has appeared on screen for a minimum time, so it does not blink', () => {
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS);
    vi.advanceTimersByTime(50);
    service.end();

    vi.advanceTimersByTime(MIN_VISIBLE_MS - 51);
    expect(service.visible()).toBe(true);

    vi.advanceTimersByTime(1);
    expect(service.visible()).toBe(false);
  });

  it('hides at once when the indicator has already been up for the minimum time', () => {
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS + MIN_VISIBLE_MS);
    service.end();

    vi.advanceTimersByTime(0);
    expect(service.visible()).toBe(false);
  });

  it('adds the explanation only after a long continuous wait, and drops it when the wait ends', () => {
    service.begin();
    vi.advanceTimersByTime(SLOW_AFTER_MS - 1);
    expect(service.slow()).toBe(false);

    vi.advanceTimersByTime(1);
    expect(service.slow()).toBe(true);

    service.end();
    expect(service.slow()).toBe(false);
  });

  it('counts overlapping calls as one wait that ends with the last of them', () => {
    service.begin();
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS);

    service.end();
    vi.advanceTimersByTime(MIN_VISIBLE_MS + 1_000);
    expect(service.visible()).toBe(true);

    service.end();
    vi.advanceTimersByTime(MIN_VISIBLE_MS);
    expect(service.visible()).toBe(false);
  });

  it('keeps the same indicator up when a new call starts before it has gone', () => {
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS);
    service.end();

    vi.advanceTimersByTime(100);
    service.begin();
    vi.advanceTimersByTime(MIN_VISIBLE_MS * 2);

    expect(service.visible()).toBe(true);
  });

  it('ignores an end with nothing in flight', () => {
    service.end();
    service.begin();
    vi.advanceTimersByTime(SHOW_AFTER_MS);

    expect(service.visible()).toBe(true);
  });
});
