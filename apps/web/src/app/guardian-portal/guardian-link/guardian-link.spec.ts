import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthSessionService } from '../../auth/auth-session.service';
import { GuardianLink } from './guardian-link';

describe('GuardianLink', () => {
  const LINKS_URL = `${environment.apiBaseUrl}/v1/guardians/guardian-1/links`;
  const LINK = { id: 'l1', guardianId: 'guardian-1', candidateId: 'c1', candidateEmail: 'candidate@example.com', status: 'Pending' };
  let httpMock: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  function open() {
    TestBed.configureTestingModule({
      imports: [GuardianLink],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthSessionService, useValue: { session: () => ({ userId: 'guardian-1' }) } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(GuardianLink);
    fixture.detectChanges();
    return fixture;
  }

  /** Fills in the form with the candidate and submits it, as the staff member would. */
  function submit(fixture: ReturnType<typeof open>) {
    fixture.componentInstance.form.patchValue({ candidateId: 'c1', candidateEmail: 'candidate@example.com' });
    fixture.componentInstance.onSubmit();
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
});
