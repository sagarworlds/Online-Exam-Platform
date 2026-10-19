import { vi } from 'vitest';
import { DisplaySource, ScreenSnapshotter, ScreenState } from './screen-snapshotter';

function fakeStream(stop = vi.fn(), onEnded: (fire: () => void) => void = () => undefined): MediaStream {
  const track = { stop, addEventListener: (_: string, handler: () => void) => onEnded(handler) };
  return { getTracks: () => [track], getVideoTracks: () => [track] } as unknown as MediaStream;
}

describe('ScreenSnapshotter', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function make(display: DisplaySource | undefined) {
    const upload = vi.fn();
    const states: ScreenState[] = [];
    const grab = vi.fn().mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }));
    const snapshotter = new ScreenSnapshotter(display, upload, (s) => states.push(s), grab);
    return { snapshotter, upload, states };
  }

  it('waits for the candidate: offering does not ask the browser for anything', () => {
    const getDisplayMedia = vi.fn();
    const { snapshotter, states } = make({ getDisplayMedia });

    snapshotter.offer();

    expect(states).toEqual(['idle']);
    expect(getDisplayMedia).not.toHaveBeenCalled();
  });

  it('captures at once and then every interval once sharing starts', async () => {
    const { snapshotter, upload } = make({ getDisplayMedia: () => Promise.resolve(fakeStream()) });

    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(0);
    expect(upload).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(120_000);
    expect(upload).toHaveBeenCalledTimes(2);
    expect(snapshotter.state).toBe('on');
  });

  it('reports denied, and captures nothing, when the picker is dismissed', async () => {
    const { snapshotter, upload } = make({ getDisplayMedia: () => Promise.reject(new Error('NotAllowedError')) });

    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(240_000);

    expect(snapshotter.state).toBe('denied');
    expect(upload).not.toHaveBeenCalled();
  });

  it('reports denied when the browser cannot share a screen', async () => {
    const { snapshotter } = make(undefined);

    await snapshotter.start(120);

    expect(snapshotter.state).toBe('denied');
  });

  it('goes back to offering the button, and stops capturing, when the candidate stops sharing in the browser', async () => {
    let ended: () => void = () => undefined;
    const { snapshotter, upload } = make({ getDisplayMedia: () => Promise.resolve(fakeStream(vi.fn(), (fire) => (ended = fire))) });
    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(0);

    ended();
    await vi.advanceTimersByTimeAsync(600_000);

    expect(snapshotter.state).toBe('idle');
    expect(upload).toHaveBeenCalledTimes(1);
  });

  it('does not start capturing when stopped while the browser is still showing its picker', async () => {
    const stop = vi.fn();
    let allow: (stream: MediaStream) => void = () => undefined;
    const pending = new Promise<MediaStream>((resolve) => (allow = resolve));
    const { snapshotter, upload } = make({ getDisplayMedia: () => pending });

    const starting = snapshotter.start(120);
    snapshotter.stop();
    allow(fakeStream(stop));
    await starting;
    await vi.advanceTimersByTimeAsync(600_000);

    expect(stop).toHaveBeenCalledTimes(1);
    expect(upload).not.toHaveBeenCalled();
    expect(snapshotter.state).toBe('off');
  });

  it('ends the share and stops capturing on stop', async () => {
    const stop = vi.fn();
    const { snapshotter, upload } = make({ getDisplayMedia: () => Promise.resolve(fakeStream(stop)) });
    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(0);

    snapshotter.stop();
    await vi.advanceTimersByTimeAsync(600_000);

    expect(stop).toHaveBeenCalledTimes(1);
    expect(upload).toHaveBeenCalledTimes(1);
    expect(snapshotter.state).toBe('off');
  });
});
