import { Routes } from '@angular/router';
import { Permission } from './auth/admin-sections';
import { authGuard } from './auth/auth.guard';
import { landingGuard } from './auth/landing-route';
import { permissionGuard } from './auth/permission.guard';

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
    path: 'admin/questions',
    canActivate: [permissionGuard(Permission.QuestionManage)],
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
    path: 'admin/attempt-requests',
    canActivate: [permissionGuard(Permission.ExamManage)],
    loadComponent: () => import('./attempt-management/attempt-requests/attempt-requests').then((m) => m.AttemptRequests),
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
    path: 'guardian/link-candidate',
    canActivate: [permissionGuard(Permission.GuardianLinkManage)],
    loadComponent: () => import('./guardian-portal/guardian-link/guardian-link').then((m) => m.GuardianLink),
  },
  { path: '**', redirectTo: 'login' },
];
