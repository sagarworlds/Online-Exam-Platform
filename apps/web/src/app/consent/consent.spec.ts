import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthSessionService } from '../auth/auth-session.service';
import { Consent } from './consent';

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

  it('loads status for all three consent purposes and shows a Grant action', () => {
    const fixture = TestBed.createComponent(Consent);
    fixture.detectChanges();

    const requests = httpMock.match(() => true);
    expect(requests).toHaveLength(3);
    const purposes = ['TermsOfService', 'PrivacyNotice', 'ProctoringDataProcessing'];
    requests.forEach((req, index) =>
      req.flush({
        subjectId: 'u1',
        purpose: purposes[index],
        isActive: false,
        consentRecordId: null,
        currentNoticeVersionId: 'notice-1',
      }),
    );
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Terms of Service');
    expect(compiled.textContent).toContain('Grant');
  });
});
