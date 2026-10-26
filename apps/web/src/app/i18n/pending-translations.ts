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
  'reset.codeFromLink',
  'reset.codeByHand',
  'inviteDirect.title',
  'inviteDirect.signing',
  'inviteDirect.missing',
  'inviteDirect.invalid',
  'inviteDirect.locked',
  'inviteDirect.normalSignIn',
  'inviteDirect.disabled',
  'inviteDirect.busy',
  'inviteDirect.network',
  'inviteDirect.retry',
  'inviteDirect.signIn',
  'invites.create.direct',
  'invites.create.directHint',
  'invites.create.directLinkReady',
  'invites.handover.directOnce',
  'students.invite.directBadge',
  'students.invite.copyDirect',
  'students.invite.copyDirectFor',
  'exams.picker.toolbar',
  'exams.picker.selectAll',
  'exams.picker.clearSelection',
  'exams.picker.selectedCount',
  'exams.picker.addSelected',
  'exams.picker.addSelectedNamed',
  'exams.picker.selectNamed',
];
