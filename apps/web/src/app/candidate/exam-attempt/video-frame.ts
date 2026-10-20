/** The smallest part of a video element the wait uses, so a test can stand in for it. */
export interface FrameSource {
  readonly readyState: number;
  readonly videoWidth: number;
  addEventListener(type: 'loadeddata', listener: () => void, options?: { once: boolean }): void;
  removeEventListener(type: 'loadeddata', listener: () => void): void;
}

/** HTMLMediaElement.HAVE_CURRENT_DATA: a frame has been decoded and can be drawn. */
const HAVE_CURRENT_DATA = 2;

/** Longest wait for the first frame, in milliseconds. A stream that gives none by then is skipped, not drawn black. */
export const FRAME_WAIT_MS = 3000;

/**
 * Resolves true once the video holds a frame that can be drawn, or false if none arrives in time. `play()` resolving does not mean a frame
 * has been decoded, so drawing straight after it can capture a black picture: most likely the very first one, taken as soon as the camera
 * or screen share starts.
 */
export function waitForFrame(video: FrameSource, timeoutMs: number = FRAME_WAIT_MS): Promise<boolean> {
  const ready = (): boolean => video.readyState >= HAVE_CURRENT_DATA && video.videoWidth > 0;
  if (ready()) {
    return Promise.resolve(true);
  }

  return new Promise((resolve) => {
    const onData = (): void => {
      if (ready()) {
        finish(true);
      }
    };
    const timer = setTimeout(() => finish(false), timeoutMs);
    const finish = (result: boolean): void => {
      clearTimeout(timer);
      video.removeEventListener('loadeddata', onData);
      resolve(result);
    };
    video.addEventListener('loadeddata', onData);
  });
}
