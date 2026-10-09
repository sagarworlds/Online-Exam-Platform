import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subscription, catchError, exhaustMap, take, takeWhile, timer } from 'rxjs';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { WhatsAppTestApiService } from './whatsapp-test-api.service';
import {
  WhatsAppDeliveryDto,
  WhatsAppDeliveryStatus,
  WhatsAppFailureDto,
  WhatsAppSendMode,
  WhatsAppSendRequest,
  WhatsAppSendResultDto,
  WhatsAppStatusDto,
  failureTitle,
} from './whatsapp-test.models';

/** The longest message the API accepts in `Text` mode. */
export const WHATSAPP_MESSAGE_MAX_LENGTH = 1000;
/** How often the delivery report is asked for after a send. */
export const WHATSAPP_POLL_INTERVAL_MS = 3000;
/** How long to wait for a delivery report before telling the administrator to check the phone. */
export const WHATSAPP_POLL_TIMEOUT_MS = 90_000;

/** The steps of a delivery, in the order WhatsApp reports them, each with the key of its word. */
const DELIVERY_STEPS: readonly { status: WhatsAppDeliveryStatus; label: MessageKey }[] = [
  { status: 'sent', label: 'whatsapp.step.sent' },
  { status: 'delivered', label: 'whatsapp.step.delivered' },
  { status: 'read', label: 'whatsapp.step.read' },
];

/** One step of the progress line: a symbol and, for the steps not still ahead, words, so colour is never the only cue. */
interface DeliveryStepView {
  label: string;
  state: 'done' | 'current' | 'todo';
  mark: string;
  spoken: string;
}

/** A delivery report after which nothing more will change, so polling can stop. */
const isFinal = (status: WhatsAppDeliveryStatus | undefined): boolean =>
  status === 'delivered' || status === 'read' || status === 'failed';

/**
 * Admin page: send one message through the platform's WhatsApp connection to check that WhatsApp works. The status card at the
 * top says whether WhatsApp is on and what, if anything, is stopping it; the settings are one tap away. When a send does not
 * work, the page shows the server's accurate reason, and after a send it follows the delivery report (sent, delivered, read, or
 * failed) because WhatsApp accepts a message long before it reaches the phone.
 */
