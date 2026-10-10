import { Injectable, effect, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';

/** The name of the app in the browser tab, shown after the name of the page. */
export const APP_TITLE = 'Online Exam Platform';

/**
 * The message that names each page in the browser tab, keyed by the route's path as app.routes.ts writes it. Most entries reuse the
 * heading message of their screen, so the tab and the page's heading agree in every language. A page not listed here keeps the app's name.
 */
export const PAGE_TITLE_KEYS: Readonly<Record<string, MessageKey>> = {
  login: 'login.title',
  register: 'register.title',
  'verify-otp': 'verify.title',
  'password-reset/request': 'reset.requestTitle',
  'password-reset/confirm': 'reset.confirmTitle',
  profile: 'profile.title',
  consent: 'consent.title',
  forbidden: 'forbidden.title',
  invite: 'invite.title',
  'my-exams': 'myExams.title',
  'my-exams/:examId/start': 'instructions.heading',
  'attempt/:attemptId': 'pageTitle.exam',
  'attempt/:attemptId/review': 'review.title',
  'attempt/:attemptId/result': 'result.title',
  'admin/questions': 'admin.area.questions.name',
  'admin/questions/:id/edit': 'pageTitle.questionEdit',
  'admin/books': 'admin.area.books.name',
  'admin/otp-codes': 'admin.area.candidateCodes.name',
  'admin/whatsapp': 'whatsapp.title',
  'admin/attempt-requests': 'admin.requests.title',
  'admin/disputes': 'admin.disputes.title',
  'admin/issue-reports': 'admin.issues.title',
  exams: 'admin.area.exams.name',
  'exams/create': 'pageTitle.examNew',
  'exams/:id': 'pageTitle.examEdit',
  'exams/:id/attempts': 'exams.editor.candidatesLink',
  'exams/:id/preview': 'pageTitle.exam',
  batches: 'admin.area.batches.name',
  invites: 'admin.area.invites.name',
};

/**
 * Sets the browser tab's title to the page the visitor is on (WCAG 2.4.2). A screen reader announces the title when a page opens, and
 * the tab list shows it, so each page needs its own. The router's default only reads a `title` set on a route, and no route sets one, so
 * without this every page would keep the title the app opened with.
 *
 * The title is a signal-driven effect rather than a one-off set: switching language renames the open page as well, so the tab never
 * shows the old language after the page itself has changed.
 */
@Injectable()
export class PageTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18nService);
  /** The message that names the page the visitor is on, or null for a page with no name of its own. */
  private readonly page = signal<MessageKey | null>(null);

  constructor() {
    super();
    effect(() => {
      const key = this.page();
      this.title.setTitle(key === null ? APP_TITLE : `${this.i18n.t(key)} · ${APP_TITLE}`);
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const path = deepestRoutePath(snapshot);
    this.page.set(path === undefined ? null : (PAGE_TITLE_KEYS[path] ?? null));
  }
}

/** The path, as written in app.routes.ts, of the deepest route the URL is on (for example `attempt/:attemptId/review`). */
export function deepestRoutePath(snapshot: RouterStateSnapshot): string | undefined {
  let route = snapshot.root;
  while (route.firstChild) {
    route = route.firstChild;
  }
  return route.routeConfig?.path;
}
