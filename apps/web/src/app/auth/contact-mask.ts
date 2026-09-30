/** Below this length a phone number is masked entirely; revealing two digits would show too much of it. */
const MIN_PHONE_LENGTH_TO_REVEAL_DIGITS = 5;

/**
 * Masks an email address or phone number for display, so the page (or a
 * screenshot or screen-share of it) never shows the full contact detail (NFR-6).
 * Mirrors the backend's ContactMasker: an email keeps its first character and
 * its domain (`j***@example.com`); a phone number keeps only its last two
 * digits (`********10`).
 *
 * @param destination The email address or phone number as the user typed it.
 * @returns The masked value; never the whole original value, however short.
 */
export function maskContact(destination: string): string {
  const value = destination.trim();
  const at = value.lastIndexOf('@');
  return at >= 0 ? maskEmail(value.slice(0, at), value.slice(at + 1)) : maskPhone(value);
}

function maskEmail(localPart: string, domain: string): string {
  // A one-character local part would be shown in full, so hide that too.
  const visible = localPart.length > 1 ? localPart[0] : '';
  return `${visible}***@${domain}`;
}

function maskPhone(phone: string): string {
  const visibleCount = phone.length >= MIN_PHONE_LENGTH_TO_REVEAL_DIGITS ? 2 : 0;
  const hiddenCount = phone.length - visibleCount;
  return '*'.repeat(hiddenCount) + phone.slice(hiddenCount);
}
