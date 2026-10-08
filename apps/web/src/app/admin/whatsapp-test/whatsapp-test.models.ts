/** One setting the WhatsApp connection reads, and whether it is filled in (secrets are never sent, only whether they are set). */
export interface WhatsAppSettingDto {
  /** The configuration key, for example `WhatsApp__AccessToken`. */
  setting: string;
  isSet: boolean;
  required: boolean;
  purpose: string;
  /** The setting's value, only for non-secret settings (such as the template name); null otherwise. */
  value: string | null;
}

/** What the server knows about the platform's WhatsApp set-up, with the reasons it cannot send when it cannot. */
export interface WhatsAppStatusDto {
  /** The master switch, `WhatsApp__Enabled`. */
  enabled: boolean;
  /** Enabled, and the access token and phone number id are set. */
  canSendMessages: boolean;
  /** Can send messages, and the sign-in template name is set. */
  canSendTemplate: boolean;
  /** The webhook is configured, so delivery reports (sent, delivered, read, failed) come back. */
  canTrackDelivery: boolean;
  /** Phone sign-in codes are routed to WhatsApp. */
  signInCodesUseWhatsApp: boolean;
  /** Invitations also send the exam code on WhatsApp. */
  inviteCodesUseWhatsApp: boolean;
  settings: WhatsAppSettingDto[];
  /** Blockers, as plain sentences. */
  problems: string[];
  /** Optional things worth knowing. */
  notes: string[];
}

/** Which kind of message to send: free text, or the approved sign-in code template. */
export type WhatsAppSendMode = 'Text' | 'SignInTemplate';

export interface WhatsAppSendRequest {
  phoneNumber: string;
  mode: WhatsAppSendMode;
  /** Only sent in `Text` mode. */
  message?: string;
}

/** Why a message could not be sent or delivered, as the server worked it out. */
export type WhatsAppFailureKind =
  | 'SwitchedOff'
  | 'NotConfigured'
  | 'InvalidNumber'
  | 'InvalidToken'
  | 'PermissionDenied'
  | 'WrongPhoneNumberId'
  | 'WrongApiAddress'
  | 'InvalidRequest'
  | 'InvalidRecipient'
  | 'TemplateNotFound'
  | 'TemplateUnavailable'
  | 'TemplateMismatch'
  | 'RecipientNotAllowed'
  | 'ReEngagementRequired'
  | 'NotOnWhatsApp'
  | 'NumberNotRegistered'
  | 'PaymentProblem'
  | 'AccountRestricted'
  | 'RateLimited'
  | 'ServiceUnavailable'
  | 'Unreachable'
  | 'TimedOut'
  | 'Rejected';

export interface WhatsAppFailureDto {
  kind: WhatsAppFailureKind;
  /** A sentence saying what is wrong and what to do about it. */
  explanation: string;
  /** What Meta itself answered, when it answered. */
  metaCode: number | null;
  metaMessage: string | null;
  httpStatus: number | null;
}

export interface WhatsAppSendResultDto {
  sent: boolean;
  messageId: string | null;
  /** The recipient with all but the last digits masked, for example `********10`. */
  to: string | null;
  mode: WhatsAppSendMode;
  /** True when delivery reports will come back, so {@link WhatsAppDeliveryDto} is worth polling. */
  deliveryTracking: boolean;
  note: string | null;
  failure: WhatsAppFailureDto | null;
}

/** `NotReported` is the state before Meta has reported anything; the others are Meta's own words. */
export type WhatsAppDeliveryStatus = 'NotReported' | 'sent' | 'delivered' | 'read' | 'failed';

export interface WhatsAppDeliveryDto {
  messageId: string;
  status: WhatsAppDeliveryStatus;
  recipient: string | null;
  updatedAtUtc: string | null;
  /** Present when the status is `failed`. */
  failure: WhatsAppFailureDto | null;
}

/** The heading for each failure, in words an administrator would use. Typed so a new kind cannot be forgotten. */
const FAILURE_TITLES: Record<WhatsAppFailureKind, string> = {
  SwitchedOff: 'WhatsApp is switched off',
  NotConfigured: 'WhatsApp is not set up',
  InvalidNumber: 'Invalid phone number',
  InvalidToken: 'Access token rejected',
  PermissionDenied: 'Permission denied',
  WrongPhoneNumberId: 'Wrong phone number ID',
  WrongApiAddress: 'Wrong WhatsApp address',
  InvalidRequest: 'Invalid request',
  InvalidRecipient: 'Invalid recipient',
  TemplateNotFound: 'Template not found',
  TemplateUnavailable: 'Template unavailable',
  TemplateMismatch: 'Template does not match',
  RecipientNotAllowed: 'Recipient not allowed',
  ReEngagementRequired: 'Re-engagement required',
  NotOnWhatsApp: 'Not on WhatsApp',
  NumberNotRegistered: 'Sender number not registered',
  PaymentProblem: 'Payment problem',
  AccountRestricted: 'Account restricted',
  RateLimited: 'Rate limited',
  ServiceUnavailable: 'WhatsApp service unavailable',
  Unreachable: 'WhatsApp unreachable',
  TimedOut: 'Timed out',
  Rejected: 'Rejected by WhatsApp',
};

/**
 * The heading to show for a failure kind. A kind this build does not know (the server may be newer than the page) is
 * still shown readably, by splitting its name into words ("SomeNewKind" becomes "Some new kind").
 */
export function failureTitle(kind: string): string {
  const known = (FAILURE_TITLES as Record<string, string | undefined>)[kind];
  if (known) {
    return known;
  }

  const words = kind
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : 'Not sent';
}
