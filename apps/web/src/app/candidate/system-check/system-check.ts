import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

/** How one part of the system check came out. */
export type CheckStatus =
  /** Fine. */
  | 'pass'
  /** Works, but the exam may feel slower than it should. Never stops the exam from starting. */
  | 'warn'
  /** The exam cannot be sat like this. Stops the exam from starting. */
  | 'fail'
  /** Could not be measured in this browser. Never stops the exam from starting. */
  | 'info';

/** One line of the system check: what was looked at, how it came out, and what to do about it. */
export interface CheckResult {
  id: 'browser' | 'connection' | 'server' | 'bandwidth';
  label: string;
  status: CheckStatus;
  /** A sentence a candidate can act on, not a technical code. */
  detail: string;
}

/** Round trips to the server at or under this many milliseconds are called fast. */
export const FAST_SERVER_MS = 800;

/** Round trips over this many milliseconds are called slow enough to warn about. */
export const SLOW_SERVER_MS = 2500;

/** A reported download speed under this many megabits a second is called slow. */
export const SLOW_DOWNLINK_MBPS = 1.5;

/** How many times the server is asked, so one slow answer (a server waking up, say) does not decide the result. */
export const SERVER_PROBES = 3;

/** What the browser check needs to know about the browser it is in. Passed in so it can be tested without a browser. */
export interface BrowserCapabilities {
  hasFetch: boolean;
  hasStructuredClone: boolean;
  hasIntl: boolean;
  /** Whether a value can be written to and read back from local storage, which the exam uses to remember what was visited. */
  canUseStorage: boolean;
}

/** Looks at the browser this code is running in. */
export function detectBrowserCapabilities(): BrowserCapabilities {
  return {
    hasFetch: typeof fetch === 'function',
    hasStructuredClone: typeof structuredClone === 'function',
    hasIntl: typeof Intl !== 'undefined' && typeof Intl.DateTimeFormat === 'function',
    canUseStorage: storageWorks(),
  };
}

function storageWorks(): boolean {
  try {
    const key = '__exam_system_check__';
    localStorage.setItem(key, '1');
    const readBack = localStorage.getItem(key) === '1';
    localStorage.removeItem(key);
    return readBack;
  } catch {
    // Storage is blocked (private mode on some browsers, or a policy): reported as a failed check, not an error.
    return false;
  }
}

/** Judges whether the browser can run the exam page. */
export function checkBrowser(capabilities: BrowserCapabilities): CheckResult {
  const label = 'Browser';
  if (!capabilities.hasFetch || !capabilities.hasStructuredClone || !capabilities.hasIntl) {
    return {
      id: 'browser',
      label,
      status: 'fail',
      detail: 'This browser is too old to run the exam. Update it, or open the exam in the latest Chrome, Edge, Firefox or Safari.',
    };
  }

  if (!capabilities.canUseStorage) {
    return {
      id: 'browser',
      label,
      status: 'fail',
      detail: 'This browser is blocking site storage, which the exam needs. Turn off private browsing or allow storage for this site, then run the check again.',
    };
  }

  return { id: 'browser', label, status: 'pass', detail: 'Your browser can run the exam.' };
}

/** Judges whether the device is online at all. */
export function checkConnection(online: boolean): CheckResult {
  return online
    ? { id: 'connection', label: 'Internet connection', status: 'pass', detail: 'You are online.' }
    : { id: 'connection', label: 'Internet connection', status: 'fail', detail: 'You are offline. Connect to the internet, then run the check again.' };
}

/** The middle value, so a single outlier does not decide. Zero for no values. */
export function median(values: readonly number[]): number {
  if (values.length === 0) {
    return 0;
  }

  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/**
 * Judges how quickly the exam server answers.
 * @param roundTripsMs How long each answered request took, in milliseconds.
 * @param failed How many requests got no answer at all.
 */
export function checkServer(roundTripsMs: readonly number[], failed: number): CheckResult {
  const label = 'Exam server';
  if (roundTripsMs.length === 0) {
    return {
      id: 'server',
      label,
      status: 'fail',
      detail: 'The exam server could not be reached. Check your connection and run the check again; if it keeps failing, tell your administrator.',
    };
  }

  const typical = median(roundTripsMs);
  const seconds = (typical / 1000).toFixed(1);
  if (typical > SLOW_SERVER_MS) {
    return {
      id: 'server',
      label,
      status: 'warn',
      detail: `The server is slow to answer (about ${seconds} s). You can still sit the exam, but each action may take a moment; wait for it rather than clicking again.`,
    };
  }

  if (failed > 0 || typical > FAST_SERVER_MS) {
    return {
      id: 'server',
      label,
      status: 'warn',
      detail: failed > 0
        ? 'Some requests to the server did not get through. Your connection may be unstable; you can continue, but it is worth moving somewhere with a steadier signal.'
        : `The server answers a little slowly (about ${seconds} s). You can continue.`,
    };
  }

  return { id: 'server', label, status: 'pass', detail: 'The exam server answers quickly.' };
}

/** What a browser that reports its connection offers. Not every browser does. */
export interface ReportedConnection {
  /** Estimated download speed in megabits a second. */
  downlink?: number;
  /** Whether the person asked the browser to save data. */
  saveData?: boolean;
}

/** Judges the connection's speed from what the browser reports about it. */
export function checkBandwidth(reported: ReportedConnection | undefined): CheckResult {
  const label = 'Connection speed';
  if (reported?.downlink === undefined) {
    return {
      id: 'bandwidth',
      label,
      status: 'info',
      detail: 'Your browser does not report its connection speed, so the exam server check above is the guide.',
    };
  }

  if (reported.downlink < SLOW_DOWNLINK_MBPS || reported.saveData === true) {
    return {
      id: 'bandwidth',
      label,
      status: 'warn',
      detail: 'Your connection looks slow or in data-saving mode. The exam sends little data, so it should still work, but a faster connection is safer.',
    };
  }

  return { id: 'bandwidth', label, status: 'pass', detail: 'Your connection speed is fine.' };
}

/** Runs the system check a candidate sees before starting an exam (FR-17). */
@Injectable({ providedIn: 'root' })
export class SystemCheckService {
  /** Runs every check and returns them in the order they are shown. */
  async run(): Promise<CheckResult[]> {
    const { roundTripsMs, failed } = await this.probeServer();
    return [
      checkBrowser(detectBrowserCapabilities()),
      checkConnection(navigator.onLine),
      checkServer(roundTripsMs, failed),
      checkBandwidth(readConnection()),
    ];
  }

  /**
   * Asks the server's health endpoint a few times in turn and times each answer. Plain fetch, not the app's HTTP client, so the
   * check neither flashes the loading bar nor depends on being signed in.
   */
  private async probeServer(): Promise<{ roundTripsMs: number[]; failed: number }> {
    const roundTripsMs: number[] = [];
    let failed = 0;
    for (let i = 0; i < SERVER_PROBES; i++) {
      const started = performance.now();
      try {
        const response = await fetch(`${environment.apiBaseUrl}/v1/health`, { cache: 'no-store' });
        if (response.ok) {
          roundTripsMs.push(performance.now() - started);
        } else {
          failed++;
        }
      } catch {
        // No answer at all (offline, blocked, server down): counted, and reported by the check, not thrown.
        failed++;
      }
    }

    return { roundTripsMs, failed };
  }
}

function readConnection(): ReportedConnection | undefined {
  const connection = (navigator as Navigator & { connection?: ReportedConnection }).connection;
  return connection ?? undefined;
}
