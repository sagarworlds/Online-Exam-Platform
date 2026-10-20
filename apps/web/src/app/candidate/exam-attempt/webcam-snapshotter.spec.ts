import { vi } from 'vitest';
import { CameraSource, WebcamSnapshotter, WebcamState } from './webcam-snapshotter';

function fakeStream(stop = vi.fn()): MediaStream {
  return { getTracks: () => [{ stop }] } as unknown as MediaStream;
}

describe('WebcamSnapshotter', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function make(camera: CameraSource | undefined, photo: Blob | null = new Blob(['x'], { type: 'image/jpeg' })) {
    const upload = vi.fn();
    const states: WebcamState[] = [];
    const grab = vi.fn().mockResolvedValue(photo);
    const snapshotter = new WebcamSnapshotter(camera, upload, (s) => states.push(s), grab);
    return { snapshotter, upload, states, grab };
  }

  it('takes a photo at once and then one every interval', async () => {
    const { snapshotter, upload } = make({ getUserMedia: () => Promise.resolve(fakeStream()) });

    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(0);
    expect(upload).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(120_000);
    expect(upload).toHaveBeenCalledTimes(2);
    expect(snapshotter.state).toBe('on');
  });

  it('reports denied, and takes nothing, when the camera is refused', async () => {
    const { snapshotter, upload, states } = make({ getUserMedia: () => Promise.reject(new Error('NotAllowedError')) });

    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(240_000);

    expect(states).toEqual(['denied']);
    expect(upload).not.toHaveBeenCalled();
  });

  it('reports denied when the browser has no camera API', async () => {
    const { snapshotter } = make(undefined);

    await snapshotter.start(120);

    expect(snapshotter.state).toBe('denied');
  });

  it('switches the camera off and stops taking photos on stop', async () => {
    const stop = vi.fn();
    const { snapshotter, upload } = make({ getUserMedia: () => Promise.resolve(fakeStream(stop)) });
    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(0);

    snapshotter.stop();
    await vi.advanceTimersByTimeAsync(600_000);

    expect(stop).toHaveBeenCalledTimes(1);
    expect(upload).toHaveBeenCalledTimes(1);
    expect(snapshotter.state).toBe('off');
  });

  it('asks for the camera only once when started twice', async () => {
    const getUserMedia = vi.fn().mockResolvedValue(fakeStream());
    const { snapshotter } = make({ getUserMedia });

    await Promise.all([snapshotter.start(120), snapshotter.start(120)]);

    expect(getUserMedia).toHaveBeenCalledTimes(1);
  });

  it('does not switch the camera on when stopped while the browser is still asking', async () => {
    const stop = vi.fn();
    let allow: (stream: MediaStream) => void = () => undefined;
    const pending = new Promise<MediaStream>((resolve) => (allow = resolve));
    const { snapshotter, upload } = make({ getUserMedia: () => pending });

    const starting = snapshotter.start(120);
    snapshotter.stop();
    allow(fakeStream(stop));
    await starting;
    await vi.advanceTimersByTimeAsync(600_000);

    expect(stop).toHaveBeenCalledTimes(1);
    expect(upload).not.toHaveBeenCalled();
    expect(snapshotter.state).toBe('off');
  });

  it('can be started again after being stopped while the browser was asking', async () => {
    let allow: (stream: MediaStream) => void = () => undefined;
    const first = new Promise<MediaStream>((resolve) => (allow = resolve));
    const getUserMedia = vi.fn().mockReturnValueOnce(first).mockResolvedValue(fakeStream());
    const { snapshotter, upload } = make({ getUserMedia });

    const starting = snapshotter.start(120);
    snapshotter.stop();
    allow(fakeStream());
    await starting;
    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(0);

    expect(snapshotter.state).toBe('on');
    expect(upload).toHaveBeenCalledTimes(1);
  });

  it('skips a frame it cannot take and tries again next interval', async () => {
    const { snapshotter, upload, grab } = make({ getUserMedia: () => Promise.resolve(fakeStream()) });
    grab.mockRejectedValueOnce(new Error('no frame'));

    await snapshotter.start(120);
    await vi.advanceTimersByTimeAsync(120_000);

    expect(upload).toHaveBeenCalledTimes(1);
  });
});
