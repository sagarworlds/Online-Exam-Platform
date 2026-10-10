import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { GuardianApiService } from './guardian-api.service';
import { environment } from '../../environments/environment';

describe('GuardianApiService', () => {
  let service: GuardianApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [GuardianApiService],
    });

    service = TestBed.inject(GuardianApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should register a guardian', () => {
    const request = { email: 'guardian@example.com', fullName: 'John Guardian', phone: '+1234567890' };

    service.registerGuardian(request).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/guardians`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.email).toBe('guardian@example.com');
  });

  it('should link a candidate', () => {
    const guardianId = 'guardian-123';
    const request = { candidateId: 'candidate-123', candidateEmail: 'candidate@example.com' };

    service.linkCandidate(guardianId, request).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/guardians/${guardianId}/links`);
    expect(req.request.method).toBe('POST');
  });

  it('should confirm a link with the code from the e-mail, without a sign-in', () => {
    service.verifyLink('code-from-email').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/guardian-links/verify`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ token: 'code-from-email' });
  });

  it('should revoke a link', () => {
    const guardianId = 'guardian-123';
    const candidateId = 'candidate-123';

    service.revokeLink(guardianId, candidateId).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/guardians/${guardianId}/links/${candidateId}`);
    expect(req.request.method).toBe('DELETE');
  });
});
