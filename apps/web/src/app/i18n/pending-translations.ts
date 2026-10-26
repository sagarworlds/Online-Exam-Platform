import type { MessageKey } from './messages.en';

/**
 * Messages whose Hindi and Marathi are still the English text, because the translation is waiting for a native speaker to write or review it.
 * They read in English in those languages meanwhile. The parity spec does not count them as untranslated, and fails when a key listed here
 * has been translated, so the list only ever shrinks. Add a key only together with its English placeholder in messages.hi.ts and messages.mr.ts.
 */
export const PENDING_TRANSLATION: readonly MessageKey[] = [
  'qb.field.lenient',
  'qb.field.lenientHint',
  'qb.field.lenientLock',
  'qb.key.lenient',
  'qb.key.lenientHint',
];
