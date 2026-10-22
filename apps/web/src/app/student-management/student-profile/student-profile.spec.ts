import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { StudentProfile } from './student-profile';

describe('StudentProfile', () => {
  let httpMock: HttpTestingController;

  const saved = {
    id: 's1',
    displayName: 'Asha Rao',
    email: 'asha@example.com',
    phoneNumber: '+919800000000',
    dateOfBirth: '2000-01-01',
    ageBand: 'Adult',
    status: 'Active',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StudentProfile],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const isStudent = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/admin/students/s1');
  const isUpdate = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/admin/students/s1');

  function open(canEdit = true) {
    const fixture = TestBed.createComponent(StudentProfile);
    fixture.componentRef.setInput('studentId', 's1');
    fixture.componentRef.setInput('canEdit', canEdit);
    const savedEvents = vi.fn();
    fixture.componentInstance.saved.subscribe(savedEvents);
    fixture.detectChanges();
    httpMock.expectOne(isStudent).flush(saved);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, savedEvents };
  }

  const field = (root: HTMLElement, name: 'name' | 'email' | 'phone' | 'dob') => root.querySelector<HTMLInputElement>(`#student-${name}-s1`)!;
  const button = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;

  function type(fixture: ReturnType<typeof open>['fixture'], input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  const result = (over: Record<string, unknown> = {}) => ({
    student: saved,
    changed: ['name'],
    invitationsMoved: 0,
    signedOut: false,
    ...over,
  });

  it('shows the saved details in the form', () => {
    const { root } = open();

    expect(field(root, 'name').value).toBe('Asha Rao');
    expect(field(root, 'email').value).toBe('asha@example.com');
    expect(field(root, 'phone').value).toBe('+919800000000');
    expect(field(root, 'dob').value).toBe('2000-01-01');
    expect(root.textContent).toContain('Now an adult.');
  });

  it('shows the details to read, with no form, to staff who may not change them', () => {
    const { root } = open(false);

    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('asha@example.com');
    expect(root.textContent).toContain('2000-01-01 (Adult)');
  });

  it('says why when the student cannot be read', () => {
    const fixture = TestBed.createComponent(StudentProfile);
    fixture.componentRef.setInput('studentId', 's1');
    fixture.detectChanges();

    httpMock.expectOne(isStudent).flush({ title: 'user_not_found', detail: 'No matching user was found.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent).toContain('No matching user was found.');
  });

  it('keeps Save changes off until something is different', () => {
    const { fixture, root } = open();
    expect(button(root, 'Save changes')!.disabled).toBe(true);

    type(fixture, field(root, 'name'), 'Asha R. Rao');
    expect(button(root, 'Save changes')!.disabled).toBe(false);

    type(fixture, field(root, 'name'), 'Asha Rao');
    expect(button(root, 'Save changes')!.disabled).toBe(true);
  });

  describe('a change that needs no second click', () => {
    it('saves a new name at once, sends the whole profile, and says what it did', () => {
      const { fixture, root, savedEvents } = open();
      type(fixture, field(root, 'name'), '  Asha R. Rao ');

      button(root, 'Save changes')!.click();
      const put = httpMock.expectOne(isUpdate);
      expect(put.request.body).toEqual({ displayName: 'Asha R. Rao', email: 'asha@example.com', phoneNumber: '+919800000000', dateOfBirth: '2000-01-01' });
      put.flush(result({ student: { ...saved, displayName: 'Asha R. Rao' } }));
      fixture.detectChanges();

      expect(root.querySelector('[role="status"]')?.textContent).toContain('Saved: name.');
      expect(field(root, 'name').value).toBe('Asha R. Rao');
      expect(savedEvents).toHaveBeenCalledTimes(1);
      expect(button(root, 'Save changes')!.disabled).toBe(true);
    });

    it('sends an emptied email address as none, not as an empty string', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'email'), '  ');

      // Dropping the address signs the student out, so it is asked about first.
      button(root, 'Save changes')!.click();
      fixture.detectChanges();
      button(root, 'Confirm and save')!.click();

      expect(httpMock.expectOne(isUpdate).request.body.email).toBeNull();
    });
  });

  describe('a change that signs the student out', () => {
    it('asks first, saying what will happen, and sends nothing until the staff member agrees', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'email'), 'asha.rao@example.org');

      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      httpMock.expectNone(isUpdate);
      const warning = root.querySelector('.profile-confirm')!;
      expect(warning.textContent).toContain('signed out');
      expect(warning.textContent).toContain('invitations move');
      expect(warning.textContent).toContain('Nothing is sent to the old address');
      expect(button(root, 'Save changes')).toBeUndefined();
    });

    it('moves focus to the button that goes ahead', async () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'phone'), '+919811111111');

      button(root, 'Save changes')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(button(root, 'Confirm and save'));
    });

    it('goes back to editing when asked, with nothing sent and the typed address kept', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'email'), 'asha.rao@example.org');
      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      button(root, 'Keep editing')!.click();
      fixture.detectChanges();

      httpMock.expectNone(isUpdate);
      expect(root.querySelector('.profile-confirm')).toBeNull();
      expect(field(root, 'email').value).toBe('asha.rao@example.org');
    });

    it('asks again when the form is edited after being asked, since that is a different change', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'email'), 'asha.rao@example.org');
      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      type(fixture, field(root, 'email'), 'someone.else@example.org');

      expect(root.querySelector('.profile-confirm')).toBeNull();
      expect(button(root, 'Save changes')).toBeDefined();
    });

    it('saves on agreeing, then says the student was signed out and how many invitations moved', () => {
      const { fixture, root, savedEvents } = open();
      type(fixture, field(root, 'email'), 'asha.rao@example.org');
      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      button(root, 'Confirm and save')!.click();
      httpMock
        .expectOne(isUpdate)
        .flush(result({ student: { ...saved, email: 'asha.rao@example.org' }, changed: ['email'], invitationsMoved: 2, signedOut: true }));
      fixture.detectChanges();

      const notice = root.querySelector('[role="status"]')!.textContent!;
      expect(notice).toContain('Saved: email address.');
      expect(notice).toContain('2 invitations moved to the new email address.');
      expect(notice).toContain('The student was signed out.');
      expect(savedEvents).toHaveBeenCalledTimes(1);
      expect(root.querySelector('.profile-confirm')).toBeNull();
    });

    it('says one invitation, not "1 invitations"', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'email'), 'asha.rao@example.org');
      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      button(root, 'Confirm and save')!.click();
      httpMock.expectOne(isUpdate).flush(result({ changed: ['email'], invitationsMoved: 1, signedOut: true }));
      fixture.detectChanges();

      expect(root.querySelector('[role="status"]')!.textContent).toContain('1 invitation moved');
    });

    it('asks before a new mobile number too, without promising to move invitations', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'phone'), '+919811111111');

      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      const warning = root.querySelector('.profile-confirm')!.textContent!;
      expect(warning).toContain('new number');
      expect(warning).not.toContain('invitations move');
    });
  });

  describe('a date of birth', () => {
    it('asks first when it makes the student a minor, and says guardian consent will apply', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'dob'), '2015-03-01');

      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      httpMock.expectNone(isUpdate);
      expect(root.querySelector('.profile-confirm')!.textContent).toContain('a minor, so guardian consent applies');
    });

    it('saves a correction within the same band without asking', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'dob'), '1999-12-31');

      button(root, 'Save changes')!.click();

      expect(httpMock.expectOne(isUpdate).request.body.dateOfBirth).toBe('1999-12-31');
    });

    it('refuses a date in the future without asking the API', () => {
      const { fixture, root } = open();
      type(fixture, field(root, 'dob'), '2999-01-01');

      button(root, 'Save changes')!.click();
      fixture.detectChanges();

      httpMock.expectNone(isUpdate);
      expect(root.querySelector('.field-error')?.textContent).toContain('future');
    });
  });

  it('will not save a student with neither an email address nor a mobile number', () => {
    const { fixture, root } = open();
    type(fixture, field(root, 'email'), '');
    type(fixture, field(root, 'phone'), '');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Keep an email address or a mobile number');
    button(root, 'Save changes')!.click();
    fixture.detectChanges();

    httpMock.expectNone(isUpdate);
    expect(root.querySelector('.profile-confirm')).toBeNull();
  });

  it('will not save a blank name', () => {
    const { fixture, root } = open();
    type(fixture, field(root, 'name'), '   ');

    button(root, 'Save changes')!.click();
    fixture.detectChanges();

    httpMock.expectNone(isUpdate);
    expect(root.querySelector('.field-error')?.textContent).toContain('Enter a display name.');
  });

  it("shows the API's reason when it refuses, keeps what was typed, and lets the staff member try again", () => {
    const { fixture, root, savedEvents } = open();
    type(fixture, field(root, 'name'), 'Asha R. Rao');
    button(root, 'Save changes')!.click();

    httpMock
      .expectOne(isUpdate)
      .flush({ title: 'contact_in_use', detail: 'Another account already uses that email address.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Another account already uses that email address.');
    expect(field(root, 'name').value).toBe('Asha R. Rao');
    expect(button(root, 'Save changes')!.disabled).toBe(false);
    expect(savedEvents).not.toHaveBeenCalled();
  });

  it('does not send a second save while one is on its way', () => {
    const { fixture, root } = open();
    type(fixture, field(root, 'name'), 'Asha R. Rao');

    button(root, 'Save changes')!.click();
    const request = httpMock.expectOne(isUpdate);
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock.expectNone(isUpdate);
    request.flush(result());
  });
});
