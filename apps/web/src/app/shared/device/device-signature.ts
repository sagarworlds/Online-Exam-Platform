/**
 * A short, stable signature of this browser and device, sent with every request to the platform's own API so the server can notice
 * when one account is used from a different device (FR-26).
 *
 * It is deliberately coarse. It is a hash of a handful of things a browser already tells every site it visits (the browser's
 * identification string, language, screen size and colour depth, time zone and processor count), never anything read from the
 * person: no canvas or font probing, no storage, nothing that follows them from one site to another. Two different devices of the
 * same model and browser can share a signature; that only means a swap between them is not noticed. It is evidence for a person to
 * weigh, not an identity, and the instructions page tells candidates it is recorded.
 */
export function deviceSignature(): string {
  cached ??= signatureOf(describeDevice());
  return cached;
}

let cached: string | undefined;

/** Forgets the memoised signature; only for tests that change what the browser reports. */
export function resetDeviceSignature(): void {
  cached = undefined;
}

/** The few properties the signature is made of, joined so that changing any one changes the signature. */
function describeDevice(): string {
  const nav = typeof navigator === 'undefined' ? undefined : navigator;
  const screenInfo = typeof screen === 'undefined' ? undefined : screen;

  let timeZone = '';
  try {
    timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  } catch {
    // No Intl time zone: the offset alone still tells devices apart a little.
  }

  return [
    nav?.userAgent ?? '',
    nav?.language ?? '',
    nav?.hardwareConcurrency ?? '',
    screenInfo?.width ?? '',
    screenInfo?.height ?? '',
    screenInfo?.colorDepth ?? '',
    timeZone,
    new Date().getTimezoneOffset(),
  ].join('|');
}

/**
 * 128 bits of a fast non-cryptographic hash (cyrb128), as 32 lowercase hex characters. It needs no secrecy and no async
 * browser API, so the header can be attached to the very first request.
 */
function signatureOf(text: string): string {
  let h1 = 1779033703;
  let h2 = 3144134277;
  let h3 = 1013904242;
  let h4 = 2773480762;

  for (let i = 0; i < text.length; i++) {
    const k = text.charCodeAt(i);
    h1 = h2 ^ Math.imul(h1 ^ k, 597399067);
    h2 = h3 ^ Math.imul(h2 ^ k, 2869860233);
    h3 = h4 ^ Math.imul(h3 ^ k, 951274213);
    h4 = h1 ^ Math.imul(h4 ^ k, 2716044179);
  }

  h1 = Math.imul(h3 ^ (h1 >>> 18), 597399067);
  h2 = Math.imul(h4 ^ (h2 >>> 22), 2869860233);
  h3 = Math.imul(h1 ^ (h3 >>> 17), 951274213);
  h4 = Math.imul(h2 ^ (h4 >>> 19), 2716044179);

  return [(h1 ^ h2 ^ h3 ^ h4) >>> 0, (h2 ^ h1) >>> 0, (h3 ^ h1) >>> 0, (h4 ^ h1) >>> 0].map((n) => n.toString(16).padStart(8, '0')).join('');
}
