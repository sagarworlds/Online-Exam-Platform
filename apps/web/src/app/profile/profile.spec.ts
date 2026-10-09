import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UserProfileDto } from '../auth/auth.models';
import { Profile } from './profile';

describe('Profile', () => {
  let httpMock: HttpTestingController;

  const account = (overrides: Partial<UserProfileDto> = {}): UserProfileDto => ({
    userId: 'u1',
    email: 'a@b.com',
    phoneNumber: null,
    displayName: 'Ada',
    status: 'Active',
    roles: ['Candidate'],
    ...overrides,
  });

  const isGet = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/me/profile');
  const isPut = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/me/profile');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Profile],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(details: UserProfileDto = account()): ComponentFixture<Profile> {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    httpMock.expectOne(isGet).flush(details);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<Profile>) => fixture.nativeElement as HTMLElement;
  const nameInput = (fixture: ComponentFixture<Profile>) => root(fixture).querySelector('#profile-display-name') as HTMLInputElement;
  const saveButton = (fixture: ComponentFixture<Profile>) => root(fixture).querySelector('form button[type="submit"]') as HTMLButtonElement;

  /** Types a name and presses Save, the way a candidate does. */
  function typeAndSave(fixture: ComponentFixture<Profile>, name: string): void {
    nameInput(fixture).value = name;
    nameInput(fixture).dispatchEvent(new Event('input'));
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  it('says the profile is loading, for a screen reader', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain('Loading…');
    httpMock.expectOne(isGet).flush(account());
  });

  it('shows the account facts, with the status and roles in words the candidate can read', () => {
    const fixture = open(account({ roles: ['ExamAdmin', 'Candidate'], status: 'PendingVerification' }));
    const text = root(fixture).textContent ?? '';

    expect(text).toContain('Waiting for verification');
    expect(text).toContain('Exam administrator, Candidate');
    expect(text).toContain('a@b.com');
  });

  it('keeps the name first, since it is the one thing this page changes', () => {
    const fixture = open();
    const form = root(fixture).querySelector('form') as HTMLElement;
    const accountHeading = root(fixture).querySelector('#account-heading') as HTMLElement;

    expect(form.compareDocumentPosition(accountHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('says on the page that only the display name changes here', () => {
    const fixture = open();

    expect(root(fixture).textContent).toContain('This page changes your display name only.');
  });

  it('leaves Save enabled, and names a blank name only once Save is pressed, sending nothing', () => {
    const fixture = open();
    expect(saveButton(fixture).disabled).toBe(false);
    expect(root(fixture).querySelector('#profile-name-error')).toBeNull();

    typeAndSave(fixture, '   ');

    expect(root(fixture).querySelector('#profile-name-error')?.textContent).toContain('Enter a display name.');
    expect(nameInput(fixture).getAttribute('aria-invalid')).toBe('true');
    httpMock.expectNone(isPut);
  });

  it('names a name over the limit once Save is pressed, and sends nothing', () => {
    const fixture = open();

    typeAndSave(fixture, 'x'.repeat(201));

    expect(root(fixture).querySelector('#profile-name-error')?.textContent).toContain('Display name must be 200 characters or fewer.');
    httpMock.expectNone(isPut);
  });

  it('saves the trimmed name, and says so as a status', () => {
    const fixture = open();

    typeAndSave(fixture, '  Ada Lovelace  ');
    const put = httpMock.expectOne(isPut);
    expect(put.request.body).toEqual({ displayName: 'Ada Lovelace' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    const saved = root(fixture).querySelector('.success-message');
    expect(saved?.textContent).toContain('Saved.');
    expect(saved?.getAttribute('role')).toBe('status');
  });

  it('announces a refused save as an alert above Save, and keeps the name typed', () => {
    const fixture = open();

    typeAndSave(fixture, 'Grace');
    httpMock
      .expectOne(isPut)
      .flush({ title: 'profile_failed', detail: 'The display name could not be saved. Try again.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const alert = root(fixture).querySelector('form [role="alert"]');
    expect(alert?.textContent).toContain('could not be saved');
    expect(alert?.nextElementSibling?.tagName).toBe('BUTTON');
    expect(nameInput(fixture).value).toBe('Grace');
    expect(saveButton(fixture).disabled).toBe(false);
  });

  it('shows the error alone when the account cannot be loaded, with no form', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    httpMock.expectOne(isGet).flush({ title: 'unavailable', detail: 'Server unavailable.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('Server unavailable.');
    expect(root(fixture).querySelector('form')).toBeNull();
  });
});
