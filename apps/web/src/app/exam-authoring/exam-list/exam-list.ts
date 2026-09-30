import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterModule } from '@angular/router';
import { ExamApiService } from '../exam-api.service';
import { ExamDto } from '../exam.models';

@Component({
  selector: 'app-exam-list',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterModule],
  template: `
    <div class="exam-list-container">
      <h2>Exams</h2>

      <div class="actions">
        <a routerLink="/exams/create" class="btn btn-primary">Create New Exam</a>
      </div>

      @if (loading) {
        <div class="loading">Loading exams...</div>
      }
      @if (error) {
        <div class="error">{{ error }}</div>
      }

      @if (!loading && exams.length === 0) {
        <div class="empty">
          No exams found. <a routerLink="/exams/create">Create one now</a>
        </div>
      }

      @if (!loading && exams.length > 0) {
        <div class="exam-grid">
          @for (exam of exams; track exam.id) {
            <div class="exam-card">
              <h3>{{ exam.name }}</h3>
              <p>{{ exam.description }}</p>
              <div class="exam-info">
                <span class="status" [class]="'status-' + exam.status">{{ exam.status }}</span>
                <span class="date">Created: {{ exam.createdAt | date: 'short' }}</span>
              </div>
              <div class="actions">
                <a [routerLink]="['/exams', exam.id, 'schedule']" class="btn btn-secondary">Schedule</a>
                <button class="btn btn-tertiary" (click)="viewExam(exam.id)">View</button>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .exam-list-container { padding: 2rem; }
    .actions { margin: 1rem 0; }
    .btn { padding: 0.5rem 1rem; margin-right: 0.5rem; text-decoration: none; display: inline-block; border: none; cursor: pointer; border-radius: 4px; }
    .btn-primary { background: #007bff; color: white; }
    .btn-secondary { background: #6c757d; color: white; font-size: 0.875rem; padding: 0.25rem 0.5rem; }
    .btn-tertiary { background: #f0f0f0; color: #333; font-size: 0.875rem; padding: 0.25rem 0.5rem; }
    .exam-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; margin-top: 2rem; }
    .exam-card { border: 1px solid #ddd; padding: 1rem; border-radius: 8px; }
    .exam-card h3 { margin-top: 0; }
    .exam-info { display: flex; gap: 1rem; font-size: 0.875rem; margin: 0.5rem 0; }
    .status { padding: 0.25rem 0.5rem; border-radius: 4px; font-weight: bold; }
    .status-Draft { background: #ffc107; }
    .status-Published { background: #28a745; color: white; }
    .status-Active { background: #007bff; color: white; }
    .status-Closed { background: #6c757d; color: white; }
    .exam-card .actions { margin-top: 1rem; display: flex; gap: 0.5rem; }
    .loading, .error, .empty { padding: 2rem; text-align: center; }
    .error { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; border-radius: 4px; }
  `]
})
export class ExamList implements OnInit {
  private examApi = inject(ExamApiService);

  exams: ExamDto[] = [];
  loading = true;
  error = '';

  ngOnInit() {
    this.loadExams();
  }

  loadExams() {
    this.loading = true;
    this.error = '';
    this.examApi.getExams().subscribe({
      next: (data) => {
        this.exams = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load exams';
        this.loading = false;
        console.error(err);
      },
    });
  }

  viewExam(id: string) {
    // Navigate to exam details page when implemented
    console.log('View exam:', id);
  }
}
