import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ExamDto } from '../exam.models';
import { describeScope } from '../exam-scope-fields/exam-scope';

/** Admin page: the newest exams, each linking to its editor. */
@Component({
  selector: 'app-exam-list',
  imports: [RouterLink, DatePipe, RefreshButton],
  templateUrl: './exam-list.html',
})
export class ExamList {
  private readonly examApi = inject(ExamApiService);

  protected readonly exams = signal<ExamDto[]>([]);
  protected readonly loading = signal(true);
  /** Whether the list is being read again because the refresh icon was pressed; the exams stay until the answer comes. */
  protected readonly refreshing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly describeScope = describeScope;

  constructor() {
    this.read();
  }

  /** Reads the exams again, so a change made elsewhere shows without reloading the tab. A failed read leaves the list and says why. */
  protected onRefresh(): void {
    if (this.refreshing()) {
      return;
    }

    this.errorMessage.set(null);
    this.refreshing.set(true);
    this.read();
  }

  private read(): void {
    this.examApi.getExams().subscribe({
      next: (exams) => {
        this.exams.set(exams);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
