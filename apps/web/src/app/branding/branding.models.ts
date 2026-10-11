/**
 * The institute's branding as the API returns it (FR-41). `primaryColour` is an upper-case six-digit hex code such as `#1A56DB`, or null for
 * the platform's default colour. `hasLogo` says whether the logo route serves an image.
 */
export interface BrandingDto {
  instituteName: string | null;
  primaryColour: string | null;
  hasLogo: boolean;
  updatedAtUtc: string | null;
}

/** What the branding form sends. A blank field clears it back to the platform's default. */
export interface BrandingRequest {
  instituteName: string | null;
  primaryColour: string | null;
}

/** The longest institute name the API accepts (FR-41). */
export const MAX_INSTITUTE_NAME_LENGTH = 100;

/** The largest logo the API accepts, in bytes (FR-41). The same limit as `LogoImage.MaxBytes` in the API. */
export const MAX_LOGO_BYTES = 256 * 1024;

/**
 * The image types the API accepts as a logo (FR-41). SVG is not among them: it is markup that can carry script. The API reads the format
 * from the file's own bytes, so this list only lets the person see the reason before an upload.
 */
export const LOGO_MEDIA_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/webp'];

/** Why a chosen file cannot be uploaded as a logo. */
export type LogoProblem = 'type' | 'size';

/** Checks a chosen file against the logo rules; null when it may be uploaded. */
export function logoProblem(file: { type: string; size: number }): LogoProblem | null {
  if (!LOGO_MEDIA_TYPES.includes(file.type)) {
    return 'type';
  }

  return file.size > MAX_LOGO_BYTES ? 'size' : null;
}

const HEX_COLOUR = /^#[0-9A-Fa-f]{6}$/;

/** Whether a colour is a six-digit hex code, the only form the API stores (FR-41). */
export function isHexColour(value: string): boolean {
  return HEX_COLOUR.test(value.trim());
}
