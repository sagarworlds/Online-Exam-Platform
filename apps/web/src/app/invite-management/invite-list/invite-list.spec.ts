import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';
import { InviteList } from './invite-list';

describe('InviteList', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InviteList],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/invites');
  const isCodeFor = (id: string) => (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith(`/v1/invites/${id}/codes`);

  const pending = { id: 'i1', email: 'a@example.com', status: 'Pending', sentAt: '2026-10-02T00:00:00Z' };
  const code = (value: string) => ({
    id: `c-${value}`,
    code: value,
    expiresAt: '2026-10-10T09:00:00Z',
    usedAt: null,
    revokedAt: null,
    link: `http://localhost:4200/invite?code=${value}`,
  });

  function open(invites: unknown[]) {
    const fixture = TestBed.createComponent(InviteList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush(invites);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const button = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;

  async function settle(fixture: ReturnType<typeof open>['fixture']) {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('lists invitations and offers Revoke only for one that is still pending', () => {
    const fixture = TestBed.createComponent(InviteList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush([
      { id: 'i1', email: 'a@example.com', status: 'Pending', sentAt: '2026-10-02T00:00:00Z' },
      { id: 'i2', email: 'b@example.com', status: 'Accepted', sentAt: '2026-10-02T00:00:00Z' },
    ]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.textContent).toContain('a@example.com');
    expect(root.textContent).toContain('b@example.com');
    expect(Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Revoke')).toHaveLength(1);
  });

  it('revokes an invitation and reloads the list', () => {
    const fixture = TestBed.createComponent(InviteList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush([{ id: 'i1', email: 'a@example.com', status: 'Pending', sentAt: '2026-10-02T00:00:00Z' }]);
    fixture.detectChanges();

    // By its label: it is no longer the only button on a pending invitation.
    const revoke = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Revoke');
    (revoke as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/i1/revoke')).flush(null, { status: 204, statusText: 'No Content' });
    httpMock.expectOne(isList).flush([{ id: 'i1', email: 'a@example.com', status: 'Revoked', sentAt: '2026-10-02T00:00:00Z' }]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Revoked');
  });

  describe('copying an invite code to hand over', () => {
    let copy: ReturnType<typeof vi.spyOn>;

    beforeEach(() => {
      copy = vi.spyOn(TestBed.inject(ClipboardService), 'copy').mockResolvedValue(true);
    });

    it('is offered only for an invitation that is still pending', () => {
      const { root } = open([
        pending,
        { ...pending, id: 'i2', email: 'b@example.com', status: 'Accepted' },
        { ...pending, id: 'i3', email: 'c@example.com', status: 'Revoked' },
      ]);

      expect(Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Copy invite code')).toHaveLength(1);
    });

    it('asks the API for a new code, copies it, and shows it with its link', async () => {
      const { fixture, root } = open([pending]);

      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      expect(copy).toHaveBeenCalledWith('K7M2QX9A');
      expect(root.textContent).toContain('K7M2QX9A');
      expect(root.textContent).toContain('http://localhost:4200/invite?code=K7M2QX9A');
      expect(root.querySelector('[role="status"]')?.textContent).toContain('Invite code copied');
    });

    it('asks for one code per click, and not again while one is being made', () => {
      const { root } = open([pending, { ...pending, id: 'i2', email: 'b@example.com' }]);

      button(root, 'Copy invite code')!.click();
      Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Copy invite code').forEach((b) => b.click());

      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      httpMock.expectNone(isCodeFor('i2'));
    });

    it('offers another code once one is shown, and replaces the one on show', async () => {
      const { fixture, root } = open([pending]);
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
      const { fixture, root } = open([pending]);

      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      expect(root.textContent).toContain('K7M2QX9A');
      expect(root.querySelector('[role="status"]')?.textContent).toContain('Select the text above and copy it yourself');
    });

    it('hides the code again when asked', async () => {
      const { fixture, root } = open([pending]);
      button(root, 'Copy invite code')!.click();
      httpMock.expectOne(isCodeFor('i1')).flush(code('K7M2QX9A'), { status: 201, statusText: 'Created' });
      await settle(fixture);

      button(root, 'Hide')!.click();
      fixture.detectChanges();

      expect(root.textContent).not.toContain('K7M2QX9A');
      expect(button(root, 'Copy invite code')).toBeDefined();
    });

    it('shows the API error, and no code, when the invitation can no longer be given one', async () => {
      const { fixture, root } = open([pending]);

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
});
