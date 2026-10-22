import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, vi } from 'vitest';
import { AppUpdateService, RELOAD_PAGE, UPDATE_CHECK_INTERVAL_MS } from './app-update.service';

const indexNaming = (script: string) => `<html><head><script src="${script}" type="module"></script></head><body></body></html>`;

/** What the server answers for index.html. */
const serverAnswers = (html: string, ok = true) => vi.fn().mockResolvedValue({ ok, text: async () => html });

describe('AppUpdateService', () => {
  let startScript: HTMLScriptElement | null;
  let reload: ReturnType<typeof vi.fn>;
  let versionUpdates: Subject<{ type: string }>;
  let unrecoverable: Subject<unknown>;
  let checkForUpdate: ReturnType<typeof vi.fn>;

  /** The service, made once the page names the start script it is running from (the build names it with a hash of its contents). */
  function service(options: { running?: string | null } = {}): AppUpdateService {
    const running = options.running === undefined ? 'main-OLD.js' : options.running;
    if (running !== null) {
      startScript = document.createElement('script');
      startScript.setAttribute('src', running);
      document.head.appendChild(startScript);
    }
    return TestBed.inject(AppUpdateService);
  }

  beforeEach(() => {
    startScript = null;
    reload = vi.fn();
    versionUpdates = new Subject();
    unrecoverable = new Subject();
    checkForUpdate = vi.fn().mockResolvedValue(false);
    TestBed.configureTestingModule({
      providers: [
        { provide: RELOAD_PAGE, useValue: reload },
        { provide: SwUpdate, useValue: { isEnabled: true, versionUpdates, unrecoverable, checkForUpdate } },
      ],
    });
  });

  afterEach(() => {
    startScript?.remove();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  describe('looking for a newer build', () => {
    it('flags an update when the server names another start script than the one running, asking past the worker and the cache', async () => {
      const fetchSpy = serverAnswers(indexNaming('main-NEW.js'));
      vi.stubGlobal('fetch', fetchSpy);
      const updates = service();

      await updates.check();

      expect(updates.updateAvailable()).toBe(true);
      const [url, init] = fetchSpy.mock.calls[0] as [URL, RequestInit];
      expect(url.pathname).toBe('/index.html');
      expect(url.searchParams.get('ngsw-bypass')).toBe('true');
      expect(init).toEqual({ cache: 'no-store', headers: { 'ngsw-bypass': 'true' } });
    });

    it('flags nothing while the server names the script already running', async () => {
      vi.stubGlobal('fetch', serverAnswers(indexNaming('main-OLD.js')));
      const updates = service();

      await updates.check();

      expect(updates.updateAvailable()).toBe(false);
    });

    it.each([
      ['the server answers with an error', () => serverAnswers(indexNaming('main-NEW.js'), false)],
      ['the answer names no start script', () => serverAnswers('<html><body>Not the app</body></html>')],
      ['the network is down', () => vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))],
    ])('flags nothing when %s, as there is nothing to compare with', async (_case, makeFetch) => {
      vi.stubGlobal('fetch', makeFetch());
      const updates = service();

      await updates.check();

      expect(updates.updateAvailable()).toBe(false);
    });

    it('does not ask when the page cannot say which script it runs from, as the development server cannot', async () => {
      const fetchSpy = serverAnswers(indexNaming('main-NEW.js'));
      vi.stubGlobal('fetch', fetchSpy);
      const updates = service({ running: null });

      await updates.check();

      expect(fetchSpy).not.toHaveBeenCalled();
      expect(updates.updateAvailable()).toBe(false);
    });

    it('asks the worker to look for its own update as well', async () => {
      vi.stubGlobal('fetch', serverAnswers(indexNaming('main-OLD.js')));

      await service().check();

      expect(checkForUpdate).toHaveBeenCalledTimes(1);
    });

    it('asks at most once a minute, however many things prompt it', async () => {
      vi.useFakeTimers();
      const fetchSpy = serverAnswers(indexNaming('main-OLD.js'));
      vi.stubGlobal('fetch', fetchSpy);
      const updates = service();

      await updates.check();
      await updates.check();
      expect(fetchSpy).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(60_000);
      await updates.check();
      expect(fetchSpy).toHaveBeenCalledTimes(2);
    });

    it('stays flagged, and stops asking, once an update is known', async () => {
      vi.useFakeTimers();
      const fetchSpy = serverAnswers(indexNaming('main-NEW.js'));
      vi.stubGlobal('fetch', fetchSpy);
      const updates = service();
      await updates.check();

      await vi.advanceTimersByTimeAsync(5 * 60_000);
      await updates.check();

      expect(updates.updateAvailable()).toBe(true);
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });
  });

  describe('when it looks', () => {
    it('looks soon after start-up, then on a timer, so a tab left open still hears of a new build', async () => {
      vi.useFakeTimers();
      const fetchSpy = serverAnswers(indexNaming('main-OLD.js'));
      vi.stubGlobal('fetch', fetchSpy);
      service().start();
      expect(fetchSpy).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(3_000);
      expect(fetchSpy).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(UPDATE_CHECK_INTERVAL_MS);
      expect(fetchSpy).toHaveBeenCalledTimes(2);
    });

    it('looks when the tab comes back to the front, and when the network returns', async () => {
      vi.useFakeTimers();
      const fetchSpy = serverAnswers(indexNaming('main-OLD.js'));
      vi.stubGlobal('fetch', fetchSpy);
      service().start();
      await vi.advanceTimersByTimeAsync(3_000);
      expect(fetchSpy).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(60_000);
      document.dispatchEvent(new Event('visibilitychange'));
      await vi.advanceTimersByTimeAsync(0);
      expect(fetchSpy).toHaveBeenCalledTimes(2);

      await vi.advanceTimersByTimeAsync(60_000);
      window.dispatchEvent(new Event('online'));
      await vi.advanceTimersByTimeAsync(0);
      expect(fetchSpy).toHaveBeenCalledTimes(3);
    });

    it('starts only once', async () => {
      vi.useFakeTimers();
      const fetchSpy = serverAnswers(indexNaming('main-OLD.js'));
      vi.stubGlobal('fetch', fetchSpy);
      const updates = service();

      updates.start();
      updates.start();
      await vi.advanceTimersByTimeAsync(3_000);

      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });
  });

  describe('what the service worker says', () => {
    it('flags an update when the worker has a new version ready', () => {
      const updates = service();
      updates.start();

      versionUpdates.next({ type: 'VERSION_DETECTED' });
      expect(updates.updateAvailable()).toBe(false);

      versionUpdates.next({ type: 'VERSION_READY' });
      expect(updates.updateAvailable()).toBe(true);
    });

    it('flags an update when the worker can no longer serve the page', () => {
      const updates = service();
      updates.start();

      unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'The cache is damaged' });

      expect(updates.updateAvailable()).toBe(true);
    });
  });

  describe('updating', () => {
    let unregister: ReturnType<typeof vi.fn>;
    let deleteCache: ReturnType<typeof vi.fn>;
    const view = () => document.defaultView as Window & { caches?: unknown };

    beforeEach(() => {
      unregister = vi.fn().mockResolvedValue(true);
      deleteCache = vi.fn().mockResolvedValue(true);
      Object.defineProperty(view().navigator, 'serviceWorker', {
        configurable: true,
        value: { getRegistrations: async () => [{ unregister }, { unregister }] },
      });
      Object.defineProperty(view(), 'caches', {
        configurable: true,
        value: { keys: async () => ['ngsw:/:db:control', 'ngsw:/:1:assets:app:cache'], delete: deleteCache },
      });
    });

    afterEach(() => {
      Reflect.deleteProperty(view().navigator, 'serviceWorker');
      Reflect.deleteProperty(view(), 'caches');
    });

    it('drops the worker and every cache it kept, then reloads, which is what clearing the site data does by hand', async () => {
      const updates = service();

      await updates.apply();

      expect(unregister).toHaveBeenCalledTimes(2);
      expect(deleteCache).toHaveBeenCalledWith('ngsw:/:db:control');
      expect(deleteCache).toHaveBeenCalledWith('ngsw:/:1:assets:app:cache');
      expect(reload).toHaveBeenCalledTimes(1);
      expect(unregister.mock.invocationCallOrder[0]).toBeLessThan(reload.mock.invocationCallOrder[0]);
      expect(deleteCache.mock.invocationCallOrder[1]).toBeLessThan(reload.mock.invocationCallOrder[0]);
    });

    it('reloads all the same when something could not be dropped', async () => {
      unregister.mockRejectedValue(new Error('SecurityError'));
      const updates = service();

      await updates.apply();

      expect(reload).toHaveBeenCalledTimes(1);
    });

    it('says an update is under way, and does it once however often it is asked', async () => {
      const updates = service();

      const first = updates.apply();
      expect(updates.applying()).toBe(true);
      await updates.apply();
      await first;

      expect(reload).toHaveBeenCalledTimes(1);
    });
  });
});
