import { EN } from '../../src/app/i18n/messages.en';

/** A key of the app's English message file: a typo in a test fails the type check, not the run. */
export type MessageKey = keyof typeof EN;

/**
 * The English wording the app shows for a message key, with `{name}` replaced by the parameter of that name (the rule
 * `I18nService.t` applies). Tests find controls by this wording, so a reworded button changes the app and its tests in one place.
 */
export function text(key: MessageKey, params: Record<string, string | number> = {}): string {
  return EN[key].replace(/\{(\w+)\}/g, (whole, name: string) => (name in params ? String(params[name]) : whole));
}
