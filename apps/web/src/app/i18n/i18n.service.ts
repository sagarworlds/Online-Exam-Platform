import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';
import { DEFAULT_LANGUAGE, UI_LANGUAGES, UiLanguage, isUiLanguage } from './languages';
import { EN, MessageKey, Messages, PluralKey } from './messages.en';
import { HI } from './messages.hi';
import { MR } from './messages.mr';

/** Where the chosen language is remembered, so it is the same after a reload and on the next visit. Browser-only. */
export const LANGUAGE_STORAGE_KEY = 'exam-platform.language';

/** Every language's messages. A language is not offered until it is here, and the type makes each one complete. */
const MESSAGES: Record<UiLanguage, Messages> = { en: EN, hi: HI, mr: MR };

/** Values that can fill a `{name}` in a message. */
export type MessageParams = Record<string, string | number>;

/** Looks a message up and fills in its parameters. */
export type Translate = (key: MessageKey, params?: MessageParams) => string;

/** Replaces each `{name}` in a message with the parameter of that name; one that was not given is left as written so the gap is visible. */
function fill(message: string, params?: MessageParams): string {
  return params === undefined ? message : message.replace(/\{(\w+)\}/g, (whole, name: string) => (name in params ? String(params[name]) : whole));
}

/** Translates into English with no service, for code that runs outside a component and for the unit tests of the sentences themselves. */
export const englishTranslate: Translate = (key, params) => fill(EN[key], params);

/** The key of the form of a message that a count calls for: `base.one` for exactly one, `base.other` for anything else. */
function pluralKey(base: PluralKey, count: number): MessageKey {
  return `${base}.${count === 1 ? 'one' : 'other'}` as MessageKey;
}

/** What code that only builds sentences needs: the service provides it, and {@link englishWords} stands in for it where there is no service. */
export interface Words {
  t: Translate;
  plural(base: PluralKey, count: number, params?: MessageParams): string;
}

/** The words in English, so a function that builds sentences works with no service and its tests need none. */
export const englishWords: Words = {
  t: englishTranslate,
  plural: (base, count, params = {}) => englishTranslate(pluralKey(base, count), { count, ...params }),
};

/**
 * The language the interface is shown in (FR-51), and the lookup of every message in it. The choice is a signal, so anything that
 * reads a message through {@link t} redraws when it changes, with no reload. It is remembered in the browser and, through the
 * `Accept-Language` header the language interceptor adds, tells the API which language to show question content in.
 */
@Injectable({ providedIn: 'root' })
export class I18nService implements Words {
  private readonly document = inject(DOCUMENT);

  /** The language in use. */
  readonly language = signal<UiLanguage>(this.initialLanguage());

  /** The languages that can be chosen, for a picker. */
  readonly languages = UI_LANGUAGES;

  constructor() {
    this.document.documentElement.lang = this.language();
  }

  /** Switches the language, remembers it, and tells the page (and so screen readers and the browser's own translation) what it is now. */
  setLanguage(language: UiLanguage): void {
    this.language.set(language);
    this.document.documentElement.lang = language;
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // A blocked store only means the choice is not remembered; it still applies to this visit.
    }
  }

  /**
   * A message in the current language. A message missing from it (only possible for a key added after a language was written, which the
   * types prevent) falls back to English rather than showing a key.
   */
  readonly t: Translate = (key, params) => fill(MESSAGES[this.language()][key] ?? EN[key], params);

  /**
   * A message whose wording depends on a count: `base.one` for exactly one, `base.other` for anything else. The count is also passed
   * as `{count}`.
   */
  plural(base: PluralKey, count: number, params: MessageParams = {}): string {
    return this.t(pluralKey(base, count), { count, ...params });
  }

  /** What was chosen before if it is still offered, else the first language the browser lists that is, else the default. */
  private initialLanguage(): UiLanguage {
    try {
      const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
      if (isUiLanguage(stored)) {
        return stored;
      }
    } catch {
      // Blocked storage is the same as nothing stored.
    }

    for (const tag of this.document.defaultView?.navigator.languages ?? []) {
      const code = tag.split('-')[0].toLowerCase();
      if (isUiLanguage(code)) {
        return code;
      }
    }

    return DEFAULT_LANGUAGE;
  }
}
