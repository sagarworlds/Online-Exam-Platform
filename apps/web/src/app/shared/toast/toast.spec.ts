import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ToastHost } from './toast-host';
import { DEFAULT_TOAST_MS, MAX_TOASTS, ToastService } from './toast.service';

describe('ToastService', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const service = () => TestBed.inject(ToastService);

  it('shows a message and puts it away after a few seconds', () => {
    const toasts = service();
    toasts.success('Saved');

    expect(toasts.toasts().map((t) => [t.kind, t.message])).toEqual([['success', 'Saved']]);
    vi.advanceTimersByTime(DEFAULT_TOAST_MS - 1);
    expect(toasts.toasts()).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('keeps an error until it is dismissed, so a person who looked away can still read why it failed', () => {
    const toasts = service();
    const id = toasts.error('Could not save');

    vi.advanceTimersByTime(10 * 60_000);
    expect(toasts.toasts().map((t) => t.message)).toEqual(['Could not save']);

    toasts.dismiss(id);
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('lets a caller choose how long, or keep a message until it is dismissed', () => {
    const toasts = service();
    toasts.info('Short', { durationMs: 1000 });
    toasts.warning('Kept', { durationMs: 0 });

    vi.advanceTimersByTime(1000);

    expect(toasts.toasts().map((t) => t.message)).toEqual(['Kept']);
  });

  it('stays while the pointer or focus is on it, and starts its time again when they leave', () => {
    const toasts = service();
    const id = toasts.success('Saved');

    vi.advanceTimersByTime(DEFAULT_TOAST_MS - 100);
    toasts.hold(id);
    vi.advanceTimersByTime(60_000);
    expect(toasts.toasts()).toHaveLength(1);

    toasts.release(id);
    vi.advanceTimersByTime(DEFAULT_TOAST_MS - 1);
    expect(toasts.toasts()).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('does not stack the same message twice, so "Saved" pressed three times is one "Saved"', () => {
    const toasts = service();
    toasts.success('Saved');
    toasts.success('Saved');
    toasts.success('Saved');

    expect(toasts.toasts()).toHaveLength(1);
  });

  it('keeps at most three on screen, the newest', () => {
    const toasts = service();
    for (let i = 1; i <= MAX_TOASTS + 2; i++) {
      toasts.info(`Message ${i}`);
    }

    expect(toasts.toasts().map((t) => t.message)).toEqual(['Message 3', 'Message 4', 'Message 5']);
  });

  it('clears everything, and ignores an id that is already gone', () => {
    const toasts = service();
    const id = toasts.info('One');
    toasts.error('Two');

    toasts.clear();
    toasts.dismiss(id);
    toasts.hold(id);
    toasts.release(id);

    expect(toasts.toasts()).toHaveLength(0);
  });
});

describe('ToastHost', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const make = () => {
    const fixture = TestBed.createComponent(ToastHost);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, toasts: TestBed.inject(ToastService) };
  };

  it('has both live regions in the page from the start, empty, so what is put into them is announced', () => {
    const { root } = make();

    expect(root.querySelector('[aria-live="polite"]')).not.toBeNull();
    expect(root.querySelector('[aria-live="assertive"]')).not.toBeNull();
    expect(root.querySelectorAll('.toast')).toHaveLength(0);
  });

  it('adds no alert or status role of its own to the page, so a page’s own alert is still the only one', () => {
    const { root } = make();

    expect(root.querySelector('[role="alert"], [role="status"]')).toBeNull();
  });

  it('puts an error in the assertive region and anything else in the polite one', () => {
    const { fixture, root, toasts } = make();

    toasts.success('Saved');
    toasts.error('Could not save');
    fixture.detectChanges();

    expect(root.querySelector('[aria-live="polite"] .toast')?.textContent).toContain('Saved');
    expect(root.querySelector('[aria-live="assertive"] .toast')?.textContent).toContain('Could not save');
    expect(root.querySelector('[aria-live="assertive"] .toast')?.classList.contains('toast--error')).toBe(true);
  });

  it('marks each kind with a shape as well as a colour', () => {
    const { fixture, root, toasts } = make();
    toasts.success('a');
    toasts.warning('b');
    toasts.info('c');
    fixture.detectChanges();

    const marks = Array.from(root.querySelectorAll('.toast__mark')).map((mark) => mark.textContent?.trim());
    expect(marks).toEqual(['✓', '!', 'i']);
    expect(root.querySelector('.toast__mark')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('puts a message away with its close button, which is named for a screen reader', () => {
    const { fixture, root, toasts } = make();
    toasts.success('Saved');
    fixture.detectChanges();
    const close = root.querySelector<HTMLButtonElement>('.toast__close')!;
    expect(close.getAttribute('aria-label')).toBe('Close');

    close.click();
    fixture.detectChanges();

    expect(root.querySelectorAll('.toast')).toHaveLength(0);
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('runs a message’s action and puts the message away', () => {
    const { fixture, root, toasts } = make();
    const undo = vi.fn();
    toasts.info('Question removed', { action: { label: 'Undo', run: undo } });
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('.toast__action')!.click();
    fixture.detectChanges();

    expect(undo).toHaveBeenCalledTimes(1);
    expect(root.querySelectorAll('.toast')).toHaveLength(0);
  });

  it('holds a message while the pointer is on it', () => {
    const { fixture, root, toasts } = make();
    toasts.success('Saved');
    fixture.detectChanges();
    const toast = root.querySelector('.toast')!;

    toast.dispatchEvent(new Event('mouseenter'));
    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();
    expect(root.querySelectorAll('.toast')).toHaveLength(1);

    toast.dispatchEvent(new Event('mouseleave'));
    vi.advanceTimersByTime(DEFAULT_TOAST_MS);
    fixture.detectChanges();
    expect(root.querySelectorAll('.toast')).toHaveLength(0);
  });
});