@Component({
  selector: 'app-whatsapp-test',
  imports: [TranslatePipe],
  templateUrl: './whatsapp-test.html',
})
export class WhatsAppTest implements OnInit {
  private readonly api = inject(WhatsAppTestApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private polling: Subscription | null = null;

  protected readonly maxLength = WHATSAPP_MESSAGE_MAX_LENGTH;

  protected readonly status = signal<WhatsAppStatusDto | null>(null);
  protected readonly statusLoading = signal(false);
  protected readonly statusError = signal<string | null>(null);

  protected readonly phone = signal('');
  protected readonly mode = signal<WhatsAppSendMode>('Text');
  protected readonly message = signal('');
  /** Whether Send has been pressed. The fields are judged only from then on, as on the other forms, so nothing is named before it is tried. */
  protected readonly attempted = signal(false);
  protected readonly sending = signal(false);

  protected readonly sendError = signal<string | null>(null);
  protected readonly sendResult = signal<WhatsAppSendResultDto | null>(null);
  protected readonly delivery = signal<WhatsAppDeliveryDto | null>(null);
  protected readonly timedOut = signal(false);

  /** The phone number's problem, named once Send has been pressed. Spaces alone are no number. */
  protected readonly phoneError = computed<MessageKey | null>(() =>
    this.attempted() && this.phone().trim() === '' ? 'whatsapp.form.phoneRequired' : null,
  );

  /** The message's problem, named once Send has been pressed. Only a text message needs one: the sign-in template sends its own words. */
  protected readonly messageError = computed<MessageKey | null>(() =>
    this.attempted() && this.mode() === 'Text' && this.message().trim() === ''
      ? 'whatsapp.form.messageRequired'
      : null,
  );

  /** Why nothing arrived: the send itself failed, or a delivery report later said the message failed. */
  protected readonly failure = computed<WhatsAppFailureDto | null>(() => {
    const result = this.sendResult();
    if (result === null) {
      return null;
    }
    if (!result.sent) {
      return result.failure ?? this.unexplainedFailure();
    }

    const delivery = this.delivery();
    return delivery?.status === 'failed' ? (delivery.failure ?? this.unexplainedFailure()) : null;
  });

  /** True when delivery reports can be followed for the message that was handed over. */
  protected readonly tracking = computed(() => {
    const result = this.sendResult();
    return result !== null && result.sent && result.deliveryTracking && !!result.messageId;
  });

  /** Sent, Delivered, Read, with the one the latest report names marked current. Nothing is current before the first report. */
  protected readonly steps = computed<DeliveryStepView[]>(() => {
    const current = DELIVERY_STEPS.findIndex((step) => step.status === this.delivery()?.status);
    return DELIVERY_STEPS.map((step, index): DeliveryStepView => {
      const label = this.i18n.t(step.label);
      if (index < current) {
        return { label, state: 'done', mark: '✓', spoken: this.i18n.t('whatsapp.step.done') };
      }
      return index === current
        ? { label, state: 'current', mark: '●', spoken: this.i18n.t('whatsapp.step.current') }
        : { label, state: 'todo', mark: '○', spoken: '' };
    });
  });

  protected readonly awaitingReport = computed(
    () => this.tracking() && !isFinal(this.delivery()?.status) && !this.timedOut(),
  );

  protected readonly timeoutNote = computed<string | null>(() => {
    if (!this.timedOut()) {
      return null;
    }
    return this.i18n.t(
      this.delivery()?.status === 'sent' ? 'whatsapp.timeout.sentOnly' : 'whatsapp.timeout.none',
    );
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  ngOnInit(): void {
    this.loadStatus();
  }

  /** The heading for a failure kind, in the language chosen; see {@link failureTitle} for kinds this build does not know. */
  protected failureHeading(kind: string): string {
    return failureTitle(kind, this.i18n.t);
  }

  /** How many things are stopping WhatsApp, in words, such as "2 things are stopping WhatsApp from sending." */
  protected problemCount(count: number): string {
    return this.i18n.plural('whatsapp.status.problems', count);
  }

  protected loadStatus(): void {
    this.statusLoading.set(true);
    this.statusError.set(null);
    this.api
      .status()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => {
          this.status.set(status);
          this.statusLoading.set(false);
        },
        error: (error: unknown) => {
          // Old facts about the set-up would mislead next to a failed refresh, so they go.
          this.status.set(null);
          this.statusLoading.set(false);
          this.statusError.set(this.describe(error));
        },
      });
  }

  protected send(): void {
    this.attempted.set(true);
    if (this.phoneError() !== null || this.messageError() !== null || this.sending()) {
      return;
    }

    this.clearOutcome();
    this.sending.set(true);

    const phoneNumber = this.phone().trim();
    const request: WhatsAppSendRequest =
      this.mode() === 'Text'
        ? { phoneNumber, mode: 'Text', message: this.message().trim() }
        : { phoneNumber, mode: 'SignInTemplate' };

    this.api
      .send(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.sending.set(false);
          this.sendResult.set(result);
          if (result.sent && result.deliveryTracking && result.messageId) {
            this.startPolling(result.messageId);
          }
        },
        error: (error: unknown) => {
          this.sending.set(false);
          this.sendError.set(this.describe(error));
        },
      });
  }

  protected hasMetaDetails(failure: WhatsAppFailureDto): boolean {
    return failure.metaCode !== null || failure.httpStatus !== null || !!failure.metaMessage;
  }

  /** Asks for the delivery report every few seconds until it is final or the wait runs out; one request at a time. */
  private startPolling(messageId: string): void {
    this.polling = timer(WHATSAPP_POLL_INTERVAL_MS, WHATSAPP_POLL_INTERVAL_MS)
      .pipe(
        take(WHATSAPP_POLL_TIMEOUT_MS / WHATSAPP_POLL_INTERVAL_MS),
        // A report that cannot be fetched this time is not an answer; the next tick tries again.
        exhaustMap(() => this.api.delivery(messageId).pipe(catchError(() => EMPTY))),
        takeWhile((delivery) => !isFinal(delivery.status), true),
      )
      .subscribe({
        next: (delivery) => this.delivery.set(delivery),
        complete: () => this.timedOut.set(!isFinal(this.delivery()?.status)),
      });
  }

  private stopPolling(): void {
    this.polling?.unsubscribe();
    this.polling = null;
  }

  private clearOutcome(): void {
    this.stopPolling();
    this.sendError.set(null);
    this.sendResult.set(null);
    this.delivery.set(null);
    this.timedOut.set(false);
  }

  /** What to show when the server says a message failed but gives no reason, so the page never shows a blank failure. */
  private unexplainedFailure(): WhatsAppFailureDto {
    return {
      kind: 'Rejected',
      explanation: this.i18n.t('whatsapp.failure.unexplained'),
      metaCode: null,
      metaMessage: null,
      httpStatus: null,
    };
  }

  /** The API's own sentence when it gave one; for the two cases a person can act on, a fallback that says what to do. */
  private describe(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return extractErrorMessage(error, this.i18n.t('whatsapp.error.notAllowed'));
      }
      if (extractProblemCode(error) === 'invalid_whatsapp_message') {
        return extractErrorMessage(error, this.i18n.t('whatsapp.error.badRequest'));
      }
    }

    return extractErrorMessage(error, this.i18n.t('common.somethingWrong'));
  }
}
