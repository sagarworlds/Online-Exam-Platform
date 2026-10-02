import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
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
    path: 'admin/questions',
    canActivate: [authGuard],
    loadComponent: () => import('./question-bank/question-bank').then((m) => m.QuestionBank),
  },
  {
    path: 'exams',
    canActivate: [authGuard],
    loadComponent: () => import('./exam-authoring/exam-list/exam-list').then((m) => m.ExamList),
  },
  {
    path: 'exams/create',
    canActivate: [authGuard],
    loadComponent: () => import('./exam-authoring/exam-builder/exam-builder').then((m) => m.ExamBuilder),
  },
  {
    path: 'exams/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./exam-authoring/exam-editor/exam-editor').then((m) => m.ExamEditor),
  },
  {
    path: 'exams/:id/schedule',
    canActivate: [authGuard],
    loadComponent: () => import('./exam-authoring/exam-scheduler/exam-scheduler').then((m) => m.ExamScheduler),
  },
  {
    path: 'batches',
    canActivate: [authGuard],
    loadComponent: () => import('./batch-management/batch-list/batch-list').then((m) => m.BatchList),
  },
  {
    path: 'batches/create',
    canActivate: [authGuard],
    loadComponent: () => import('./batch-management/batch-create/batch-create').then((m) => m.BatchCreate),
  },
  {
    path: 'batches/:id/roster',
    canActivate: [authGuard],
    loadComponent: () => import('./batch-management/batch-roster/batch-roster').then((m) => m.BatchRoster),
  },
  {
    path: 'invites',
    canActivate: [authGuard],
    loadComponent: () => import('./invite-management/invite-list/invite-list').then((m) => m.InviteList),
  },
  {
    path: 'invites/create',
    canActivate: [authGuard],
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
    canActivate: [authGuard],
    loadComponent: () => import('./guardian-portal/guardian-link/guardian-link').then((m) => m.GuardianLink),
  },
  { path: '**', redirectTo: 'login' },
];
