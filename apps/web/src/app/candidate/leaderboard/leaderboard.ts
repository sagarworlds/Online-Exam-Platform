import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { LeaderboardBoardKey, LeaderboardDto, LeaderboardEntryDto } from '../candidate.models';

/** What the leaderboard page shows at any moment: being read, shown, held until the results are released, or unreadable (retry). */
export type LeaderboardView = 'loading' | 'ready' | 'held' | 'error';

/**
 * An exam's leaderboards (FR-35): the overall board, the board of the batch the candidate is in, and the board of one subject. Every board uses
 * the released results and the same ranking. Names are shortened by the API, and the candidate's own row is marked.
 */
@Component({
  selector: 'app-leaderboard',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './leaderboard.html',
  styleUrl: './leaderboard.css',
})
export class Leaderboard {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('examId');

  protected readonly view = signal<LeaderboardView>('loading');
  protected readonly data = signal<LeaderboardDto | null>(null);
  protected readonly board = signal<LeaderboardBoardKey>('overall');
  /** The batch the batch board shows; the API's choice once it has answered, so the selector always shows what is listed. */
  protected readonly batchId = signal<string | null>(null);
  /** The subject the subject board shows; the API's choice once it has answered. */
  protected readonly subject = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    if (this.examId === null) {
      this.errorMessage.set(this.i18n.t('leaderboard.noExam'));
      this.view.set('error');
      return;
    }
    this.load();
  }

  /** Shows one of the three boards. */
  protected chooseBoard(board: LeaderboardBoardKey): void {
    if (board === this.board()) {
      return;
    }
    this.board.set(board);
    this.load();
  }

  /** Shows the batch board for another batch the candidate is in. */
  protected chooseBatch(id: string): void {
    this.batchId.set(id);
    this.load();
  }

  /** Shows the subject board for another subject of the exam. */
  protected chooseSubject(name: string): void {
    this.subject.set(name);
    this.load();
  }

  protected retry(): void {
    this.load();
  }

  /** The candidate's own row: from the places listed, or from the row kept apart when it falls outside them. */
  protected ownRow(board: LeaderboardDto): LeaderboardEntryDto | null {
    return board.entries.find((entry) => entry.isYou) ?? board.you;
  }

  /** The listed places, then the candidate's own row when it sits outside them. */
  protected rowsOf(board: LeaderboardDto): LeaderboardEntryDto[] {
    return board.you ? [...board.entries, board.you] : board.entries;
  }

  private load(): void {
    const examId = this.examId;
    if (examId === null) {
      return;
    }

    this.view.set('loading');
    this.errorMessage.set(null);
    const board = this.board();
    this.api
      .getLeaderboard(
        examId,
        board,
        board === 'batch' ? this.batchId() : null,
        board === 'subject' ? this.subject() : null,
      )
      .subscribe({
        next: (result) => {
          this.data.set(result);
          this.batchId.set(result.batchId);
          this.subject.set(result.subject);
          this.view.set('ready');
        },
        error: (error: unknown) => {
          // Held results are an expected state, shown as such rather than as a failure.
          if (extractProblemCode(error) === 'results_not_released') {
            this.view.set('held');
            return;
          }
          console.error('Reading the leaderboard failed', error);
          this.errorMessage.set(extractErrorMessage(error, this.i18n.t('leaderboard.loadError')));
          this.view.set('error');
        },
      });
  }
}
