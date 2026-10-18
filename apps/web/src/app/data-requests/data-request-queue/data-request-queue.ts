import { DatePipe } from '@angular/common';
import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChildren,
} from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { DataRequestApiService } from '../data-request-api.service';
import { KIND_KEYS } from '../data-request-labels';
import {
  DataRequestOutcome,
  MAX_DATA_REQUEST_NOTE,
  OpenDataRequestDto,
} from '../data-request.models';

/** What a request in this visit ended as: one of the two answers staff gave, or gone from the queue because someone else got there first. */
type Answer = DataRequestOutcome | 'Elsewhere';

/** The API's reasons for a request that is no longer there to answer: it was answered already, or is gone. */
const NO_LONGER_WAITING = ['data_request_not_open', 'data_request_not_found'];

/** The answer form that is open: which request it is for, and which of the two answers it records. */
interface Answering {
  id: string;
  outcome: DataRequestOutcome;
}

/**
 * Staff's queue of data requests (FR-48): everything waiting for an answer, the one due soonest first, so the overdue ones are at the top.
 *
 * The platform does not send a copy, change a record or erase anything. Staff do that by hand, then record here that it is done, or
 * that it is refused and why; the person who asked sees the note. A refusal must say why, because the person is owed a reason, and the
 * API insists on it too.
 *
 * An answer does not take the request off the page. Its card becomes a line saying what was recorded, in the same place, and focus moves
 * to it. The lines stay until the page is left or reloaded, so the staff member can check what they did.
 *
 * All the state a template reads is in signals, because the app runs without zone.js and a plain field would never be redrawn.
 */
