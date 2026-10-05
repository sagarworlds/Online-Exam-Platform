import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import {
  BrowserCapabilities,
  FAST_SERVER_MS,
  SERVER_PROBES,
  SLOW_DOWNLINK_MBPS,
  SLOW_SERVER_MS,
  SystemCheckService,
  checkBandwidth,
  checkBrowser,
  checkConnection,
  checkServer,
  detectBrowserCapabilities,
  median,
} from './system-check';

const CAPABLE: BrowserCapabilities = { hasFetch: true, hasStructuredClone: true, hasIntl: true, canUseStorage: true };

describe('checkBrowser', () => {
  it('passes a browser with everything the exam needs', () => {
    expect(checkBrowser(CAPABLE).status).toBe('pass');
  });

  it.each([
    ['fetch', { hasFetch: false }],
    ['structuredClone', { hasStructuredClone: false }],
    ['Intl', { hasIntl: false }],
  ])('fails a browser without %s, and says to update it', (_name, missing) => {
    const result = checkBrowser({ ...CAPABLE, ...missing });

    expect(result.status).toBe('fail');
    expect(result.detail).toContain('too old');
  });

  it('fails a browser that blocks storage, and says how to fix it', () => {
    const result = checkBrowser({ ...CAPABLE, canUseStorage: false });

    expect(result.status).toBe('fail');
    expect(result.detail).toContain('private browsing');
  });
});

describe('detectBrowserCapabilities', () => {
  afterEach(() => vi.restoreAllMocks());

  it('finds this test browser capable', () => {
    expect(detectBrowserCapabilities()).toEqual(CAPABLE);
  });

  it('reports blocked storage instead of throwing', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });

    expect(detectBrowserCapabilities().canUseStorage).toBe(false);
  });
});

describe('checkConnection', () => {
  it('passes when online and fails when offline', () => {
    expect(checkConnection(true).status).toBe('pass');
    expect(checkConnection(false).status).toBe('fail');
  });
});

describe('median', () => {
  it('takes the middle value, so one outlier does not decide', () => {
    expect(median([100, 5000, 120])).toBe(120);
    expect(median([100, 200])).toBe(150);
    expect(median([])).toBe(0);
  });
});

describe('checkServer', () => {
  it('fails when no request was answered', () => {
    const result = checkServer([], SERVER_PROBES);

    expect(result.status).toBe('fail');
    expect(result.detail).toContain('could not be reached');
  });

  it('passes a server that answers quickly', () => {
    expect(checkServer([120, 150, 130], 0).status).toBe('pass');
  });

  it('warns, without failing, about a server that is a little slow', () => {
    expect(checkServer([FAST_SERVER_MS + 200, FAST_SERVER_MS + 300, FAST_SERVER_MS + 250], 0).status).toBe('warn');
  });

  it('warns, without failing, about a very slow server, and tells the candidate to wait', () => {
    const result = checkServer([SLOW_SERVER_MS + 1000, SLOW_SERVER_MS + 2000, SLOW_SERVER_MS + 1500], 0);

    expect(result.status).toBe('warn');
    expect(result.detail).toContain('rather than clicking again');
  });

  it('is not thrown off by one slow first answer, as when a server is waking up', () => {
    expect(checkServer([9000, 140, 130], 0).status).toBe('pass');
  });

  it('warns about an unstable connection when some requests got no answer', () => {
    const result = checkServer([120, 130], 1);

    expect(result.status).toBe('warn');
    expect(result.detail).toContain('did not get through');
  });
});

describe('checkBandwidth', () => {
  it('says it could not measure when the browser reports nothing, and does not block', () => {
    expect(checkBandwidth(undefined).status).toBe('info');
    expect(checkBandwidth({}).status).toBe('info');
  });

  it('passes a fast connection', () => {
    expect(checkBandwidth({ downlink: 10 }).status).toBe('pass');
  });

  it('warns about a slow connection or data-saving mode', () => {
    expect(checkBandwidth({ downlink: SLOW_DOWNLINK_MBPS - 0.5 }).status).toBe('warn');
    expect(checkBandwidth({ downlink: 10, saveData: true }).status).toBe('warn');
  });
});

describe('SystemCheckService', () => {
  afterEach(() => vi.restoreAllMocks());

  it('runs every check in the order they are shown, timing the server', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('Healthy', { status: 200 }));

    const results = await TestBed.inject(SystemCheckService).run();

    expect(results.map((r) => r.id)).toEqual(['browser', 'connection', 'server', 'bandwidth']);
    expect(fetchSpy).toHaveBeenCalledTimes(SERVER_PROBES);
    expect(fetchSpy.mock.calls[0][0]).toMatch(/\/v1\/health$/);
    expect(results.find((r) => r.id === 'server')?.status).toBe('pass');
  });

  it('fails the server check, and does not throw, when the server cannot be reached', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));

    const results = await TestBed.inject(SystemCheckService).run();

    expect(results.find((r) => r.id === 'server')?.status).toBe('fail');
  });

  it('counts a server error answer as a request that did not get through', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('boom', { status: 503 }));

    const results = await TestBed.inject(SystemCheckService).run();

    expect(results.find((r) => r.id === 'server')?.status).toBe('fail');
  });
});
