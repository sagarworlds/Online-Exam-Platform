import { vi } from 'vitest';
import { FrameSource, waitForFrame } from './video-frame';

function fakeVideo(readyState: number, videoWidth: number) {
  let listener: () => void = () => undefined;
  const video = {
    readyState,
    videoWidth,
    addEventListener: (_: string, l: () => void) => (listener = l),
    removeEventListener: vi.fn(),
  };
  return { video: video as FrameSource & { readyState: number; videoWidth: number }, fire: () => listener() };
}

describe('waitForFrame', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('is ready at once when a frame is already decoded', async () => {
    const { video } = fakeVideo(2, 320);

    expect(await waitForFrame(video)).toBe(true);
  });

  it('waits for the first frame instead of drawing before there is one', async () => {
    const { video, fire } = fakeVideo(0, 0);
    let result: boolean | undefined;
    void waitForFrame(video).then((r) => (result = r));

    await vi.advanceTimersByTimeAsync(1000);
    expect(result).toBeUndefined();

    Object.assign(video, { readyState: 2, videoWidth: 320 });
    fire();
    await vi.advanceTimersByTimeAsync(0);

    expect(result).toBe(true);
  });

  it('gives up when no frame arrives in time, so a black picture is skipped', async () => {
    const { video } = fakeVideo(0, 0);
    let result: boolean | undefined;
    void waitForFrame(video, 3000).then((r) => (result = r));

    await vi.advanceTimersByTimeAsync(3000);

    expect(result).toBe(false);
    expect(video.removeEventListener).toHaveBeenCalled();
  });

  it('ignores a loadeddata event that still has no picture size', async () => {
    const { video, fire } = fakeVideo(2, 0);
    let result: boolean | undefined;
    void waitForFrame(video, 3000).then((r) => (result = r));

    fire();
    await vi.advanceTimersByTimeAsync(0);
    expect(result).toBeUndefined();

    await vi.advanceTimersByTimeAsync(3000);
    expect(result).toBe(false);
  });
});
