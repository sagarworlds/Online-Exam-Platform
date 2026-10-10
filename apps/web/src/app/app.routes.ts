import { Routes } from '@angular/router';
import { Permission } from './auth/admin-sections';
import { authGuard } from './auth/auth.guard';
import { landingGuard } from './auth/landing-route';
import { permissionGuard } from './auth/permission.guard';
import { CandidateApiService } from './candidate/candidate-api.service';
import { PreviewCandidateApiService } from './candidate/exam-attempt/preview-candidate-api.service';

export const routes: Routes = [
  // The root sends each visitor home: to /login when signed out, to their exams or the admin area when signed in.
  { path: '', pathMatch: 'full', canActivate: [landingGuard], children: [] },
  {
    path: 'login',
    loadComponent: () => import('./auth/login/login').then((m) => m.Login),
  },
  {
    path: 'register',
    loadComponent: () => import('./auth/register/register').then((m) => m.Register),
  },
  {
    path: 'verify-otp',
    loadComponent: () => import('./auth/verify-otp/verify-otp').then((m) => m.VerifyOtp),
  },
  {
    path: 'password-reset/request',
    loadComponent: () =>
      import('./auth/password-reset-request/password-reset-request').then((m) => m.PasswordResetRequest),
  },
  {
    path: 'password-reset/confirm',
    loadComponent: () =>
      import('./auth/password-reset-confirm/password-reset-confirm').then((m) => m.PasswordResetConfirm),
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () => import('./profile/profile').then((m) => m.Profile),
  },
  {
    path: 'consent',
    canActivate: [authGuard],
    loadComponent: () => import('./consent/consent').then((m) => m.Consent),
  },
  {
    path: 'notifications',
    canActivate: [authGuard],
    loadComponent: () => import('./notifications/notification-feed/notification-feed').then((m) => m.NotificationFeed),
  },
  {
    path: 'admin',
    canActivate: [authGuard],
    loadComponent: () => import('./admin/admin-home/admin-home').then((m) => m.AdminHome),
  },
  {
    path: 'forbidden',
    loadComponent: () => import('./admin/forbidden/forbidden').then((m) => m.Forbidden),
  },
  {
    path: 'invite',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/invite-accept/invite-accept').then((m) => m.InviteAccept),
  },
  {
    path: 'my-exams',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/my-exams/my-exams').then((m) => m.MyExams),
  },
  {
    path: 'my-exams/:examId/start',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/exam-instructions/exam-instructions').then((m) => m.ExamInstructions),
  },
  {
    path: 'attempt/:attemptId',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/exam-attempt/exam-attempt').then((m) => m.ExamAttempt),
  },
  {
    path: 'attempt/:attemptId/review',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/attempt-review/attempt-review').then((m) => m.AttemptReview),
  },
  {
    path: 'attempt/:attemptId/result',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/attempt-result/attempt-result').then((m) => m.AttemptResult),
  },
  {
    path: 'my-exams/:examId/leaderboard',
    canActivate: [authGuard],
    loadComponent: () => import('./candidate/leaderboard/leaderboard').then((m) => m.Leaderboard),
  },
  {
    path: 'admin/questions',
    canActivate: [permissionGuard(Permission.QuestionRead)],
    loadComponent: () => import('./question-bank/question-bank').then((m) => m.QuestionBank),
  },
  {
    path: 'admin/questions/:id/edit',
    canActivate: [permissionGuard(Permission.QuestionManage)],
    loadComponent: () => import('./question-bank/question-edit/question-edit').then((m) => m.QuestionEdit),
  },
  {
    path: 'admin/books',
    canActivate: [permissionGuard(Permission.QuestionManage)],
    loadComponent: () => import('./book-management/book-list/book-list').then((m) => m.BookList),
  },
  {
    path: 'admin/books/:id',
    canActivate: [permissionGuard(Permission.QuestionManage)],
    loadComponent: () => import('./book-management/book-detail/book-detail').then((m) => m.BookDetail),
  },
  {
    path: 'exams',
    canActivate: [permissionGuard(Permission.ExamRead)],
    loadComponent: () => import('./exam-authoring/exam-list/exam-list').then((m) => m.ExamList),
  },
  {
    path: 'exams/create',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./exam-authoring/exam-builder/exam-builder').then((m) => m.ExamBuilder),
  },
  {
    path: 'exams/:id',
    canActivate: [permissionGuard(Permission.ExamRead)],
    loadComponent: () => import('./exam-authoring/exam-editor/exam-editor').then((m) => m.ExamEditor),
  },
  {
    path: 'admin/otp-codes',
    canActivate: [permissionGuard(Permission.OtpRead)],
    loadComponent: () => import('./admin/otp-codes/otp-codes').then((m) => m.OtpCodes),
  },
  {
    path: 'admin/whatsapp',
    canActivate: [permissionGuard(Permission.WhatsAppTest)],
    loadComponent: () => import('./admin/whatsapp-test/whatsapp-test').then((m) => m.WhatsAppTest),
  },
  {
    path: 'admin/attempt-requests',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./attempt-management/attempt-requests/attempt-requests').then((m) => m.AttemptRequests),
  },
  {
    path: 'admin/disputes',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./attempt-management/disputes/disputes').then((m) => m.Disputes),
  },
  {
    path: 'admin/issue-reports',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./attempt-management/issue-reports/issue-reports').then((m) => m.IssueReports),
  },
  {
    // Staff see an exam as a candidate would (FR-15). The same page runs on the preview API, which saves nothing.
    path: 'exams/:id/preview',
    canActivate: [permissionGuard(Permission.ExamManage)],
    data: { preview: true },
    providers: [{ provide: CandidateApiService, useClass: PreviewCandidateApiService }],
    loadComponent: () => import('./candidate/exam-attempt/exam-attempt').then((m) => m.ExamAttempt),
  },
  {
    path: 'exams/:id/attempts',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./attempt-management/exam-attempts/exam-attempts').then((m) => m.ExamAttempts),
  },
  {
    path: 'exams/:id/schedule',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./exam-authoring/exam-scheduler/exam-scheduler').then((m) => m.ExamScheduler),
  },
  {
    path: 'batches',
    canActivate: [permissionGuard(Permission.BatchManage)],
    loadComponent: () => import('./batch-management/batch-list/batch-list').then((m) => m.BatchList),
  },
  {
    path: 'batches/create',
    canActivate: [permissionGuard(Permission.BatchManage)],
    loadComponent: () => import('./batch-management/batch-create/batch-create').then((m) => m.BatchCreate),
  },
  {
    path: 'batches/:id/roster',
    canActivate: [permissionGuard(Permission.BatchManage)],
    loadComponent: () => import('./batch-management/batch-roster/batch-roster').then((m) => m.BatchRoster),
  },
  {
    path: 'invites',
    canActivate: [permissionGuard(Permission.InviteManage)],
    loadComponent: () => import('./invite-management/invite-list/invite-list').then((m) => m.InviteList),
  },
  {
    path: 'invites/create',
    canActivate: [permissionGuard(Permission.InviteManage)],
    loadComponent: () => import('./invite-management/invite-create/invite-create').then((m) => m.InviteCreate),
  },
  {
    path: 'guardian',
    canActivate: [authGuard],
    loadComponent: () => import('./guardian-portal/guardian-dashboard/guardian-dashboard').then((m) => m.GuardianDashboard),
  },
  {
    path: 'guardian/register',
    loadComponent: () => import('./guardian-portal/guardian-register/guardian-register').then((m) => m.GuardianRegister),
  },
  {
    // Public: the guardian opens this from the e-mail. It has no account, so the code in the link is the only proof.
    path: 'guardian/confirm-link',
    loadComponent: () => import('./guardian-portal/guardian-confirm-link/guardian-confirm-link').then((m) => m.GuardianConfirmLink),
  },
  {
    path: 'guardian/link-candidate',
    canActivate: [permissionGuard(Permission.GuardianLinkManage)],
    loadComponent: () => import('./guardian-portal/guardian-link/guardian-link').then((m) => m.GuardianLink),
  },
  {
    // Staff review the risk flags of an exam's attempts (FR-27). Nothing on the page changes a candidate's attempt.
    path: 'exams/:id/risk-flags',
    canActivate: [permissionGuard(Permission.ProctoringReview)],
    loadComponent: () => import('./proctoring/risk-flag-queue/risk-flag-queue').then((m) => m.RiskFlagQueue),
  },
  {
    // The candidate's own performance across the exams they sat (FR-36): released results only, and only their own.
    path: 'analytics',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./analytics/candidate-analytics/candidate-analytics').then((m) => m.CandidateAnalytics),
  },
  {
    // Staff see how each question of an exam performed (FR-37): difficulty and discrimination, once enough candidates had the question.
    path: 'exams/:id/item-analysis',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./analytics/item-analysis/item-analysis').then((m) => m.ItemAnalysis),
  },
  {
    // Staff set the institute's name, colour and logo for the candidate pages (FR-41). Reading is open; changing needs exam.manage.
    path: 'admin/branding',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./branding/branding-settings/branding-settings').then((m) => m.BrandingSettings),
  },
  {
    // The reusable instructions an exam starts from (FR-41).
    path: 'admin/instruction-templates',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () =>
      import('./instruction-templates/instruction-templates').then((m) => m.InstructionTemplates),
  },
  { path: '**', redirectTo: 'login' },
];
