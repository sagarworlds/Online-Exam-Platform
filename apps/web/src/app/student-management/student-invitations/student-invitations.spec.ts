import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { InviteDto } from '../../invite-management/invite.models';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { StudentInvitations } from './student-invitations';
import { AUTH_TOKEN_STORAGE_KEY } from '../../auth/auth-session.service';
import { buildFakeJwt } from '../../auth/testing/fake-jwt';

describe('StudentInvitations', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StudentInvitations],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const isCodeFor = (id: string) => (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith(`/v1/invites/${id}/codes`);

  const invite = (id: string, examName: string | null, over: Partial<InviteDto> = {}): InviteDto =>
    ({ id, email: 'a@example.com', examName, status: 'Pending', sentAt: '2026-10-02T00:00:00Z', ...over }) as InviteDto;

  const code = (value: string) => ({
    id: `c-${value}`,
    code: value,
    expiresAt: '2026-10-10T09:00:00Z',
    usedAt: null,
    revokedAt: null,
    link: `http://localhost:4200/invite?code=${value}`,
  });

  function open(invites: InviteDto[]) {
    const fixture = TestBed.createComponent(StudentInvitations);
    fixture.componentRef.setInput('invites', invites);
    const revoked = vi.fn();
    fixture.componentInstance.revoked.subscribe(revoked);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, revoked };
  }

  const button = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;

  async function settle(fixture: ReturnType<typeof open>['fixture']) {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const exams = (root: HTMLElement) => Array.from(root.querySelectorAll('.invite-row h4')).map((h) => h.childNodes[0].textContent?.trim());

  it('lists the invitations in the order given, each with its exam and status', () => {
    const { root } = open([invite('i1', 'Physics Quiz'), invite('i2', 'Maths Final', { status: 'Accepted' })]);

    expect(exams(root)).toEqual(['Physics Quiz', 'Maths Final']);
    expect(root.querySelectorAll('.status-badge')).toHaveLength(2);
    expect(root.querySelector('.status-badge[data-tone="warning"]')?.textContent).toContain('Pending');
    expect(root.querySelector('.status-badge[data-tone="success"]')?.textContent).toContain('Accepted');
  });

  it('names an invitation to an exam that cannot be found rather than leaving its row blank', () => {
    const { root } = open([invite('i1', null)]);

    expect(exams(root)).toEqual(['Exam not found']);
  });

  it("names each row's buttons for its exam, so two rows are not two identical buttons", () => {
    const { root } = open([invite('i1', 'Maths Final'), invite('i2', 'Physics Quiz')]);

    const labels = Array.from(root.querySelectorAll('.invite-row button')).map((b) => b.getAttribute('aria-label'));
    expect(labels).toContain('Copy invite code for Maths Final');
    expect(labels).toContain('Copy invite code for Physics Quiz');
    expect(labels).toContain('Revoke the invitation to Maths Final');
    expect(labels).toContain('Revoke the invitation to Physics Quiz');
  });

  it('offers Revoke and a code only for an invitation that is still pending', () => {
    const { root } = open([invite('i1', 'A'), invite('i2', 'B', { status: 'Accepted' }), invite('i3', 'C', { status: 'Revoked' })]);

    expect(Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Revoke')).toHaveLength(1);
    expect(Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Copy invite code')).toHaveLength(1);
  });

  describe('revoking', () => {
    const answer = (confirmed: boolean) => vi.spyOn(TestBed.inject(ConfirmService), 'ask').mockResolvedValue(confirmed);

    it('asks first, naming the exam, and sends nothing when the answer is no', async () => {
      const ask = answer(false);
      const { root, revoked } = open([invite('i1', 'Maths Final')]);

      button(root, 'Revoke')!.click();
      await Promise.resolve();

      expect(ask).toHaveBeenCalledTimes(1);
      expect(ask.mock.calls[0][0]).toMatchObject({ title: 'Revoke this invitation?', confirmLabel: 'Revoke invitation', danger: true });
      expect(ask.mock.calls[0][0].message).toContain('Maths Final');
      httpMock.expectNone((r) => r.url.endsWith('/revoke'));
      expect(revoked).not.toHaveBeenCalled();
    });

    it('revokes the invitation and tells the page to read its list again', async () => {
      answer(true);
      const { root, revoked } = open([invite('i1', 'Maths Final')]);

      button(root, 'Revoke')!.click();
      await Promise.resolve();
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/i1/revoke')).flush(null, { status: 204, statusText: 'No Content' });

      expect(revoked).toHaveBeenCalledTimes(1);
    });

    it('shows the API error and does not tell the page when the revoke is refused', async () => {
      answer(true);
      const { fixture, root, revoked } = open([invite('i1', 'Maths Final')]);

      button(root, 'Revoke')!.click();
      await Promise.resolve();
      httpMock
        .expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/i1/revoke'))
        .flush({ title: 'invite_state_invalid', detail: 'That invitation was already accepted.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('That invitation was already accepted.');
      expect(revoked).not.toHaveBeenCalled();
    });
  });

  describe('copying an invite code to hand over', () => {
    let copy: ReturnType<typeof vi.spyOn>;

    beforeEach(() => {
      copy = vi.spyOn(TestBed.inject(ClipboardService), 'copy').mockResolvedValue(true);
    });

    it('asks the API for a new code, copies it, and shows it with its link', async () => {
      const { fixture, root } = open([invite('i1', 'Maths Final')]);

      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      expect(copy).toHaveBeenCalledWith('K7M2QX9A');
      expect(root.textContent).toContain('K7M2QX9A');
      expect(root.textContent).toContain('http://localhost:4200/invite?code=K7M2QX9A');
      expect(root.querySelector('[role="status"]')?.textContent).toContain('Invite code copied');
    });

    it('asks for one code per click, and not again while one is being made', () => {
      const { root } = open([invite('i1', 'A'), invite('i2', 'B')]);

      button(root, 'Copy invite code')!.click();
      Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Copy invite code').forEach((b) => b.click());

      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      httpMock.expectNone(isCodeFor('i2'));
    });

    it('offers another code once one is shown, and replaces the one on show', async () => {
      const { fixture, root } = open([invite('i1', 'Maths Final')]);
      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('AAAAAAAA'), { status: 201, statusText: 'Created' });
      await settle(fixture);
      expect(button(root, 'Copy invite code')).toBeUndefined();

      button(root, 'Get another code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('BBBBBBBB'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      expect(root.textContent).toContain('BBBBBBBB');
      expect(root.textContent).not.toContain('AAAAAAAA');
      expect(copy).toHaveBeenLastCalledWith('BBBBBBBB');
    });

    it('still shows the code to select when the browser will not copy', async () => {
      copy.mockResolvedValue(false);
      const { fixture, root } = open([invite('i1', 'Maths Final')]);

      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      expect(root.textContent).toContain('K7M2QX9A');
      expect(root.querySelector('[role="status"]')?.textContent).toContain('Select the text above and copy it yourself');
    });

    it('hides the code again when asked', async () => {
      const { fixture, root } = open([invite('i1', 'Maths Final')]);
      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      button(root, 'Hide')!.click();
      fixture.detectChanges();

      expect(root.textContent).not.toContain('K7M2QX9A');
      expect(button(root, 'Copy invite code')).toBeDefined();
    });

    it('shows the API error, and no code, when the invitation can no longer be given one', async () => {
      const { fixture, root } = open([invite('i1', 'Maths Final')]);

      button(root, 'Copy invite code')!.click();
      httpMock
        .expectOne(isCodeFor('i1'))
        .flush({ title: 'invite_state_invalid', detail: 'Only a pending invitation can be given another code.' }, { status: 409, statusText: 'Conflict' });
      await settle(fixture);

      expect(root.textContent).toContain('Only a pending invitation can be given another code.');
      expect(copy).not.toHaveBeenCalled();
      expect(root.querySelector('app-invite-handover')).toBeNull();
    });
  });

  describe('direct sign-in links', () => {
    const permit = () =>
      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'staff-1', exp: Math.floor(Date.now() / 1000) + 3600, perm: ['invite.direct-link'] }));

    beforeEach(() => localStorage.clear());
    afterEach(() => localStorage.clear());

    function openAs(permitted: boolean, invites: InviteDto[]) {
      TestBed.resetTestingModule();
      if (permitted) permit();
      TestBed.configureTestingModule({ imports: [StudentInvitations], providers: [provideHttpClient(), provideHttpClientTesting()] });
      httpMock = TestBed.inject(HttpTestingController);
      return open(invites);
    }

    it('marks an invitation that has a direct link', () => {
      const { root } = openAs(false, [invite('i1', 'Maths', { hasDirectLink: true }), invite('i2', 'Physics')]);

      const rows = Array.from(root.querySelectorAll('.invite-row'));
      expect(rows[0].textContent).toContain('Direct link');
      expect(rows[1].textContent).not.toContain('Direct link');
    });

    it('offers "Copy direct link" only to staff who hold the permission', () => {
      expect(button(openAs(false, [invite('i1', 'Maths')]).root, 'Copy direct link')).toBeUndefined();

      expect(button(openAs(true, [invite('i1', 'Maths')]).root, 'Copy direct link')).toBeDefined();
    });

    it('asks the API for a new direct code, and shows its link as one that signs the student in', async () => {
      const { fixture, root } = openAs(true, [invite('i1', 'Maths')]);

      button(root, 'Copy direct link')!.click();
      const request = httpMock.expectOne(isCodeFor('i1'));
      expect(request.request.body).toEqual({ directSignIn: true });
      request.flush(
        { ...code('ABCD1234EFGH5678'), link: 'http://localhost:4200/invite/direct#code=ABCD1234EFGH5678', directSignIn: true },
        { status: 201, statusText: 'Created' },
      );
      await fixture.whenStable();
      fixture.detectChanges();

      expect(root.textContent).toContain('http://localhost:4200/invite/direct#code=ABCD1234EFGH5678');
      expect(root.textContent).toContain('signs a@example.com in');
    });

    it('does not ask for a direct code when the ordinary copy button is used', () => {
      const { root } = openAs(true, [invite('i1', 'Maths')]);

      button(root, 'Copy invite code')!.click();

      const ordinary = httpMock.expectOne(isCodeFor('i1'));
      expect(ordinary.request.body).toEqual({});
      ordinary.flush(code('AB12CD34'), { status: 201, statusText: 'Created' });
    });
  });
});
