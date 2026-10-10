import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { GuardianLink } from './guardian-link';

describe('GuardianLink', () => {
  const GUARDIANS_URL = `${environment.apiBaseUrl}/v1/guardians`;
  const LINKS_URL = `${environment.apiBaseUrl}/v1/guardians/guardian-1/links`;
  const GUARDIAN = { id: 'guardian-1', email: 'guardian@example.com', fullName: 'Gia Guardian', createdAt: '2026-10-10T00:00:00Z', updatedAt: '2026-10-10T00:00:00Z' };
  const LINK = { id: 'l1', guardianId: 'guardian-1', candidateId: 'c1', candidateEmail: 'candidate@example.com', status: 'Pending' };
  let httpMock: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  function open() {
    TestBed.configureTestingModule({
      imports: [GuardianLink],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(GuardianLink);
    fixture.detectChanges();
    return fixture;
  }

  /** Fills in the form as the staff member would and submits it. */
  function fillAndSubmit(fixture: ReturnType<typeof open>) {
    fixture.componentInstance.form.patchValue({
      guardianEmail: 'guardian@example.com',
      candidateId: 'c1',
      candidateEmail: 'candidate@example.com',
    });
    fixture.componentInstance.onSubmit();
  }

  /** Submits the form, answers the guardian lookup with a found guardian, and returns the request that links the candidate. */
  function submit(fixture: ReturnType<typeof open>) {
    fillAndSubmit(fixture);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === GUARDIANS_URL).flush(GUARDIAN);
    return httpMock.expectOne(LINKS_URL);
  }

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('says the request was e-mailed, and goes back to the guardians page', () => {
    const fixture = open();

    submit(fixture).flush({ link: LINK, consentRequestSent: true });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('The guardian has been e-mailed a request to confirm the link.');
    expect((fixture.nativeElement as HTMLElement).querySelector('#confirmLink')).toBeNull();
    vi.advanceTimersByTime(2000);
    expect(navigate).toHaveBeenCalledWith(['/guardian']);
  });

  it('shows the confirmation link to pass on when no e-mail went out, and stays on the page to keep it', () => {
    const fixture = open();

    submit(fixture).flush({ link: LINK, consentRequestSent: false, consentLink: 'https://app.example/guardian/confirm-link?token=abc' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.warning-message')?.textContent).toContain('could not be sent');
    expect((root.querySelector('#confirmLink') as HTMLTextAreaElement).value).toBe('https://app.example/guardian/confirm-link?token=abc');
    vi.advanceTimersByTime(2000);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('says so when no e-mail went out and no link came back either, rather than showing nothing', () => {
    const fixture = open();

    submit(fixture).flush({ link: LINK, consentRequestSent: false });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('no confirmation link was returned');
  });

  it('looks the guardian up by the address staff typed, and links the candidate to the guardian found', () => {
    const fixture = open();

    fillAndSubmit(fixture);
    const lookup = httpMock.expectOne((r) => r.method === 'GET' && r.url === GUARDIANS_URL);
    expect(lookup.request.params.get('email')).toBe('guardian@example.com');
    lookup.flush(GUARDIAN);
    httpMock.expectOne(LINKS_URL).flush({ link: LINK, consentRequestSent: true });
  });

  it('shows the reason when no guardian is registered with the address, and links nothing', () => {
    const fixture = open();

    fillAndSubmit(fixture);
    httpMock
      .expectOne((r) => r.method === 'GET' && r.url === GUARDIANS_URL)
      .flush(
        { title: 'guardian_not_found', detail: 'No guardian is registered with that e-mail address.' },
        { status: 404, statusText: 'Not Found' },
      );
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No guardian is registered with that e-mail address.');
    httpMock.expectNone((r) => r.method === 'POST');
  });
});
