import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { GuardianConfirmLink } from './guardian-confirm-link';

describe('GuardianConfirmLink', () => {
  const VERIFY_URL = `${environment.apiBaseUrl}/v1/guardian-links/verify`;
  let httpMock: HttpTestingController;

  /** Opens the page with the given code in its link, or with no code when none is given. */
  function open(token?: string) {
    TestBed.configureTestingModule({
      imports: [GuardianConfirmLink],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(token === undefined ? {} : { token }) } },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(GuardianConfirmLink);
    fixture.detectChanges();
    return fixture;
  }

  const button = (root: HTMLElement) => root.querySelector('button') as HTMLButtonElement | null;

  afterEach(() => {
    httpMock.verify();
  });

  it('asks for the link from the e-mail, and sends nothing, when the page has no code', () => {
    const fixture = open();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('needs the confirmation link from the e-mail');
    expect(button(root)).toBeNull();
    httpMock.expectNone(VERIFY_URL);
  });

  it('confirms with the code from the link, and names the candidate it confirms', () => {
    const fixture = open('code-1');

    button(fixture.nativeElement as HTMLElement)!.click();
    const req = httpMock.expectOne(VERIFY_URL);
    expect(req.request.body).toEqual({ token: 'code-1' });
    req.flush({ id: 'l1', guardianId: 'g1', candidateId: 'c1', candidateEmail: 'candidate@example.com', status: 'Verified' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('You have confirmed that you are the guardian of candidate@example.com');
    expect(button(root)).toBeNull();
  });

  it('shows the reason the API gives when the code cannot be used, and offers the button again', () => {
    const fixture = open('used-code');

    button(fixture.nativeElement as HTMLElement)!.click();
    httpMock
      .expectOne(VERIFY_URL)
      .flush({ title: 'guardian_link_not_pending', detail: 'The link was already confirmed.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('The link was already confirmed.');
    expect(root.textContent).not.toContain('Thank you');
    expect(button(root)!.disabled).toBe(false);
  });

  it('sends one confirmation while one is in flight, so the code is used once', () => {
    const fixture = open('code-2');

    fixture.componentInstance.confirm();
    fixture.componentInstance.confirm();

    httpMock.expectOne(VERIFY_URL).flush({ id: 'l2', guardianId: 'g1', candidateId: 'c2', candidateEmail: 'x@example.com', status: 'Verified' });
  });
});
