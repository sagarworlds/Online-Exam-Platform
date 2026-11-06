import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { InviteApiService } from '../../invite-management/invite-api.service';
import { ProblemDetails } from '../../shared/problem-details';

/** Why a direct sign-in link did not sign the student in; each one has its own wording and its own way forward. */
export type DirectSignInFailure =
  | 'missing'
  | 'invalid'
  | 'locked'
  | 'normalSignIn'
  | 'disabled'
  | 'busy'
  | 'network';

const FAILURE_MESSAGES: Readonly<Record<DirectSignInFailure, MessageKey>> = {
  missing: 'inviteDirect.missing',
  invalid: 'inviteDirect.invalid',
  locked: 'inviteDirect.locked',
  normalSignIn: 'inviteDirect.normalSignIn',
  disabled: 'inviteDirect.disabled',
  busy: 'inviteDirect.busy',
  network: 'inviteDirect.network',
};

/**
 * Where a direct sign-in link lands. Opening the link signs the invited student in, accepts the invitation and goes to the exam's start
 * page, where the instructions, system check and proctoring consent still apply. Nobody is signed in yet, so the page is public and the
 * code is the only proof.
 *
 * The code is in the link's fragment (`#code=…`), which a browser never sends to a server, so it stays out of access logs and `Referer`
 * headers. It is read once and taken off the address at once, so the credential is not left in the address bar or the history entry.
 * The page asks for it with a script (a POST on load), not a plain GET, so a mail scanner that only fetches the link does not spend it.
 */
@Component({
  selector: 'app-invite-direct',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './invite-direct.html',
})
export class InviteDirect {
  private readonly inviteApi = inject(InviteApiService);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  /** Kept in memory for a retry, since it is no longer on the address. */
  private readonly code: string | null;
  private inFlight = false;

  /** Null while the link is being opened; otherwise why it did not work. */
  protected readonly failure = signal<DirectSignInFailure | null>(null);

  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    this.code = new URLSearchParams(fragment ?? '').get('code')?.trim() || null;
    if (fragment !== null) {
      this.location.replaceState(this.location.path());
    }

    if (this.code === null) {
      this.failure.set('missing');
      return;
    }

    this.signIn(this.code);
  }

  protected messageKey(failure: DirectSignInFailure): MessageKey {
    return FAILURE_MESSAGES[failure];
  }

  /** Only a failure that may pass by itself is worth trying again; a used or refused link would fail the same way. */
  protected canRetry(failure: DirectSignInFailure): boolean {
    return failure === 'busy' || failure === 'network';
  }

  protected retry(): void {
    if (this.code !== null) {
      this.signIn(this.code);
    }
  }

  private signIn(code: string): void {
    // One request at a time, and the link is opened once however often the page is asked to start.
    if (this.inFlight) {
      return;
    }

    this.inFlight = true;
    this.failure.set(null);
    this.inviteApi.directSignIn(code).subscribe({
      next: (result) => {
        // Whoever was signed in in this browser is replaced. The old session is not ended on the server by a call of its own: its token may
        // be the very one that was superseded, and a 401 on that call would send this page to the login screen.
        this.authSession.logout();
        this.authSession.login(result.accessToken);
        // Replaced in the history, so Back does not return to a one-time link that no longer works.
        void this.router.navigateByUrl(`/my-exams/${result.examId}/start`, { replaceUrl: true });
      },
      error: (error: unknown) => {
        this.inFlight = false;
        this.failure.set(failureOf(error));
      },
    });
  }
}

/** Maps the API's answer to the failure the page shows. Only known codes are read, so a server string never reaches the page. */
function failureOf(error: unknown): DirectSignInFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return 'network';
  }

  if (error.status === 429) {
    return 'busy';
  }

  switch ((error.error as ProblemDetails | null)?.title) {
    case 'invalid_invite_code':
    case 'invite_state_invalid':
      return 'invalid';
    case 'account_locked':
      return 'locked';
    case 'direct_sign_in_not_allowed':
      return 'normalSignIn';
    case 'direct_sign_in_disabled':
      return 'disabled';
    // A click that lost a race with another click of the same link; trying again says whether the other one won.
    case 'concurrency_conflict':
      return 'busy';
    default:
      return 'network';
  }
}
