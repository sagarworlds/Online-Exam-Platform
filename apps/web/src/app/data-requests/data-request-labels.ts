import { MessageKey } from '../i18n/messages.en';
import { DataRequestKind, DataRequestStatus } from './data-request.models';

/** What each kind of request is called in a list, for the person who made it and for staff alike. */
export const KIND_KEYS: Readonly<Record<DataRequestKind, MessageKey>> = {
  Access: 'dataRequests.kind.Access',
  Correction: 'dataRequests.kind.Correction',
  Erasure: 'dataRequests.kind.Erasure',
};

/** Where each request stands, in words: a badge never relies on its colour alone. */
export const STATUS_KEYS: Readonly<Record<DataRequestStatus, MessageKey>> = {
  Received: 'dataRequests.status.Received',
  Completed: 'dataRequests.status.Completed',
  Rejected: 'dataRequests.status.Rejected',
};

/** What each kind is called as a choice on the candidate's form: in the candidate's own voice. */
export const CHOICE_KEYS: Readonly<Record<DataRequestKind, MessageKey>> = {
  Access: 'dataRequests.mine.kind.Access',
  Correction: 'dataRequests.mine.kind.Correction',
  Erasure: 'dataRequests.mine.kind.Erasure',
};

/** One line under each choice saying what asking for it leads to. */
export const CHOICE_HELP_KEYS: Readonly<Record<DataRequestKind, MessageKey>> = {
  Access: 'dataRequests.mine.help.Access',
  Correction: 'dataRequests.mine.help.Correction',
  Erasure: 'dataRequests.mine.help.Erasure',
};
