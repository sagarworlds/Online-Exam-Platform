/**
 * The languages the interface can be shown in (FR-51), with each named in its own script so a reader who cannot read the current
 * language can still find theirs. To add one: add its code here, add a `messages.<code>.ts` typed as {@link Messages}, and register it
 * in `MESSAGES` (i18n.service.ts). The compiler then refuses the build until every message is translated.
 *
 * The codes are the ones the API uses for question content (`en`, `hi`, `mr`), so one choice drives both the interface and the
 * language a candidate sees questions in.
 */
export const UI_LANGUAGES = [
  { code: 'en', label: 'English' },
  { code: 'hi', label: 'हिन्दी' },
  { code: 'mr', label: 'मराठी' },
] as const;

/** A language the interface can be shown in. */
export type UiLanguage = (typeof UI_LANGUAGES)[number]['code'];

/** The language everything falls back to, and the one the messages are written in first. */
export const DEFAULT_LANGUAGE: UiLanguage = 'en';

/** Whether a stored or detected value is one of the supported languages. */
export function isUiLanguage(value: unknown): value is UiLanguage {
  return UI_LANGUAGES.some((language) => language.code === value);
}
