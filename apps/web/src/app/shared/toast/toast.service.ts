import { Injectable, signal } from '@angular/core';

/** What a toast says about: a thing that worked, a thing that did not, or something worth knowing. */
export type ToastKind = 'success' | 'error' | 'warning' | 'info';

/** One message on the screen. */
export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
  /** A button on the toast ("Undo"); null for none. Pressing it runs `run` and puts the toast away. */
  action: { label: string; run: () => void } | null;
}

export interface ToastOptions {
  /** How long it stays, in milliseconds, before it goes by itself. 0 keeps it until it is dismissed. The default depends on the kind. */
  durationMs?: number;
  action?: { label: string; run: () => void };
}

/** How long a message that needs no answer stays. Long enough to read a sentence twice, short enough not to pile up. */
export const DEFAULT_TOAST_MS = 6000;

/** At most this many are on screen; a new one makes the oldest go, so a burst of messages cannot cover the page. */
export const MAX_TOASTS = 3;

/**
 * The one place a page says "Saved", "Could not save" or "Link copied" in a message that comes and goes, in place of each page building
 * its own `role="status"` paragraph (there were about fifty). The messages are drawn by `ToastHost`, which the page around the app holds
 * once, so they sit in the same spot on every page and are announced the same way: an error at once, anything else politely.
 *
 * An error does not go by itself: a person who looked away must still be able to read why something failed. Everything else goes after
 * a few seconds, and stays while the pointer or the keyboard focus is on it (WCAG 2.2.1).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly all = signal<Toast[]>([]);
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();
  private readonly durations = new Map<number, number>();
  private nextId = 1;

  /** The messages on screen, oldest first. */
  readonly toasts = this.all.asReadonly();

  success(message: string, options?: ToastOptions): number {
    return this.show('success', message, options);
  }

  error(message: string, options?: ToastOptions): number {
    return this.show('error', message, { durationMs: 0, ...options });
  }

  warning(message: string, options?: ToastOptions): number {
    return this.show('warning', message, options);
  }

  info(message: string, options?: ToastOptions): number {
    return this.show('info', message, options);
  }

  /** Puts a message on screen and returns its id, for `dismiss`. */
  show(kind: ToastKind, message: string, options: ToastOptions = {}): number {
    const id = this.nextId++;
    const toast: Toast = { id, kind, message, action: options.action ?? null };
    this.all.update((current) => [...current, toast]);

    // A message that is already there is not stacked twice: the older copy goes, so "Saved" pressed three times is one "Saved".
    const same = this.all().filter((other) => other.id !== id && other.kind === kind && other.message === message);
    same.forEach((other) => this.dismiss(other.id));
    // And no more than MAX_TOASTS at a time.
    this.all()
      .slice(0, Math.max(0, this.all().length - MAX_TOASTS))
      .forEach((old) => this.dismiss(old.id));

    const duration = options.durationMs ?? DEFAULT_TOAST_MS;
    this.durations.set(id, duration);
    this.startTimer(id);
    return id;
  }

  /** Puts a message away. Nothing happens for an id that is already gone. */
  dismiss(id: number): void {
    this.stopTimer(id);
    this.durations.delete(id);
    this.all.update((current) => current.filter((toast) => toast.id !== id));
  }

  /** Puts every message away. */
  clear(): void {
    [...this.all()].forEach((toast) => this.dismiss(toast.id));
  }

  /** The pointer or the focus is on a message: it stays while they are. */
  hold(id: number): void {
    this.stopTimer(id);
  }

  /** The pointer and the focus have left a message: its time starts again. */
  release(id: number): void {
    if (this.durations.has(id)) {
      this.startTimer(id);
    }
  }

  private startTimer(id: number): void {
    this.stopTimer(id);
    const duration = this.durations.get(id) ?? 0;
    if (duration > 0) {
      this.timers.set(
        id,
        setTimeout(() => this.dismiss(id), duration),
      );
    }
  }

  private stopTimer(id: number): void {
    const timer = this.timers.get(id);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.timers.delete(id);
    }
  }
}
