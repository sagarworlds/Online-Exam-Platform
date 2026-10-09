import { FocusViolationKind } from '../candidate.models';
import { FocusMonitor } from './focus-monitor';

describe('FocusMonitor', () => {
  let departures: FocusViolationKind[];
  let monitor: FocusMonitor;
  let fullscreenElement: Element | null;
  let visibility: DocumentVisibilityState;

  beforeEach(() => {
    departures = [];
    fullscreenElement = null;
    visibility = 'visible';
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, get: () => fullscreenElement });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
    monitor = new FocusMonitor(document, (kind) => departures.push(kind));
  });

  afterEach(() => {
    monitor.stop();
    delete (document as unknown as Record<string, unknown>)['fullscreenElement'];
    delete (document as unknown as Record<string, unknown>)['visibilityState'];
  });

  const hideTab = () => {
    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
  };
  const showTab = () => {
    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
  };
  const blur = () => window.dispatchEvent(new Event('blur'));
  const focus = () => window.dispatchEvent(new Event('focus'));
  const setFullscreen = (on: boolean) => {
    fullscreenElement = on ? document.documentElement : null;
    document.dispatchEvent(new Event('fullscreenchange'));
  };

  it('reports nothing until it is started', () => {
    hideTab();
    blur();

    expect(departures).toEqual([]);
  });

  it('reports a hidden tab', () => {
    monitor.start();

    hideTab();

    expect(departures).toEqual(['TabHidden']);
  });

  it('reports a window that lost focus without being hidden, such as a click into another application', () => {
    monitor.start();

    blur();

    expect(departures).toEqual(['WindowBlurred']);
  });

  it('counts one trip away once, although a tab switch fires both signals', () => {
    monitor.start();

    hideTab();
    blur();

    expect(departures).toEqual(['TabHidden']);
  });

  it('counts the same signals in the other order as one trip too', () => {
    monitor.start();

    blur();
    hideTab();

    expect(departures).toEqual(['WindowBlurred']);
  });

  it('counts a second trip once the candidate is back', () => {
    monitor.start();
    hideTab();
    blur();
    showTab();
    focus();

    hideTab();
    blur();

    expect(departures).toEqual(['TabHidden', 'TabHidden']);
  });

  it('is back again on focus alone, for a window that was only blurred', () => {
    monitor.start();
    blur();
    focus();

    blur();

    expect(departures).toEqual(['WindowBlurred', 'WindowBlurred']);
  });

  it('does not treat a focus event in a still-hidden tab as coming back', () => {
    monitor.start();
    hideTab();

    focus();
    blur();

    expect(departures).toEqual(['TabHidden']);
  });

  it('reports leaving full screen, but only after having been in it', () => {
    monitor.start();

    setFullscreen(false);
    expect(departures).toEqual([]);

    setFullscreen(true);
    expect(departures).toEqual([]);

    setFullscreen(false);
    expect(departures).toEqual(['FullscreenExited']);
  });

  it('counts leaving full screen even while the tab trip is still counted', () => {
    monitor.start();
    setFullscreen(true);
    blur();

    setFullscreen(false);

    expect(departures).toEqual(['WindowBlurred', 'FullscreenExited']);
  });

  it('knows a page that started in full screen has left it', () => {
    fullscreenElement = document.documentElement;
    monitor.start();

    setFullscreen(false);

    expect(departures).toEqual(['FullscreenExited']);
  });

  it('stops listening, and can be started again', () => {
    monitor.start();
    monitor.stop();
    hideTab();
    blur();
    expect(departures).toEqual([]);

    showTab();
    monitor.start();
    blur();
    expect(departures).toEqual(['WindowBlurred']);
  });

  it('does not report twice when started twice', () => {
    monitor.start();
    monitor.start();

    blur();

    expect(departures).toEqual(['WindowBlurred']);
  });

  it('can be stopped without having been started', () => {
    expect(() => monitor.stop()).not.toThrow();
  });

  it('asks the browser for full screen, and carries on if it refuses', async () => {
    const request = vi.fn().mockRejectedValue(new Error('needs a gesture'));
    document.documentElement.requestFullscreen = request;

    await expect(monitor.enterFullscreen()).resolves.toBeUndefined();

    expect(request).toHaveBeenCalledTimes(1);
  });

  it('says whether the browser offers full screen', () => {
    Object.defineProperty(document, 'fullscreenEnabled', { configurable: true, value: true });
    expect(monitor.canEnterFullscreen).toBe(true);

    Object.defineProperty(document, 'fullscreenEnabled', { configurable: true, value: false });
    expect(monitor.canEnterFullscreen).toBe(false);

    delete (document as unknown as Record<string, unknown>)['fullscreenEnabled'];
  });
});
