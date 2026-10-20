import { waitForFrame } from './video-frame';

/** Whether the candidate's screen is being captured. `idle` means the server asks for it but the candidate has not yet chosen to share. */
export type ScreenState = 'off' | 'idle' | 'on' | 'denied';

/** The smallest part of the browser's screen-sharing API the snapshotter uses, so a test can stand in for it. */
export interface DisplaySource {
  getDisplayMedia(constraints: DisplayMediaStreamOptions): Promise<MediaStream>;
}

/** Takes a still from the screen stream as a JPEG, or null when the browser cannot. */
export type ScreenFrameGrabber = (stream: MediaStream) => Promise<Blob | null>;

/** Wide enough for a reviewer to read what is on screen, small enough to stay well under the server's size cap. */
const CAPTURE_WIDTH = 1280;
const CAPTURE_HEIGHT = 720;
const JPEG_QUALITY = 0.6;

/**
 * Takes a picture of the candidate's screen at a fixed interval while an exam that asks for them is open (FR-25), and hands each one to
 * `upload`. The browser only allows screen sharing after a click and shows its own picker and "sharing" indicator, so the page offers a
 * button and calls {@link start} from it; whether captures are taken at all, for whom and how often is the server's decision, told to
 * the page as a policy.
 *
 * A refusal is not hidden: the state becomes `denied` so the page can say captures are not being taken, and the exam goes on. When the
 * candidate stops sharing from the browser's own control, the state returns to `idle` so they can start again. A capture that fails to
 * upload is dropped, because the next interval brings another.
 */
export class ScreenSnapshotter {
  private stream: MediaStream | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;
  private current: ScreenState = 'off';
  private starting = false;
  private generation = 0;

  /**
   * @param display Where the screen stream comes from; `navigator.mediaDevices` in the browser.
   * @param upload Sends one capture to the server.
   * @param onState Called whenever the state changes, so the page can show it.
   * @param grab Takes a still from the stream; the default draws the video onto a canvas.
   */
  constructor(
    private readonly display: DisplaySource | undefined,
    private readonly upload: (image: Blob) => void,
    private readonly onState: (state: ScreenState) => void,
    private readonly grab: ScreenFrameGrabber = grabFrameFromVideo,
  ) {}

  /** Whether the screen is being captured, is waiting for the candidate to share, was refused, or is not in use. */
  get state(): ScreenState {
    return this.current;
  }

  /** Marks the capture as asked for but not yet started, so the page shows the share button. */
  offer(): void {
    if (this.current === 'off') {
      this.setState('idle');
    }
  }

  /**
   * Asks the browser to share the screen and starts taking pictures. Must be called from a click. Safe to call twice.
   * @param intervalSeconds Seconds between pictures, as the server's policy says.
   */
  async start(intervalSeconds: number): Promise<void> {
    if (this.stream !== null || this.starting) {
      return;
    }

    if (this.display === undefined || typeof this.display.getDisplayMedia !== 'function') {
      // No screen-sharing API (a phone, or an old browser) is the same to the candidate as a refusal: nothing is being captured.
      this.setState('denied');
      return;
    }

    this.starting = true;
    // Each halt moves this on, so a share chosen after the page was left can be told apart from one asked for now.
    const generation = this.generation;
    let stream: MediaStream;
    try {
      stream = await this.display.getDisplayMedia({ video: true, audio: false });
    } catch {
      if (generation === this.generation) {
        this.setState('denied');
      }
      return;
    } finally {
      if (generation === this.generation) {
        this.starting = false;
      }
    }

    if (generation !== this.generation) {
      // The exam page was left (or the attempt ended) while the picker was open: end the share the moment it is chosen.
      stream.getTracks().forEach((track) => track.stop());
      return;
    }

    this.stream = stream;
    // The browser's own "stop sharing" control ends the track; go back to offering the button rather than pretending it is still on.
    stream.getVideoTracks()[0]?.addEventListener('ended', () => this.halt('idle'));
    this.setState('on');
    void this.capture();
    this.timer = setInterval(() => void this.capture(), intervalSeconds * 1000);
  }

  /** Stops the captures and ends the share. Safe to call when not started. */
  stop(): void {
    this.halt('off');
  }

  private halt(next: ScreenState): void {
    this.generation++;
    this.starting = false;
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
    this.setState(next);
  }

  private async capture(): Promise<void> {
    const stream = this.stream;
    if (stream === null) {
      return;
    }

    try {
      const image = await this.grab(stream);
      if (image !== null && this.stream === stream) {
        this.upload(image);
      }
    } catch {
      // A frame that cannot be taken is skipped; the next interval tries again.
    }
  }

  private setState(state: ScreenState): void {
    if (this.current !== state) {
      this.current = state;
      this.onState(state);
    }
  }
}

/** Draws one frame of the stream onto a canvas and returns it as a JPEG. */
async function grabFrameFromVideo(stream: MediaStream): Promise<Blob | null> {
  const video = document.createElement('video');
  video.muted = true;
  video.playsInline = true;
  video.srcObject = stream;
  await video.play();
  // play() resolving is not a decoded frame; drawing now could save a black picture, most likely the first one.
  if (!(await waitForFrame(video))) {
    video.srcObject = null;
    return null;
  }

  const canvas = document.createElement('canvas');
  canvas.width = CAPTURE_WIDTH;
  canvas.height = CAPTURE_HEIGHT;
  const context = canvas.getContext('2d');
  if (context === null) {
    return null;
  }

  context.drawImage(video, 0, 0, CAPTURE_WIDTH, CAPTURE_HEIGHT);
  video.pause();
  video.srcObject = null;
  return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY));
}
