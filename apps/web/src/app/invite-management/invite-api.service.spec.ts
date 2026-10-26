import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { InviteApiService } from './invite-api.service';

describe('InviteApiService', () => {
  let service: InviteApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(InviteApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('creates an invite from an exam and an email, with no inviter id', () => {
    service.createInvite({ examId: 'exam-123', email: 'test@example.com' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ examId: 'exam-123', email: 'test@example.com' });
  });

  it('asks for a direct sign-in code only when told to, so an ordinary request is unchanged', () => {
    service.generateCode('invite-123', 24).subscribe();
    service.generateCode('invite-123', 24, true).subscribe();

    const [ordinary, direct] = httpMock.match(`${environment.apiBaseUrl}/v1/invites/invite-123/codes`);
    expect(ordinary.request.body).toEqual({ expiryHours: 24 });
    expect(direct.request.body).toEqual({ expiryHours: 24, directSignIn: true });
  });

  it('opens a direct sign-in link on the anonymous route, with only the code', () => {
    service.directSignIn('ABCD1234EFGH5678').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites/direct-sign-in`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'ABCD1234EFGH5678' });
  });

  it('lists the invites', () => {
    service.getInvites().subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites`);
    expect(req.request.method).toBe('GET');
  });

  it('accepts by code on the owner-bound route, not by invite id', () => {
    service.acceptInvite('AB12CD34').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites/accept`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'AB12CD34' });
  });

  it('revokes an invite', () => {
    service.revokeInvite('invite-123').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites/invite-123/revoke`);
    expect(req.request.method).toBe('POST');
  });

  it('moves the invitations of one address to another', () => {
    service.changeEmail('old@example.com', 'new@example.com').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites/change-email`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ currentEmail: 'old@example.com', newEmail: 'new@example.com' });
  });
});
