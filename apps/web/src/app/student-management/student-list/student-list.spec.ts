import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AuthSessionService } from '../../auth/auth-session.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { StudentList } from './student-list';

describe('StudentList', () => {
  let httpMock: HttpTestingController;

  const asha = { id: 'a1', displayName: 'Asha Rao', email: 'asha@example.com', phoneNumber: '+919800000000', status: 'Active' };
  const ben = { id: 'a2', displayName: 'Ben Das', email: 'ben@example.com', phoneNumber: null, status: 'Active' };

  const invite = (id: string, email: string, examName: string, status = 'Pending') => ({
    id,
    email,
    examName,
    status,
    sentAt: `2026-10-0${id.replace(/\D/g, '') || '1'}T00:00:00Z`,
  });

  const isStudents = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/admin/students');
  const isInvites = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/invites');
  const isLookup = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/admin/students/lookup');

  function configure(permissions: string[]) {
    TestBed.configureTestingModule({
      imports: [StudentList],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthSessionService, useValue: { hasPermission: (code: string) => permissions.includes(code) } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  }

  /** Opens the page and answers its first read. `holders` answers the lookup, when the page makes one. */
  function open(
    answers: { students?: unknown[]; invites?: unknown[]; holders?: unknown[] } = {},
    permissions = ['student.read', 'student.manage', 'invite.manage'],
  ) {
    configure(permissions);
    const fixture = TestBed.createComponent(StudentList);
    fixture.detectChanges();
    answerRead(answers);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function answerRead(answers: { students?: unknown[]; invites?: unknown[]; holders?: unknown[] }) {
    httpMock.expectOne(isStudents).flush(answers.students ?? []);
    if (permissions().includes('invite.manage')) {
      httpMock.expectOne(isInvites).flush(answers.invites ?? []);
    }
    const lookups = httpMock.match(isLookup);
    for (const lookup of lookups) {
      lookup.flush(answers.holders ?? []);
    }
  }

  const permissions = () => {
    const session = TestBed.inject(AuthSessionService);
    return ['student.read', 'student.manage', 'invite.manage'].filter((code) => session.hasPermission(code));
  };

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  const rowFor = (root: HTMLElement, title: string) =>
    Array.from(root.querySelectorAll<HTMLElement>('section.student-row')).find((s) => s.querySelector('.student-row__name')?.textContent?.trim() === title)!;
  const toggleOf = (root: HTMLElement, title: string) => rowFor(root, title).querySelector<HTMLButtonElement>('.student-row__toggle')!;
  const titles = (root: HTMLElement) => Array.from(root.querySelectorAll('.student-row__name')).map((n) => n.textContent?.trim());

  /** The edit, deactivate or reactivate icon at the end of a student's row (the form a mouse gets), or null when the row has none. */
  const iconOf = (root: HTMLElement, title: string, label: 'Edit' | 'Deactivate' | 'Reactivate') =>
    rowFor(root, title)?.querySelector<HTMLButtonElement>(`.row-actions--icons button[aria-label="${label} ${title}"]`) ?? null;

  /** The same action as a button with its word, in the open row (the form a touch screen gets). */
  const wordsOf = (root: HTMLElement, title: string, label: 'Edit' | 'Deactivate' | 'Reactivate') =>
    rowFor(root, title)?.querySelector<HTMLButtonElement>(`.student-row__panel .row-actions--words button[aria-label="${label} ${title}"]`) ?? null;

  const profileOf = (student: { id: string }) => ({ ...asha, ...student, dateOfBirth: '2000-01-01', ageBand: 'Adult' });
  /** A button by its words. While a popup is open it is looked for in the popup: a row's own Deactivate is on the page too, behind it. */
  const buttonNamed = (root: HTMLElement, name: string) =>
    Array.from((root.querySelector('app-modal-dialog') ?? root).querySelectorAll('button')).find((b) => b.textContent?.trim() === name);

  /** Clicks a student's edit icon and answers the profile read the popup makes. */
  function openEditPopup(fixture: ReturnType<typeof open>['fixture'], root: HTMLElement, title: string, student: { id: string } = asha) {
    iconOf(root, title, 'Edit')!.click();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith(`/v1/admin/students/${student.id}`)).flush(profileOf(student));
    fixture.detectChanges();
  }

  it('lists the students, then the invited addresses nobody has registered with', () => {
    const { root } = open({
      students: [asha, ben],
      invites: [invite('1', 'asha@example.com', 'Maths'), invite('2', 'newcomer@example.com', 'Maths')],
    });

    expect(titles(root)).toEqual(['Asha Rao', 'Ben Das', 'newcomer@example.com']);
    expect(rowFor(root, 'newcomer@example.com').textContent).toContain('Not registered yet');
    expect(rowFor(root, 'Asha Rao').textContent).not.toContain('Not registered yet');
  });

  it("shows a student's contact details and how many invitations they have, with each status", () => {
    const { root } = open({
      students: [asha],
      invites: [invite('1', 'asha@example.com', 'Maths'), invite('2', 'ASHA@example.com', 'Physics', 'Accepted')],
    });

    const header = toggleOf(root, 'Asha Rao').textContent!;
    expect(header).toContain('asha@example.com · +919800000000');
    expect(header).toContain('2 invitations');
    expect(header).toContain('1 Pending');
    expect(header).toContain('1 Accepted');
    expect(rowFor(root, 'Ben Das')).toBeUndefined();
  });

  it('says so when a student has no invitations', () => {
    const { root } = open({ students: [ben] });

    expect(toggleOf(root, 'Ben Das').textContent).toContain('No invitations');
  });

  it('flags an account that is not active', () => {
    const { root } = open({ students: [{ ...ben, status: 'Suspended' }] });

    expect(toggleOf(root, 'Ben Das').textContent).toContain('Suspended');
  });

  it('says how many students there are and how many invitations are waiting', () => {
    const { root } = open({
      students: [asha],
      invites: [invite('1', 'asha@example.com', 'Maths'), invite('2', 'x@example.com', 'Maths'), invite('3', 'y@example.com', 'Maths', 'Revoked')],
    });

    expect(root.querySelector('.student-summary')?.textContent?.trim()).toBe('3 students · 2 invitations pending');
  });

  describe('asking which invited addresses have an account', () => {
    it('asks about the invited addresses the listed accounts do not hold, once', () => {
      configure(['student.read', 'student.manage', 'invite.manage']);
      const fixture = TestBed.createComponent(StudentList);
      fixture.detectChanges();
      httpMock.expectOne(isStudents).flush([asha]);
      httpMock.expectOne(isInvites).flush([invite('1', 'asha@example.com', 'Maths'), invite('2', 'far@example.com', 'Maths'), invite('3', 'far@example.com', 'Physics')]);

      const lookup = httpMock.expectOne(isLookup);
      expect(lookup.request.body).toEqual({ emails: ['far@example.com'] });
      lookup.flush([]);
    });

    it('shows an invited address as the student who holds it, not as unregistered, when the list stopped short of them', () => {
      const { root } = open({
        students: [asha],
        invites: [invite('1', 'far@example.com', 'Maths')],
        holders: [{ id: 'a9', displayName: 'Zoya Far', email: 'far@example.com', phoneNumber: null, status: 'Active' }],
      });

      expect(titles(root)).toEqual(['Asha Rao', 'Zoya Far']);
      expect(root.textContent).not.toContain('Not registered yet');
      expect(rowFor(root, 'Zoya Far').textContent).toContain('1 invitation');
    });

    it('makes no lookup when every invited address already has a listed account', () => {
      const { root } = open({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths')] });

      httpMock.expectNone(isLookup);
      expect(titles(root)).toEqual(['Asha Rao']);
    });
  });

  describe('opening a student', () => {
    it('shows their profile and invitations, and folds them away again', () => {
      const { fixture, root } = open({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths Final')] }, ['student.read', 'invite.manage']);
      const toggle = toggleOf(root, 'Asha Rao');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(root.querySelector('app-student-profile')).toBeNull();

      toggle.click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/admin/students/a1')).flush({
        ...asha,
        dateOfBirth: '2000-01-01',
        ageBand: 'Adult',
      });
      fixture.detectChanges();

      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(root.querySelector(`[id="${toggle.getAttribute('aria-controls')}"] app-student-profile`)).not.toBeNull();
      expect(root.textContent).toContain('Maths Final');

      toggle.click();
      fixture.detectChanges();
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(root.querySelector('app-student-profile')).toBeNull();
    });

    it('keeps the profile out of the open row for staff who may manage students: it is edited in the popup instead', () => {
      const { fixture, root } = open({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths Final')] });

      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();

      httpMock.expectNone((r) => r.url.endsWith('/v1/admin/students/a1'));
      expect(root.querySelector('app-student-profile')).toBeNull();
      expect(rowFor(root, 'Asha Rao').textContent).toContain('Maths Final');
    });

    it('shows a read-only profile to staff who may not manage students', () => {
      const { fixture, root } = open({ students: [asha] }, ['student.read', 'invite.manage']);
      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.url.endsWith('/v1/admin/students/a1')).flush({ ...asha, dateOfBirth: '2000-01-01', ageBand: 'Adult' });
      fixture.detectChanges();

      expect(root.querySelector('app-student-profile form')).toBeNull();
      expect(root.querySelector('app-student-profile dl')?.textContent).toContain('2000-01-01');
    });

    it('reads the list again after the profile is saved, so the header shows the new details', () => {
      const { fixture, root } = open({ students: [asha] });
      openEditPopup(fixture, root, 'Asha Rao');

      const name = root.querySelector<HTMLInputElement>('#student-name-a1')!;
      name.value = 'Asha R. Rao';
      name.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Save changes')!.click();
      httpMock
        .expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/admin/students/a1'))
        .flush({ student: { ...asha, displayName: 'Asha R. Rao', dateOfBirth: '2000-01-01', ageBand: 'Adult' }, changed: ['name'], invitationsMoved: 0, signedOut: false });
      answerRead({ students: [{ ...asha, displayName: 'Asha R. Rao' }] });
      fixture.detectChanges();

      expect(titles(root)).toEqual(['Asha R. Rao']);
      // The popup stays open, with the form the staff member was using, and says what happened.
      expect(root.querySelector('app-modal-dialog')).not.toBeNull();
      expect(root.querySelector('app-modal-dialog app-student-profile [role="status"]')?.textContent).toContain('Saved: name.');
    });

    it('keeps the student on the page, with what was done to them, when their new address no longer matches the search', () => {
      vi.useFakeTimers();
      const { fixture, root } = open({ students: [asha] });
      const search = root.querySelector<HTMLInputElement>('#student-search')!;
      search.value = 'asha@example.com';
      search.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      httpMock.expectOne(isStudents).flush([asha]);
      httpMock.expectOne(isInvites).flush([]);
      fixture.detectChanges();
      openEditPopup(fixture, root, 'Asha Rao');

      const email = root.querySelector<HTMLInputElement>('#student-email-a1')!;
      email.value = 'other@example.org';
      email.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Save changes')!.click();
      fixture.detectChanges();
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Confirm and save')!.click();
      httpMock
        .expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/admin/students/a1'))
        .flush({
          student: { ...asha, email: 'other@example.org', dateOfBirth: '2000-01-01', ageBand: 'Adult' },
          changed: ['email'],
          invitationsMoved: 0,
          signedOut: true,
        });
      // The search still says the old address, so the API no longer finds the student.
      httpMock.expectOne(isStudents).flush([]);
      httpMock.expectOne(isInvites).flush([]);
      fixture.detectChanges();

      expect(titles(root)).toEqual(['Asha Rao']);
      expect(root.querySelector('app-modal-dialog app-student-profile [role="status"]')?.textContent).toContain('Saved: e-mail address.');

      // A new search starts the list afresh.
      search.value = 'nobody';
      search.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      httpMock.expectOne(isStudents).flush([]);
      httpMock.expectOne(isInvites).flush([]);
      fixture.detectChanges();
      expect(titles(root)).toEqual([]);
    });

    it('reads the list again after an invitation is revoked, once the question has been answered yes', async () => {
      const { fixture, root } = open({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths Final')] });
      vi.spyOn(TestBed.inject(ConfirmService), 'ask').mockResolvedValue(true);
      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();

      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Revoke')!.click();
      await Promise.resolve();
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/1/revoke')).flush(null, { status: 204, statusText: 'No Content' });
      answerRead({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths Final', 'Revoked')] });
      fixture.detectChanges();

      expect(toggleOf(root, 'Asha Rao').getAttribute('aria-expanded')).toBe('true');
      expect(toggleOf(root, 'Asha Rao').textContent).toContain('1 Revoked');
    });
  });

  describe('the edit icon', () => {
    it('opens the profile in a popup, with the form, and leaves the row as it was', () => {
      const { fixture, root } = open({ students: [asha] });
      expect(root.querySelector('app-modal-dialog')).toBeNull();

      openEditPopup(fixture, root, 'Asha Rao');

      const popup = root.querySelector('app-modal-dialog')!;
      expect(popup.querySelector('.modal__title')?.textContent?.trim()).toBe('Edit profile');
      expect(popup.querySelector('app-student-profile form')).not.toBeNull();
      expect(toggleOf(root, 'Asha Rao').getAttribute('aria-expanded')).toBe('false');
    });

    it('takes the popup away when it is closed, and asks for nothing more', () => {
      const { fixture, root } = open({ students: [asha] });
      openEditPopup(fixture, root, 'Asha Rao');

      root.querySelector<HTMLButtonElement>('app-modal-dialog .modal__close')!.click();
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog')).toBeNull();
    });

    it('is on every student the staff member may manage, and not on a student they may only read', () => {
      const writer = open({ students: [asha, ben] });
      expect(iconOf(writer.root, 'Asha Rao', 'Edit')).not.toBeNull();
      expect(iconOf(writer.root, 'Ben Das', 'Edit')).not.toBeNull();
    });

    it('is not offered to staff who may only read students', () => {
      const { root } = open({ students: [asha] }, ['student.read', 'invite.manage']);

      expect(iconOf(root, 'Asha Rao', 'Edit')).toBeNull();
      expect(iconOf(root, 'Asha Rao', 'Deactivate')).toBeNull();
    });

    it('is not on an invited address that has no account, since there is no profile to edit', () => {
      const { root } = open({ invites: [invite('1', 'newcomer@example.com', 'Maths Final')] });

      expect(iconOf(root, 'newcomer@example.com', 'Edit')).toBeNull();
      expect(iconOf(root, 'newcomer@example.com', 'Deactivate')).toBeNull();
    });
  });

  describe('the deactivate icon', () => {
    const deactivateUrl = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/admin/students/a1/deactivate');

    it('asks first, and does nothing when the staff member cancels', () => {
      const { fixture, root } = open({ students: [asha] });

      iconOf(root, 'Asha Rao', 'Deactivate')!.click();
      fixture.detectChanges();

      const popup = root.querySelector('app-modal-dialog')!;
      expect(popup.querySelector('.modal__title')?.textContent).toContain('Deactivate Asha Rao?');
      expect(popup.textContent).toContain('signed out');
      expect(popup.textContent).toContain('results, certificates and records are kept');
      httpMock.expectNone(deactivateUrl);

      buttonNamed(popup as HTMLElement, 'Cancel')!.click();
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog')).toBeNull();
      httpMock.expectNone(deactivateUrl);
    });

    it('deactivates once confirmed, says so above the list, and reads the list again', () => {
      const { fixture, root } = open({ students: [asha] });
      iconOf(root, 'Asha Rao', 'Deactivate')!.click();
      fixture.detectChanges();

      buttonNamed(root, 'Deactivate')!.click();
      const request = httpMock.expectOne(deactivateUrl);
      expect(request.request.body).toBeNull();
      request.flush({ ...profileOf(asha), status: 'Deactivated' });
      answerRead({ students: [{ ...asha, status: 'Deactivated' }] });
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog')).toBeNull();
      expect(root.querySelector('.success-message')?.textContent).toContain('Deactivated Asha Rao.');
      expect(toggleOf(root, 'Asha Rao').textContent).toContain('Deactivated');
      // Nothing is left to deactivate: it can be turned back on instead, and the profile can still be corrected.
      expect(iconOf(root, 'Asha Rao', 'Deactivate')).toBeNull();
      expect(iconOf(root, 'Asha Rao', 'Reactivate')).not.toBeNull();
      expect(iconOf(root, 'Asha Rao', 'Edit')).not.toBeNull();
    });

    it('keeps the popup open and says why when the account could not be deactivated', () => {
      const { fixture, root } = open({ students: [asha] });
      iconOf(root, 'Asha Rao', 'Deactivate')!.click();
      fixture.detectChanges();

      buttonNamed(root, 'Deactivate')!.click();
      httpMock
        .expectOne(deactivateUrl)
        .flush({ title: 'user_not_found', detail: 'There is no such student.', status: 404 }, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog .error-message')?.textContent).toContain('There is no such student.');
      expect(root.querySelector('app-modal-dialog')).not.toBeNull();
      expect(buttonNamed(root, 'Deactivate')!.disabled).toBe(false);
      httpMock.expectNone(isStudents);
    });

    it('is not offered again for an account that is already deactivated', () => {
      const { root } = open({ students: [{ ...asha, status: 'Deactivated' }] });

      expect(iconOf(root, 'Asha Rao', 'Deactivate')).toBeNull();
    });

    it('says the account can be turned back on later', () => {
      const { fixture, root } = open({ students: [asha] });

      iconOf(root, 'Asha Rao', 'Deactivate')!.click();
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog')?.textContent).toContain('turn the account back on');
    });
  });

  describe('the reactivate icon', () => {
    const deactivated = { ...asha, status: 'Deactivated' };
    const reactivateUrl = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/admin/students/a1/reactivate');

    it('stands in for the deactivate icon on a deactivated account, and on no other', () => {
      const { root } = open({ students: [deactivated, ben, { ...ben, id: 'a3', displayName: 'Cara Roy', status: 'Suspended' }] });

      expect(iconOf(root, 'Asha Rao', 'Reactivate')).not.toBeNull();
      expect(iconOf(root, 'Asha Rao', 'Deactivate')).toBeNull();
      expect(iconOf(root, 'Ben Das', 'Reactivate')).toBeNull();
      expect(iconOf(root, 'Ben Das', 'Deactivate')).not.toBeNull();
      expect(iconOf(root, 'Cara Roy', 'Reactivate')).toBeNull();
    });

    it('is not offered to staff who may only read students', () => {
      const { root } = open({ students: [deactivated] }, ['student.read', 'invite.manage']);

      expect(iconOf(root, 'Asha Rao', 'Reactivate')).toBeNull();
    });

    it('turns the account back on at once, says so above the list, and swaps the icon back', () => {
      const { fixture, root } = open({ students: [deactivated] });

      iconOf(root, 'Asha Rao', 'Reactivate')!.click();
      const request = httpMock.expectOne(reactivateUrl);
      expect(request.request.body).toBeNull();
      request.flush({ ...profileOf(asha), status: 'Active' });
      answerRead({ students: [asha] });
      fixture.detectChanges();

      expect(root.querySelector('.success-message')?.textContent).toContain('Reactivated Asha Rao.');
      expect(toggleOf(root, 'Asha Rao').textContent).not.toContain('Deactivated');
      expect(iconOf(root, 'Asha Rao', 'Reactivate')).toBeNull();
      expect(iconOf(root, 'Asha Rao', 'Deactivate')).not.toBeNull();
    });

    it('sends one request even if pressed again while the answer is on its way', () => {
      const { fixture, root } = open({ students: [deactivated] });
      const icon = iconOf(root, 'Asha Rao', 'Reactivate')!;

      icon.click();
      fixture.detectChanges();
      // Unavailable, not switched off: it stays where it is and can still be reached to read why.
      expect(icon.getAttribute('aria-disabled')).toBe('true');
      icon.click();

      httpMock.expectOne(reactivateUrl).flush({ ...profileOf(asha), status: 'Active' });
      answerRead({ students: [asha] });
    });

    it('says why when the account could not be turned back on, and leaves the icon to try again', () => {
      const { fixture, root } = open({ students: [deactivated] });

      iconOf(root, 'Asha Rao', 'Reactivate')!.click();
      httpMock
        .expectOne(reactivateUrl)
        .flush({ title: 'user_not_found', detail: 'There is no such student.', status: 404 }, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();

      expect(root.querySelector('.error-message')?.textContent).toContain('There is no such student.');
      expect(root.querySelector('.success-message')).toBeNull();
      expect(iconOf(root, 'Asha Rao', 'Reactivate')!.hasAttribute('aria-disabled')).toBe(false);
      httpMock.expectNone(isStudents);
    });
  });

  describe('the row actions for a touch screen', () => {
    it('are in the open row, in words, and not before it is opened', () => {
      const { fixture, root } = open({ students: [asha] });
      expect(wordsOf(root, 'Asha Rao', 'Edit')).toBeNull();

      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();

      expect(wordsOf(root, 'Asha Rao', 'Edit')?.textContent?.trim()).toBe('Edit');
      expect(wordsOf(root, 'Asha Rao', 'Deactivate')?.textContent?.trim()).toBe('Deactivate');
      expect(wordsOf(root, 'Asha Rao', 'Deactivate')?.classList.contains('btn--danger')).toBe(true);
    });

    it('do the same as the icons: Deactivate asks first', () => {
      const { fixture, root } = open({ students: [asha] });
      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();

      wordsOf(root, 'Asha Rao', 'Deactivate')!.click();
      fixture.detectChanges();

      expect(root.querySelector('app-modal-dialog .modal__title')?.textContent).toContain('Deactivate Asha Rao?');
    });

    it('offer Reactivate, not Deactivate, for an account that was deactivated', () => {
      const { fixture, root } = open({ students: [{ ...asha, status: 'Deactivated' }] });
      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();

      expect(wordsOf(root, 'Asha Rao', 'Reactivate')).not.toBeNull();
      expect(wordsOf(root, 'Asha Rao', 'Deactivate')).toBeNull();
    });

    it('are not in the open row for staff who may only read students', () => {
      const { fixture, root } = open({ students: [asha] }, ['student.read', 'invite.manage']);
      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();
      // A row that cannot be edited shows the profile in itself, which is read when it opens.
      httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/admin/students/a1')).flush(profileOf(asha));

      expect(rowFor(root, 'Asha Rao').querySelector('.student-row__panel .row-actions--words')).toBeNull();
    });
  });

  it('tags a status with the shared badge: a deactivated account in the danger tone, a pending invitation as a warning', () => {
    const { root } = open({ students: [{ ...asha, status: 'Deactivated' }], invites: [invite('1', 'asha@example.com', 'Maths')] });

    const tones = Array.from(rowFor(root, 'Asha Rao').querySelectorAll('.student-row__statuses .status-badge')).map((b) => [b.getAttribute('data-tone'), b.textContent?.replace(/\s+/g, ' ').trim()]);
    expect(tones).toEqual([
      ['danger', '✕ Deactivated'],
      ['warning', '! 1 Pending'],
    ]);
  });

  describe('an address nobody has registered with', () => {
    const invitedOnly = { invites: [invite('1', 'newcomer@example.com', 'Maths Final')] };

    it('shows its invitations and a way to correct the address, but no profile', () => {
      const { fixture, root } = open(invitedOnly);

      toggleOf(root, 'newcomer@example.com').click();
      fixture.detectChanges();

      const row = rowFor(root, 'newcomer@example.com');
      expect(row.textContent).toContain('Maths Final');
      expect(row.querySelector('app-invited-address')).not.toBeNull();
      expect(row.querySelector('app-student-profile')).toBeNull();
    });

    it('moves the invitations to the corrected address, says so above the list, and reads the list again', () => {
      const { fixture, root } = open(invitedOnly);
      toggleOf(root, 'newcomer@example.com').click();
      fixture.detectChanges();

      const field = root.querySelector<HTMLInputElement>('app-invited-address input[type="email"]')!;
      field.value = 'right@example.com';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      root.querySelector<HTMLButtonElement>('app-invited-address button[type="submit"]')!.click();
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/change-email')).flush({ changed: 1 });
      answerRead({ invites: [invite('1', 'right@example.com', 'Maths Final')] });
      fixture.detectChanges();

      expect(root.querySelector('.success-message')?.textContent).toContain('Moved 1 invitation from newcomer@example.com to right@example.com.');
      expect(titles(root)).toEqual(['right@example.com']);
    });

    it('is not offered to staff who may not manage invitations', () => {
      const { fixture, root } = open({}, ['student.read']);

      expect(root.textContent).not.toContain('Invite a candidate');
      httpMock.expectNone(isInvites);
      expect(fixture.componentInstance).toBeTruthy();
    });
  });

  describe('searching', () => {
    beforeEach(() => vi.useFakeTimers());

    function typeSearch(fixture: ReturnType<typeof open>['fixture'], text: string) {
      const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#student-search')!;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    }

    it('waits for a pause in typing, then asks the API with what was typed', () => {
      const { fixture } = open({ students: [asha, ben] });

      typeSearch(fixture, 'b');
      typeSearch(fixture, 'be');
      typeSearch(fixture, 'ben');
      httpMock.expectNone(isStudents);
      vi.advanceTimersByTime(300);

      const request = httpMock.expectOne(isStudents);
      expect(request.request.params.get('search')).toBe('ben');
      request.flush([ben]);
      httpMock.expectOne(isInvites).flush([]);
      fixture.detectChanges();

      expect(titles(fixture.nativeElement)).toEqual(['Ben Das']);
    });

    it('keeps the invited addresses that contain the search and drops the others', () => {
      const { fixture } = open({ students: [], invites: [invite('1', 'ben.invited@example.com', 'Maths'), invite('2', 'asha.invited@example.com', 'Maths')] });
      expect(titles(fixture.nativeElement)).toHaveLength(2);

      typeSearch(fixture, 'ASHA');
      vi.advanceTimersByTime(300);
      httpMock.expectOne(isStudents).flush([]);
      httpMock.expectOne(isInvites).flush([invite('1', 'ben.invited@example.com', 'Maths'), invite('2', 'asha.invited@example.com', 'Maths')]);
      httpMock.match(isLookup).forEach((lookup) => lookup.flush([]));
      fixture.detectChanges();

      expect(titles(fixture.nativeElement)).toEqual(['asha.invited@example.com']);
    });

    it('counts only the invitations of the students the search leaves on the page, not everything the API holds', () => {
      const { fixture } = open({ students: [asha, ben] });

      typeSearch(fixture, 'asha');
      vi.advanceTimersByTime(300);
      httpMock.expectOne(isStudents).flush([asha]);
      httpMock.expectOne(isInvites).flush([invite('1', 'asha@example.com', 'Maths'), invite('2', 'ben@example.com', 'Maths'), invite('3', 'ben@example.com', 'Physics')]);
      // Ben holds an account the search did not match, and his address does not contain what was typed.
      httpMock.expectOne(isLookup).flush([ben]);
      fixture.detectChanges();

      expect(titles(fixture.nativeElement)).toEqual(['Asha Rao']);
      expect((fixture.nativeElement as HTMLElement).querySelector('.student-summary')?.textContent?.trim()).toBe('1 student · 1 invitation pending');
    });

    it('says nothing matched, rather than showing an empty page', () => {
      const { fixture } = open({ students: [asha] });

      typeSearch(fixture, 'nobody');
      vi.advanceTimersByTime(300);
      httpMock.expectOne(isStudents).flush([]);
      httpMock.expectOne(isInvites).flush([]);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('No students match that search.');
    });

    it('lets a newer search replace one still on its way, so a slow old answer cannot win', () => {
      const { fixture } = open({ students: [asha, ben] });

      typeSearch(fixture, 'asha');
      vi.advanceTimersByTime(300);
      const first = httpMock.expectOne(isStudents);
      const firstInvites = httpMock.expectOne(isInvites);

      typeSearch(fixture, 'ben');
      vi.advanceTimersByTime(300);
      expect(first.cancelled).toBe(true);
      expect(firstInvites.cancelled).toBe(true);
      httpMock.expectOne(isStudents).flush([ben]);
      httpMock.expectOne(isInvites).flush([]);
      fixture.detectChanges();

      expect(titles(fixture.nativeElement)).toEqual(['Ben Das']);
    });
  });

  it('says there are no students when there are none and nothing was searched', () => {
    const { root } = open({});

    expect(root.textContent).toContain('No students yet.');
  });

  it('warns that the list stopped at 200 when it did', () => {
    const many = Array.from({ length: 200 }, (_, n) => ({ id: `s${n}`, displayName: `Student ${n}`, email: `s${n}@example.com`, phoneNumber: null, status: 'Active' }));
    const { root } = open({ students: many });

    expect(root.textContent).toContain('Showing the first 200 students by name.');
    // Drawing 200 rows takes a few seconds when the whole suite is running at once; the default five is not enough on a busy machine.
  }, 30_000);

  describe('the refresh icon', () => {
    const icon = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('app-refresh-button button')!;

    it('reads the students and invitations again without blanking the list, and shows a student who registered since', () => {
      const { fixture, root } = open({ students: [asha] });

      icon(root).click();
      fixture.detectChanges();
      expect(icon(root).getAttribute('aria-busy')).toBe('true');
      expect(titles(root)).toEqual(['Asha Rao']);
      expect(root.textContent).not.toContain('Loading');

      answerRead({ students: [asha, ben] });
      fixture.detectChanges();

      expect(titles(root)).toEqual(['Asha Rao', 'Ben Das']);
      expect(icon(root).hasAttribute('aria-busy')).toBe(false);
    });

    it('keeps a row that is open, open', () => {
      const { fixture, root } = open({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths Final')] }, ['student.read', 'invite.manage']);
      toggleOf(root, 'Asha Rao').click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/admin/students/a1')).flush({ ...asha, dateOfBirth: '2000-01-01', ageBand: 'Adult' });
      fixture.detectChanges();

      icon(root).click();
      answerRead({ students: [asha], invites: [invite('1', 'asha@example.com', 'Maths Final'), invite('2', 'asha@example.com', 'Physics')] });
      fixture.detectChanges();

      expect(toggleOf(root, 'Asha Rao').getAttribute('aria-expanded')).toBe('true');
      expect(root.textContent).toContain('Physics');
    });

    it('turns while a change reads the list again too, and is pressable once that is over', () => {
      const { fixture, root } = open({ students: [asha] });
      iconOf(root, 'Asha Rao', 'Deactivate')!.click();
      fixture.detectChanges();
      buttonNamed(root, 'Deactivate')!.click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/admin/students/a1/deactivate')).flush({ ...asha, status: 'Deactivated' });
      fixture.detectChanges();

      expect(icon(root).getAttribute('aria-busy')).toBe('true');
      answerRead({ students: [{ ...asha, status: 'Deactivated' }] });
      fixture.detectChanges();
      expect(icon(root).hasAttribute('aria-busy')).toBe(false);
    });

    it('keeps the list and says why when the read fails, and clears the reason when it works', () => {
      const { fixture, root } = open({ students: [asha] });

      icon(root).click();
      httpMock.expectOne(isStudents).flush({ title: 'server_error', detail: 'The list is not available.' }, { status: 500, statusText: 'Server Error' });
      httpMock.match(isInvites).forEach((r) => r.cancelled || r.flush([]));
      fixture.detectChanges();

      expect(titles(root)).toEqual(['Asha Rao']);
      expect(root.querySelector('[role="alert"]')?.textContent).toContain('The list is not available.');
      expect(icon(root).hasAttribute('aria-busy')).toBe(false);

      icon(root).click();
      answerRead({ students: [asha] });
      fixture.detectChanges();
      expect(root.querySelector('[role="alert"]')).toBeNull();
    });
  });

  it("shows the API's message when the list cannot be read", () => {
    configure(['student.read', 'invite.manage']);
    const fixture = TestBed.createComponent(StudentList);
    fixture.detectChanges();
    httpMock.expectOne(isStudents).flush({ title: 'forbidden', detail: 'You may not see students.' }, { status: 403, statusText: 'Forbidden' });
    httpMock.match(isInvites).forEach((r) => r.cancelled || r.flush([]));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent).toContain('You may not see students.');
  });
});
