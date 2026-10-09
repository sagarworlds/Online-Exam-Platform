import { Injectable, computed, signal } from '@angular/core';

/** Where the candidate's own choice is remembered, on this device. Browser-only: it is about this connection, not about the exam. */
const STORAGE_KEY = 'exam.lowBandwidth';

/** How often the exam page asks whether an organiser has paused, warned or ended the attempt, with a good connection. */
export const NORMAL_HEARTBEAT_MS = 10_000;

/** The same with low-bandwidth mode on: a pause still reaches the candidate within half a minute, at a third of the traffic. */
export const LOW_BANDWIDTH_HEARTBEAT_MS = 30_000;

/** The part of the browser's network information this reads; not in every browser, and not in TypeScript's own types. */
interface NetworkInformation {
  saveData?: boolean;
  effectiveType?: string;
}

/** Whether the device says it is on a slow connection, or the person asked it to save data. Absent information means no. */
function detectSlowConnection(): boolean {
  const connection = (globalThis.navigator as (Navigator & { connection?: NetworkInformation }) | undefined)?.connection;
  return connection?.saveData === true || connection?.effectiveType === 'slow-2g' || connection?.effectiveType === '2g';
}

/** Reads the remembered choice: true, false, or null when the candidate never made one (or the store is blocked). */
function readChoice(): boolean | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'on' ? true : stored === 'off' ? false : null;
  } catch {
    return null;
  }
}

/**
 * Low-bandwidth mode (FR-53): the exam page asks for its questions without their pictures, which it then fetches only when the
 * candidate asks, and checks in with the server less often. It is on when the candidate turned it on; when they never chose, it follows
 * the device, so a candidate on a connection the browser calls 2G, or with "data saver" on, gets it without having to find the switch,
 * and can still turn it off.
 */
@Injectable({ providedIn: 'root' })
export class LowBandwidthService {
  private readonly choice = signal<boolean | null>(readChoice());

  /** Whether the device suggests it: a slow connection or data saver. */
  readonly suggested = signal(detectSlowConnection());

  /** Whether the mode is on now: the candidate's choice if they made one, otherwise what the device suggests. */
  readonly enabled = computed(() => this.choice() ?? this.suggested());

  /** How long to wait between checks on the attempt's status. */
  readonly heartbeatMs = computed(() => (this.enabled() ? LOW_BANDWIDTH_HEARTBEAT_MS : NORMAL_HEARTBEAT_MS));

  /**
   * Turns the mode on or off and remembers the choice on this device.
   * @param on Whether it should be on.
   */
  set(on: boolean): void {
    this.choice.set(on);
    try {
      localStorage.setItem(STORAGE_KEY, on ? 'on' : 'off');
    } catch {
      // A blocked store only means the choice is forgotten when the page is closed.
    }
  }
}
