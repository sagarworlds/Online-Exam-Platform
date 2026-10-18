import { DatePipe } from '@angular/common';
import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { DataRequestApiService } from '../data-request-api.service';
import { CHOICE_HELP_KEYS, CHOICE_KEYS, KIND_KEYS, STATUS_KEYS } from '../data-request-labels';
import {
  DATA_REQUEST_KINDS,
  DataRequestDto,
  DataRequestKind,
  MAX_DATA_REQUEST_DETAILS,
} from '../data-request.models';

/** The requests newest first, whatever order the API gave them in. */
function newestFirst(requests: readonly DataRequestDto[]): DataRequestDto[] {
  return [...requests].sort((a, b) => b.receivedAtUtc.localeCompare(a.receivedAtUtc));
}

/**
 * The candidate's data requests (FR-48): ask for a copy of their data, a correction or erasure, and read what became of each request.
 *
 * Staff answer a request by hand, so the page says what is asked and shows the answer as staff wrote it; it never claims that anything
 * was sent, changed or removed. A kind that already has an open request cannot be asked for again until it is answered, which is the
 * API's rule too (`409 data_request_already_open`); the page shows the rule up front and still copes when the API refuses.
 *
 * All the state a template reads is in signals, because the app runs without zone.js and a plain field would never be redrawn.
 */
@Component({
  selector: 'app-my-data-requests',
  imports: [DatePipe, TranslatePipe],
  templateUrl: './my-data-requests.html',
  styleUrl: './my-data-requests.css',
})
export class MyDataRequests {
  private readonly api = inject(DataRequestApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /** The line that says the request was sent: focus moves to it, since the form's button the person pressed is no longer where they were. */
  private readonly sentMessage = viewChild<ElementRef<HTMLElement>>('sentMessage');

  protected readonly kinds = DATA_REQUEST_KINDS;
  protected readonly maxDetails = MAX_DATA_REQUEST_DETAILS;
  protected readonly kindKeys = KIND_KEYS;
  protected readonly statusKeys = STATUS_KEYS;
  protected readonly choiceKeys = CHOICE_KEYS;
  protected readonly choiceHelpKeys = CHOICE_HELP_KEYS;

  protected readonly requests = signal<DataRequestDto[]>([]);
  protected readonly loading = signal(true);
  /** Why the list could not be read; the form stays, since sending does not depend on the list. */
  protected readonly loadError = signal<string | null>(null);

  protected readonly kind = signal<DataRequestKind | null>(null);
  protected readonly details = signal('');
  /** Whether Send has been pressed; a missing choice is named only from then on, as on the other forms. */
  protected readonly attempted = signal(false);
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);
  /** The request that was just sent, for the line that says so and when it is due. */
  protected readonly sent = signal<DataRequestDto | null>(null);

  /** The kinds that have a request waiting for an answer, which cannot be asked for again meanwhile. */
  protected readonly openKinds = computed(
    () => new Set(this.requests().filter((request) => request.status === 'Received').map((request) => request.kind)),
  );
  protected readonly allOpen = computed(() => this.kinds.every((kind) => this.openKinds().has(kind)));
  protected readonly kindMissing = computed(() => this.attempted() && this.kind() === null);

  constructor() {
    this.load();
  }

  /** Chooses a kind. What was said about the last request no longer applies to the one being written, so it goes. */
  protected choose(kind: DataRequestKind): void {
    this.kind.set(kind);
    this.sent.set(null);
    this.sendError.set(null);
  }

  protected setDetails(value: string): void {
    this.details.set(value);
    this.sent.set(null);
    this.sendError.set(null);
  }

  protected submit(): void {
    this.attempted.set(true);
    const kind = this.kind();
    if (kind === null || this.sending()) {
      return;
    }

    this.sending.set(true);
    this.sendError.set(null);
    this.sent.set(null);
    // The API trims what is written and treats nothing as none; send it that way so what is shown matches what is kept.
    const details = this.details().trim();

    this.api.raise({ kind, details: details === '' ? null : details }).subscribe({
      next: (created) => {
        this.sending.set(false);
        this.requests.update((current) => newestFirst([created, ...current]));
        this.kind.set(null);
        this.details.set('');
        this.attempted.set(false);
        this.sent.set(created);
        afterNextRender(() => this.sentMessage()?.nativeElement.focus(), { injector: this.injector });
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.sendError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
        // The page was out of date: a request of this kind is open already. Read the list again so the choice shows as taken.
        if (extractProblemCode(error) === 'data_request_already_open') {
          this.load();
        }
      },
    });
  }

  private load(): void {
    this.api.listMine().subscribe({
      next: (requests) => {
        this.requests.set(newestFirst(requests));
        this.loadError.set(null);
        this.loading.set(false);
        // A choice that has just become taken is put back, so the person is never left holding one they cannot send.
        const chosen = this.kind();
        if (chosen !== null && this.openKinds().has(chosen)) {
          this.kind.set(null);
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }
}
