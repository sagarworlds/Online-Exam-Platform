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
  { path: '**', redirectTo: 'login' },
];
