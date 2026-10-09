import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ExamDto } from '../exam.models';
import { describeScope } from '../exam-scope-fields/exam-scope';

/** Admin page: the newest exams, each linking to its editor. */
@Component({
  selector: 'app-exam-list',
  imports: [RouterLink, DatePipe],
  templateUrl: './exam-list.html',
})
export class ExamList {
  private readonly examApi = inject(ExamApiService);

  protected readonly exams = signal<ExamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly describeScope = describeScope;

  constructor() {
    this.examApi.getExams().subscribe({
      next: (exams) => {
        this.exams.set(exams);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
