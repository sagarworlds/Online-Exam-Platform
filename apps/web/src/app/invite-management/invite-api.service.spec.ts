import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { InviteApiService } from './invite-api.service';
import { environment } from '../../environments/environment';

describe('InviteApiService', () => {
  let service: InviteApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [InviteApiService],
    });

    service = TestBed.inject(InviteApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create an invite', () => {
    const request = { examId: 'exam-123', batchMemberId: 'member-123', email: 'test@example.com' };
    const userId = 'user-123';

    service.createInvite(request, userId).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.email).toBe('test@example.com');
  });

  it('should revoke an invite', () => {
    const inviteId = 'invite-123';

    service.revokeInvite(inviteId).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/invites/${inviteId}/revoke`);
    expect(req.request.method).toBe('POST');
  });
});