@Component({
  selector: 'app-data-request-queue',
  imports: [DatePipe, TranslatePipe],
  templateUrl: './data-request-queue.html',
  styleUrl: './data-request-queue.css',
})
export class DataRequestQueue {
  private readonly api = inject(DataRequestApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /** The elements focus can be moved to, each marked with its `data-focus` key, so focus lands where the person was working. */
  private readonly focusTargets = viewChildren<ElementRef<HTMLElement>>('focusTarget');

  protected readonly kindKeys = KIND_KEYS;
  protected readonly maxNote = MAX_DATA_REQUEST_NOTE;

  protected readonly requests = signal<OpenDataRequestDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  /** The answer given to each request in this visit, by request id; its line stands where its card was until the page is left. */
  protected readonly answers = signal<Readonly<Record<string, Answer>>>({});
  /** The request a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a request failed, by request id, so the reason shows on that request. */
  protected readonly rowErrors = signal<Readonly<Record<string, string>>>({});

  /** The answer form that is open; one at a time, so a stray click cannot answer two. */
  protected readonly answering = signal<Answering | null>(null);
  protected readonly note = signal('');
  /** Whether Refuse was pressed with no reason; the reason is named as missing from then on. */
  protected readonly noteMissing = signal(false);

  /** How many requests still wait: the ones read, less the ones answered in this visit. */
  protected readonly waiting = computed(() => this.requests().filter((request) => this.answers()[request.id] === undefined).length);

  constructor() {
    this.api.listOpen().subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** How many requests still wait, in words. */
  protected waitingText(): string {
    return this.i18n.plural('admin.dataRequests.waiting', this.waiting());
  }

  /** The id of a request's heading, which its card names itself by. */
  protected headingId(request: OpenDataRequestDto): string {
    return `data-request-${request.id}`;
  }

  /** What to call the person in a sentence: the address first, since that is where an answer goes, then the name, then the number. */
  protected who(request: OpenDataRequestDto): string {
    return (
      request.requesterEmail ||
      request.requesterName ||
      request.requesterPhoneNumber ||
      this.i18n.t('admin.dataRequests.requesterFallback', { id: request.userId })
    );
  }

  /** What the API told about the person: their name, address and number, those they have. */
  protected contact(request: OpenDataRequestDto): string[] {
    return [request.requesterName, request.requesterEmail, request.requesterPhoneNumber].filter(
      (part): part is string => !!part,
    );
  }

  /** Whether there is an address or a number to answer the person at. A name alone is no way to send them a copy. */
  protected canReach(request: OpenDataRequestDto): boolean {
    return !!request.requesterEmail || !!request.requesterPhoneNumber;
  }

  /** Whether the API answered with the person's details at all; an older API does not, and then there is nothing to say about them. */
  protected contactReported(request: OpenDataRequestDto): boolean {
    return (
      request.requesterName !== undefined ||
      request.requesterEmail !== undefined ||
      request.requesterPhoneNumber !== undefined
    );
  }

  /** The line that stands for a request answered here: what was recorded, and for whom. */
  protected resultText(request: OpenDataRequestDto, answer: Answer): string {
    const kind = this.i18n.t(KIND_KEYS[request.kind]);
    const who = this.who(request);
    switch (answer) {
      case 'Completed':
        return this.i18n.t('admin.dataRequests.completedResult', { kind, who });
      case 'Rejected':
        return this.i18n.t('admin.dataRequests.rejectedResult', { kind, who });
      case 'Elsewhere':
        return this.i18n.t('admin.dataRequests.answeredElsewhere', { kind, who });
    }
  }

  /** The symbol that stands before a result line, repeating in a mark what the words and the edge colour say. */
  protected symbol(answer: Answer): string {
    return answer === 'Completed' ? '✓' : answer === 'Rejected' ? '✗' : '–';
  }

  /** Whether the answer form of this request is the one that is open. */
  protected isAnswering(request: OpenDataRequestDto): Answering | null {
    const open = this.answering();
    return open !== null && open.id === request.id ? open : null;
  }

  protected startAnswering(request: OpenDataRequestDto, outcome: DataRequestOutcome): void {
    this.note.set('');
    this.noteMissing.set(false);
    this.answering.set({ id: request.id, outcome });
    this.focusLater(`note:${request.id}`);
  }

  /** Puts the form away without answering, and gives focus back to the button that opened it. */
  protected cancel(request: OpenDataRequestDto, outcome: DataRequestOutcome): void {
    this.answering.set(null);
    this.focusLater(`${outcome === 'Completed' ? 'complete' : 'reject'}:${request.id}`);
  }

  protected confirm(request: OpenDataRequestDto, outcome: DataRequestOutcome): void {
    if (this.busyId() !== null) {
      return;
    }

    const note = this.note().trim();
    if (outcome === 'Rejected' && note === '') {
      this.noteMissing.set(true);
      this.focusLater(`note:${request.id}`);
      return;
    }

    this.busyId.set(request.id);
    this.rowErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([id]) => id !== request.id)));
    this.api.resolve(request.id, { outcome, note: note === '' ? null : note }).subscribe({
      next: () => {
        this.busyId.set(null);
        this.answering.set(null);
        this.answers.update((current) => ({ ...current, [request.id]: outcome }));
        this.focusLater(`result:${request.id}`);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        // Someone else answered it first, or it is gone: there is nothing left here to answer, so it becomes a line saying so.
        if (NO_LONGER_WAITING.includes(extractProblemCode(error) ?? '')) {
          this.answering.set(null);
          this.answers.update((current) => ({ ...current, [request.id]: 'Elsewhere' }));
          this.focusLater(`result:${request.id}`);
          return;
        }
        this.rowErrors.update((errors) => ({
          ...errors,
          [request.id]: extractErrorMessage(error, this.i18n.t('common.somethingWrong')),
        }));
      },
    });
  }

  /**
   * Moves focus to the element marked with this key once the change is drawn. The change removes or replaces the element that had
   * focus, so without this the focus would fall back to the top of the page.
   */
  private focusLater(key: string): void {
    afterNextRender(
      () =>
        this.focusTargets()
          .find((target) => target.nativeElement.getAttribute('data-focus') === key)
          ?.nativeElement.focus(),
      { injector: this.injector },
    );
  }
}
