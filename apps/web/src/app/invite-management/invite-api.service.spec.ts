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

  it('creates an invite from an exam and an e-mail, with no inviter id', () => {
    service.createInvite({ examId: 'exam-123', email: 'test@example.com' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ examId: 'exam-123', email: 'test@example.com' });
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
});
