import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { MyExamDto } from '../candidate.models';

/** The candidate's page: the exams they have accepted an invitation to, and whether each can be started now (FR-16). */
@Component({
  selector: 'app-my-exams',
  imports: [DatePipe],
  templateUrl: './my-exams.html',
})
export class MyExams {
  private readonly api = inject(CandidateApiService);

  protected readonly exams = signal<MyExamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    this.api.listMyExams().subscribe({
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
