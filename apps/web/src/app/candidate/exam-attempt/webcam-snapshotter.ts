import { waitForFrame } from './video-frame';

/** Whether the camera is being used to take photos. */
export type WebcamState = 'off' | 'on' | 'denied';

/** The smallest part of the browser's camera API the snapshotter uses, so a test can stand in for it. */
export interface CameraSource {
  getUserMedia(constraints: MediaStreamConstraints): Promise<MediaStream>;
}

/** Takes a still from the camera stream as a JPEG, or null when the browser cannot. */
export type FrameGrabber = (stream: MediaStream) => Promise<Blob | null>;

/** How wide a photo is. A reviewer needs to see who is at the desk, not detail, and a small image keeps the upload small and fast. */
const PHOTO_WIDTH = 320;
const PHOTO_HEIGHT = 240;
const JPEG_QUALITY = 0.7;

/**
 * Takes a webcam photo of the candidate at a fixed interval while an exam that asks for them is open (FR-24), and hands each one to
 * `upload`. It only captures and sends: whether photos are taken at all, for whom, and how often is the server's decision, told to the
 * page as a policy. The camera light is the candidate's cue that it is on, so the stream is stopped as soon as the exam page is left.
 *
 * A refusal of the camera is not an error to hide: the state becomes `denied` so the page can say photos are not being taken, and the
 * exam goes on. A photo that fails to upload is dropped, because the next interval brings another.
 */
export class WebcamSnapshotter {
  private stream: MediaStream | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;
  private current: WebcamState = 'off';
  private starting = false;
  private generation = 0;

  /**
   * @param camera Where the camera stream comes from; `navigator.mediaDevices` in the browser.
   * @param upload Sends one photo to the server.
   * @param onState Called whenever the state changes, so the page can show it.
   * @param grab Takes a still from the stream; the default draws the video onto a small canvas.
   */
  constructor(
    private readonly camera: CameraSource | undefined,
    private readonly upload: (photo: Blob) => void,
    private readonly onState: (state: WebcamState) => void,
    private readonly grab: FrameGrabber = grabFrameFromVideo,
  ) {}

  /** Whether the camera is on, refused, or not in use. */
  get state(): WebcamState {
    return this.current;
  }

  /**
   * Asks for the camera and starts taking photos. Safe to call twice.
   * @param intervalSeconds Seconds between photos, as the server's policy says.
   */
  async start(intervalSeconds: number): Promise<void> {
    if (this.stream !== null || this.starting) {
      return;
    }

    if (this.camera === undefined) {
      // No camera API (an insecure page or an old browser) is the same to the candidate as a refusal: no photos are being taken.
      this.setState('denied');
      return;
    }

    this.starting = true;
    // Each stop() moves this on, so a camera granted after the page was left can be told apart from one asked for now.
    const generation = this.generation;
    let stream: MediaStream;
    try {
      stream = await this.camera.getUserMedia({ video: { width: PHOTO_WIDTH, height: PHOTO_HEIGHT }, audio: false });
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
      // The exam page was left (or the attempt ended) while the browser was still asking: switch the camera straight back off.
      stream.getTracks().forEach((track) => track.stop());
      return;
    }

    this.stream = stream;
    this.setState('on');
    // One photo straight away, so a reviewer sees the candidate at the start, then one every interval.
    void this.capture();
    this.timer = setInterval(() => void this.capture(), intervalSeconds * 1000);
  }

  /** Stops the photos and switches the camera off. Safe to call when not started. */
  stop(): void {
    this.generation++;
    this.starting = false;
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
    this.setState('off');
  }

  private async capture(): Promise<void> {
    const stream = this.stream;
    if (stream === null) {
      return;
    }

    try {
      const photo = await this.grab(stream);
      if (photo !== null && this.stream === stream) {
        this.upload(photo);
      }
    } catch {
      // A frame that cannot be taken is skipped; the next interval tries again.
    }
  }

  private setState(state: WebcamState): void {
    if (this.current !== state) {
      this.current = state;
      this.onState(state);
    }
  }
}

/** Draws one frame of the stream onto a small canvas and returns it as a JPEG. */
async function grabFrameFromVideo(stream: MediaStream): Promise<Blob | null> {
  const video = document.createElement('video');
  video.muted = true;
  video.playsInline = true;
  video.srcObject = stream;
  await video.play();
  // play() resolving is not a decoded frame; drawing now could save a black photo, most likely the first one.
  if (!(await waitForFrame(video))) {
    video.srcObject = null;
    return null;
  }

  const canvas = document.createElement('canvas');
  canvas.width = PHOTO_WIDTH;
  canvas.height = PHOTO_HEIGHT;
  const context = canvas.getContext('2d');
  if (context === null) {
    return null;
  }

  context.drawImage(video, 0, 0, PHOTO_WIDTH, PHOTO_HEIGHT);
  video.pause();
  video.srcObject = null;
  return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY));
}
