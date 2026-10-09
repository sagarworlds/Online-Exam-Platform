import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthSessionService } from '../auth/auth-session.service';
import { Consent } from './consent';
import { ConsentPurpose, ConsentStatusDto } from './consent.models';

const PURPOSES: readonly ConsentPurpose[] = ['TermsOfService', 'PrivacyNotice', 'ProctoringDataProcessing'];

describe('Consent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Consent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthSessionService,
          useValue: { session: () => ({ userId: 'u1', sessionId: null, expiresAtUtc: 0 }) },
        },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const status = (purpose: ConsentPurpose, isActive: boolean): ConsentStatusDto => ({
    subjectId: 'u1',
    purpose,
    isActive,
    consentRecordId: isActive ? 'rec-1' : null,
    currentNoticeVersionId: 'notice-1',
  });

  /** Opens the page and answers the three status requests, in the order the page makes them. */
  function open(active: readonly boolean[] = [false, false, false]): ComponentFixture<Consent> {
    const fixture = TestBed.createComponent(Consent);
    fixture.detectChanges();
    httpMock.match(() => true).forEach((request, index) => request.flush(status(PURPOSES[index], active[index])));
    fixture.detectChanges();
    return fixture;
  }

  const card = (fixture: ComponentFixture<Consent>, purpose: ConsentPurpose) =>
    fixture.nativeElement.querySelector(`[aria-labelledby="consent-name-${purpose}"]`) as HTMLElement;
  const buttonIn = (element: HTMLElement, label: string) =>
    Array.from(element.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;

  it('explains each purpose on its own card, and offers to agree when it has not been agreed', () => {
    const fixture = open();

    for (const purpose of PURPOSES) {
      expect(card(fixture, purpose)).not.toBeNull();
      expect(buttonIn(card(fixture, purpose), 'Agree')).toBeDefined();
      expect(card(fixture, purpose).textContent).toContain('Not agreed');
    }
    expect(card(fixture, 'TermsOfService').textContent).toContain('The rules for using this platform');
    expect(card(fixture, 'ProctoringDataProcessing').textContent).toContain('camera or screen capture');
  });

  it('links the privacy notice to the notice itself, and no other purpose has a link', () => {
    const fixture = open();

    const link = card(fixture, 'PrivacyNotice').querySelector('a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/privacy.html');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(card(fixture, 'TermsOfService').querySelector('a')).toBeNull();
    expect(card(fixture, 'ProctoringDataProcessing').querySelector('a')).toBeNull();
  });

  it('shows Agreed and offers to withdraw once the candidate has agreed', () => {
    const fixture = open([true, false, false]);

    expect(card(fixture, 'TermsOfService').textContent).toContain('Agreed');
    expect(buttonIn(card(fixture, 'TermsOfService'), 'Withdraw agreement')).toBeDefined();
    expect(buttonIn(card(fixture, 'TermsOfService'), 'Agree')).toBeUndefined();
  });

  it('agrees only when the button is pressed, sending the current notice, then shows the new state', () => {
    const fixture = open();

    buttonIn(card(fixture, 'TermsOfService'), 'Agree')?.click();
    const grant = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/consent/'));
    expect(grant.request.body).toEqual({ subjectId: 'u1', purpose: 'TermsOfService', noticeVersionId: 'notice-1' });
    grant.flush({ consentRecordId: 'rec-1' });
    httpMock
      .expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/consent/status') && r.params.get('purpose') === 'TermsOfService')
      .flush(status('TermsOfService', true));
    fixture.detectChanges();

    expect(card(fixture, 'TermsOfService').textContent).toContain('Agreed');
  });

  it('withdraws an agreement using the record it was given, then shows Not agreed', () => {
    const fixture = open([true, false, false]);

    buttonIn(card(fixture, 'TermsOfService'), 'Withdraw agreement')?.click();
    httpMock.expectOne((r) => r.method === 'DELETE' && r.url.endsWith('/v1/consent/rec-1')).flush(null, { status: 204, statusText: 'No Content' });
    httpMock
      .expectOne((r) => r.method === 'GET' && r.params.get('purpose') === 'TermsOfService')
      .flush(status('TermsOfService', false));
    fixture.detectChanges();

    expect(card(fixture, 'TermsOfService').textContent).toContain('Not agreed');
  });

  it('shows Saving… on the button and holds it while the change is in flight, and marks the card busy', () => {
    const fixture = open();

    buttonIn(card(fixture, 'PrivacyNotice'), 'Agree')?.click();
    fixture.detectChanges();

    const busy = buttonIn(card(fixture, 'PrivacyNotice'), 'Saving…');
    expect(busy?.disabled).toBe(true);
    expect(card(fixture, 'PrivacyNotice').getAttribute('aria-busy')).toBe('true');
    httpMock.expectOne((r) => r.method === 'POST').flush({});
    httpMock.expectOne((r) => r.method === 'GET' && r.params.get('purpose') === 'PrivacyNotice').flush(status('PrivacyNotice', true));
  });

  it('announces a refused change as an alert on its own card, and leaves the other cards alone', () => {
    const fixture = open();

    buttonIn(card(fixture, 'ProctoringDataProcessing'), 'Agree')?.click();
    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush({ title: 'consent_failed', detail: 'The choice could not be saved. Try again.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(card(fixture, 'ProctoringDataProcessing').querySelector('[role="alert"]')?.textContent).toContain('could not be saved');
    expect(card(fixture, 'TermsOfService').querySelector('[role="alert"]')).toBeNull();
    expect(buttonIn(card(fixture, 'ProctoringDataProcessing'), 'Agree')?.disabled).toBe(false);
  });

  it('says the choice is not available yet, and sends nothing, when no notice exists to agree to', () => {
    const fixture = TestBed.createComponent(Consent);
    fixture.detectChanges();
    httpMock.match(() => true).forEach((request, index) =>
      request.flush({ ...status(PURPOSES[index], false), currentNoticeVersionId: null }),
    );
    fixture.detectChanges();

    buttonIn(card(fixture, 'TermsOfService'), 'Agree')?.click();
    fixture.detectChanges();

    expect(card(fixture, 'TermsOfService').querySelector('[role="alert"]')?.textContent).toContain('not available yet');
    httpMock.expectNone((r) => r.method === 'POST');
  });
});
