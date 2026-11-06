import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { AuthSessionService } from '../../auth/auth-session.service';
import { buildFakeJwt } from '../../auth/testing/fake-jwt';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';
import { InviteToExam } from './invite-to-exam';

describe('InviteToExam', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [InviteToExam],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function signIn(permissions: string[]) {
    TestBed.inject(AuthSessionService).login(buildFakeJwt({ sub: 'staff-1', exp: Math.floor(Date.now() / 1000) + 3600, perm: permissions }));
  }

  function open(permissions: string[] = ['invite.manage']) {
    signIn(permissions);
    const fixture = TestBed.createComponent(InviteToExam);
    fixture.componentRef.setInput('examId', 'exam-1');
    fixture.componentRef.setInput('examName', 'Maths Final');
    const closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, closed };
  }

  function type(fixture: ComponentFixture<InviteToExam>, root: HTMLElement, address: string) {
    const email = root.querySelector<HTMLInputElement>('#invite-to-exam-email')!;
    email.value = address;
    email.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  const submit = (root: HTMLElement) => root.querySelector('form')!.dispatchEvent(new Event('submit'));
  const sendButton = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('form button[type="submit"]')!;
  const isInvite = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/invites');

  it('is titled for the exam and asks for an email address, with the keyboard a phone offers for one', () => {
    const { root } = open();

    expect(root.querySelector('.modal__title')?.textContent).toContain('Invite a candidate to Maths Final');
    const email = root.querySelector<HTMLInputElement>('#invite-to-exam-email')!;
    expect(email.type).toBe('email');
    expect(email.getAttribute('inputmode')).toBe('email');
    expect(root.querySelector('label[for="invite-to-exam-email"]')?.textContent).toContain('email');
  });

  it('puts the cursor in the address box when it opens', () => {
    const { root } = open();

    expect(document.activeElement).toBe(root.querySelector('#invite-to-exam-email'));
  });

  it('asks for no exam: the exam is the one it was opened for', () => {
    const { root } = open();

    expect(root.querySelector('select')).toBeNull();
  });

  it('cannot be sent without a valid address', () => {
    const { fixture, root } = open();
    expect(sendButton(root).disabled).toBe(true);

    type(fixture, root, 'not-an-address');
    expect(sendButton(root).disabled).toBe(true);
    submit(root);
    httpMock.expectNone(isInvite);

    type(fixture, root, 'ok@example.com');
    expect(sendButton(root).disabled).toBe(false);
  });

  it('sends the address, trimmed, for this exam, and says the email went out', () => {
    const { fixture, root } = open();
    type(fixture, root, '  new.person@example.com ');

    submit(root);

    const post = httpMock.expectOne(isInvite);
    expect(post.request.body).toEqual({ examId: 'exam-1', email: 'new.person@example.com' });
    fixture.detectChanges();
    expect(sendButton(root).disabled).toBe(true);
    post.flush({ id: 'i1', email: 'new.person@example.com', emailSent: true, whatsAppSent: false, inviteLink: null }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Invitation emailed to new.person@example.com');
    expect(root.textContent).toContain('appear here once they accept');
    expect(root.querySelector('form')).toBeNull();
    // The form that had the focus is gone, so the focus is on what was done: a screen reader reads it out.
    const result = root.querySelector<HTMLElement>('[role="group"]')!;
    expect(result.getAttribute('aria-label')).toBe('Invitation sent');
    expect(document.activeElement).toBe(result);
  });

  it('does not send twice while the first is on its way', () => {
    const { fixture, root } = open();
    type(fixture, root, 'new.person@example.com');

    submit(root);
    submit(root);

    httpMock.expectOne(isInvite);
  });

  it('shows the link and code to pass on by hand when nothing could be sent', () => {
    const { fixture, root } = open();
    type(fixture, root, 'new.person@example.com');
    submit(root);

    httpMock
      .expectOne(isInvite)
      .flush({ id: 'i1', email: 'new.person@example.com', emailSent: false, inviteLink: 'http://localhost:4200/invite?code=AB12CD34', inviteCode: 'AB12CD34' }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(root.textContent).toContain('no email could be sent');
    expect(root.textContent).toContain('AB12CD34');
    expect(root.textContent).toContain('http://localhost:4200/invite?code=AB12CD34');
  });

  it('lets the inviter ask for a code of their own to hand over once the email went out', async () => {
    vi.spyOn(TestBed.inject(ClipboardService), 'copy').mockResolvedValue(true);
    const { fixture, root } = open();
    type(fixture, root, 'new.person@example.com');
    submit(root);
    httpMock.expectOne(isInvite).flush({ id: 'i1', email: 'new.person@example.com', emailSent: true, inviteLink: null }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Copy an invite code'))!.click();
    const request = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/i1/codes'));
    request.flush({ id: 'c1', code: 'K7M2QX9A', expiresAt: '2026-11-07T00:00:00Z', usedAt: null, revokedAt: null, link: 'http://localhost:4200/invite?code=K7M2QX9A' });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(root.textContent).toContain('K7M2QX9A');
  });

  it('keeps the form, with what was typed, and shows the API reason when the invitation is refused', () => {
    const { fixture, root } = open();
    type(fixture, root, 'new.person@example.com');
    submit(root);

    httpMock.expectOne(isInvite).flush({ title: 'invite_exam_not_found', detail: 'The exam is not open for invitations.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('The exam is not open for invitations.');
    expect(root.querySelector<HTMLInputElement>('#invite-to-exam-email')!.value).toBe('new.person@example.com');
    expect(sendButton(root).disabled).toBe(false);
  });

  it('offers a fresh form for the next candidate, and forgets the last one', () => {
    const { fixture, root } = open();
    type(fixture, root, 'new.person@example.com');
    submit(root);
    httpMock.expectOne(isInvite).flush({ id: 'i1', email: 'new.person@example.com', emailSent: true, inviteLink: null }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Invite another candidate'))!.click();
    fixture.detectChanges();

    expect(root.textContent).not.toContain('Invitation emailed');
    expect(root.querySelector<HTMLInputElement>('#invite-to-exam-email')!.value).toBe('');
    expect(sendButton(root).disabled).toBe(true);
    expect(document.activeElement).toBe(root.querySelector('#invite-to-exam-email'));
  });

  describe('the direct sign-in link', () => {
    it('is offered only to staff who may send one', () => {
      expect(open(['invite.manage']).root.querySelector('#invite-to-exam-direct')).toBeNull();
    });

    it('is not sent unless it is ticked', () => {
      const { fixture, root } = open(['invite.manage', 'invite.direct-link']);
      type(fixture, root, 'new.person@example.com');

      submit(root);

      expect(httpMock.expectOne(isInvite).request.body).toEqual({ examId: 'exam-1', email: 'new.person@example.com' });
    });

    it('is asked for when it is ticked', () => {
      const { fixture, root } = open(['invite.manage', 'invite.direct-link']);
      type(fixture, root, 'new.person@example.com');
      const direct = root.querySelector<HTMLInputElement>('#invite-to-exam-direct')!;
      direct.click();
      fixture.detectChanges();

      submit(root);

      expect(httpMock.expectOne(isInvite).request.body).toEqual({ examId: 'exam-1', email: 'new.person@example.com', directSignIn: true });
    });

    it('is never asked for by someone who may not send one, whatever the form held', () => {
      const { fixture, root } = open(['invite.manage']);
      fixture.componentInstance['form'].controls.directSignIn.setValue(true);
      type(fixture, root, 'new.person@example.com');

      submit(root);

      expect(httpMock.expectOne(isInvite).request.body).toEqual({ examId: 'exam-1', email: 'new.person@example.com' });
    });
  });
});
