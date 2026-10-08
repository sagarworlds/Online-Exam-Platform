import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WhatsAppTestApiService } from './whatsapp-test-api.service';
import { WhatsAppDeliveryDto, WhatsAppSendResultDto, WhatsAppStatusDto } from './whatsapp-test.models';

describe('WhatsAppTestApiService', () => {
  let service: WhatsAppTestApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(WhatsAppTestApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('reads the setup check from the status route', () => {
    const status: WhatsAppStatusDto = {
      enabled: true,
      canSendMessages: true,
      canSendTemplate: false,
      canTrackDelivery: false,
      signInCodesUseWhatsApp: false,
      inviteCodesUseWhatsApp: false,
      settings: [],
      problems: [],
      notes: [],
    };
    let received: WhatsAppStatusDto | undefined;
    service.status().subscribe((s) => (received = s));

    const request = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/admin/whatsapp/status'));
    request.flush(status);

    expect(received).toEqual(status);
  });

  it('posts the message request as it is, to the messages route', () => {
    const result: WhatsAppSendResultDto = {
      sent: true,
      messageId: 'wamid.1',
      to: '********10',
      mode: 'Text',
      deliveryTracking: true,
      note: null,
      failure: null,
    };
    let received: WhatsAppSendResultDto | undefined;
    service.send({ phoneNumber: '919876543210', mode: 'Text', message: 'Hello' }).subscribe((r) => (received = r));

    const request = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/admin/whatsapp/messages'));
    expect(request.request.body).toEqual({ phoneNumber: '919876543210', mode: 'Text', message: 'Hello' });
    request.flush(result);

    expect(received).toEqual(result);
  });

  it('sends a template request without a message field', () => {
    service.send({ phoneNumber: '919876543210', mode: 'SignInTemplate' }).subscribe();

    const request = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/admin/whatsapp/messages'));
    expect(request.request.body).toEqual({ phoneNumber: '919876543210', mode: 'SignInTemplate' });
    expect('message' in (request.request.body as object)).toBe(false);
    request.flush({});
  });

  it('asks for a delivery report by message id', () => {
    const delivery: WhatsAppDeliveryDto = { messageId: 'wamid.1', status: 'delivered', recipient: '********10', updatedAtUtc: '2026-10-07T10:00:00Z', failure: null };
    let received: WhatsAppDeliveryDto | undefined;
    service.delivery('wamid.1').subscribe((d) => (received = d));

    const request = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/admin/whatsapp/messages/wamid.1'));
    request.flush(delivery);

    expect(received).toEqual(delivery);
  });

  it('escapes a message id that contains characters a URL path cannot carry', () => {
    service.delivery('wamid.A/B+C==').subscribe();

    const request = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/admin/whatsapp/messages/wamid.A%2FB%2BC%3D%3D'));
    request.flush({});
  });
});
