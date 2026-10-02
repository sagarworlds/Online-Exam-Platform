import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InviteList } from './invite-list';

describe('InviteList', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InviteList],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/invites');

  it('lists invitations and offers Revoke only for one that is still pending', () => {
    const fixture = TestBed.createComponent(InviteList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush([
      { id: 'i1', email: 'a@example.com', status: 'Pending', sentAt: '2026-10-02T00:00:00Z' },
      { id: 'i2', email: 'b@example.com', status: 'Accepted', sentAt: '2026-10-02T00:00:00Z' },
    ]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.textContent).toContain('a@example.com');
    expect(root.textContent).toContain('b@example.com');
    expect(Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Revoke')).toHaveLength(1);
  });

  it('revokes an invitation and reloads the list', () => {
    const fixture = TestBed.createComponent(InviteList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush([{ id: 'i1', email: 'a@example.com', status: 'Pending', sentAt: '2026-10-02T00:00:00Z' }]);
    fixture.detectChanges();

    ((fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites/i1/revoke')).flush(null, { status: 204, statusText: 'No Content' });
    httpMock.expectOne(isList).flush([{ id: 'i1', email: 'a@example.com', status: 'Revoked', sentAt: '2026-10-02T00:00:00Z' }]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Revoked');
  });
});
