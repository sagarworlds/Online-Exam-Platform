import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subscription, catchError, exhaustMap, take, takeWhile, timer } from 'rxjs';
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

const NOT_ALLOWED = 'You are not allowed to use the WhatsApp test.';
const BAD_REQUEST = 'Check the phone number and the message, then try again.';

/** Shown when the server says a message failed but not why, so the page never shows a blank failure. */
const UNEXPLAINED_FAILURE: WhatsAppFailureDto = {
  kind: 'Rejected',
  explanation: 'WhatsApp did not deliver the message, and gave no reason. Check the phone, and check the setup above.',
  metaCode: null,
  metaMessage: null,
  httpStatus: null,
};

/** The steps of a delivery, in the order WhatsApp reports them. */
const DELIVERY_STEPS: readonly { status: WhatsAppDeliveryStatus; label: string }[] = [
  { status: 'sent', label: 'Sent' },
  { status: 'delivered', label: 'Delivered' },
  { status: 'read', label: 'Read' },
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
 * Admin page: send one message through the platform's WhatsApp connection to check that WhatsApp works. The setup check
 * shows which settings are filled in; when a send does not work, the page shows the server's accurate reason, and after a
 * send it follows the delivery report (sent, delivered, read, or failed) because WhatsApp accepts a message long before it
 * reaches the phone.
 */
@Component({
  selector: 'app-whatsapp-test',
  templateUrl: './whatsapp-test.html',
})
export class WhatsAppTest implements OnInit {
  private readonly api = inject(WhatsAppTestApiService);
  private readonly destroyRef = inject(DestroyRef);
  private polling: Subscription | null = null;

  protected readonly maxLength = WHATSAPP_MESSAGE_MAX_LENGTH;
  protected readonly failureTitle = failureTitle;

  protected readonly status = signal<WhatsAppStatusDto | null>(null);
  protected readonly statusLoading = signal(false);
  protected readonly statusError = signal<string | null>(null);

  protected readonly phone = signal('');
  protected readonly mode = signal<WhatsAppSendMode>('Text');
  protected readonly message = signal('');
  protected readonly sending = signal(false);

  protected readonly sendError = signal<string | null>(null);
  protected readonly sendResult = signal<WhatsAppSendResultDto | null>(null);
  protected readonly delivery = signal<WhatsAppDeliveryDto | null>(null);
  protected readonly timedOut = signal(false);

  protected readonly canSend = computed(
    () => !this.sending() && this.phone().trim() !== '' && (this.mode() === 'SignInTemplate' || this.message().trim() !== ''),
  );

  /** Why nothing arrived: the send itself failed, or a delivery report later said the message failed. */
  protected readonly failure = computed<WhatsAppFailureDto | null>(() => {
    const result = this.sendResult();
    if (result === null) {
      return null;
    }
    if (!result.sent) {
      return result.failure ?? UNEXPLAINED_FAILURE;
    }

    const delivery = this.delivery();
    return delivery?.status === 'failed' ? (delivery.failure ?? UNEXPLAINED_FAILURE) : null;
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
      if (index < current) {
        return { label: step.label, state: 'done', mark: '✓', spoken: 'done' };
      }
      return index === current
        ? { label: step.label, state: 'current', mark: '●', spoken: 'current step' }
        : { label: step.label, state: 'todo', mark: '○', spoken: '' };
    });
  });

  protected readonly awaitingReport = computed(() => this.tracking() && !isFinal(this.delivery()?.status) && !this.timedOut());

  protected readonly timeoutNote = computed(() => {
    if (!this.timedOut()) {
      return null;
    }
    return this.delivery()?.status === 'sent'
      ? 'WhatsApp reports the message as sent, but not yet as delivered. Check the phone.'
      : 'No delivery report yet. Check the phone.';
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  ngOnInit(): void {
    this.loadStatus();
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
    if (!this.canSend()) {
      return;
    }

    this.clearOutcome();
    this.sending.set(true);

    const phoneNumber = this.phone().trim();
    const request: WhatsAppSendRequest =
      this.mode() === 'Text' ? { phoneNumber, mode: 'Text', message: this.message().trim() } : { phoneNumber, mode: 'SignInTemplate' };

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

  /** The API's own sentence when it gave one; for the two cases a person can act on, a fallback that says what to do. */
  private describe(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return extractErrorMessage(error, NOT_ALLOWED);
      }
      if (extractProblemCode(error) === 'invalid_whatsapp_message') {
        return extractErrorMessage(error, BAD_REQUEST);
      }
    }

    return extractErrorMessage(error);
  }
}
