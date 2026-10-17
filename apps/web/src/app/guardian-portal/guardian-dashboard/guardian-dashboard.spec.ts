import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { GuardianLinkDto } from '../guardian.models';
import { GuardianDashboard } from './guardian-dashboard';

/**
 * The guardian's page leaves "Loading candidates..." when the answer comes.
 *
 * The app runs without zone.js, so a page is redrawn only for what a signal tells it about. These tests wait for the page to settle by
 * itself after the answer (`whenStable`) and never call `detectChanges` for it, as a manual redraw would hide a page that a browser leaves
 * on the old state.
 */
describe('GuardianDashboard', () => {
  let fixture: ComponentFixture<GuardianDashboard>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;

  const isRead = (request: { method: string; url: string }) => request.method === 'GET' && request.url.endsWith('/v1/guardians/guardian-1/links');

  const link = (overrides: Partial<GuardianLinkDto> & { id: string; candidateEmail: string }): GuardianLinkDto => ({
    guardianId: 'guardian-1',
    candidateId: `candidate-of-${overrides.id}`,
    status: 'Verified',
    ...overrides,
  });

  async function open(session: { userId: string } | null = { userId: 'guardian-1' }): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [GuardianDashboard],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthSessionService, useValue: { session: () => session } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(GuardianDashboard);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  afterEach(() => httpMock.verify());

  it('says it is loading while the links are being read', async () => {
    await open();

    expect(root.textContent).toContain('Loading candidates...');
    httpMock.expectOne(isRead).flush([]);
  });

  it('lists the linked candidates and counts the verified ones once they arrive', async () => {
    await open();

    httpMock.expectOne(isRead).flush([
      link({ id: 'l1', candidateEmail: 'asha@example.com' }),
      link({ id: 'l2', candidateEmail: 'ben@example.com', status: 'Pending' }),
      link({ id: 'l3', candidateEmail: 'chitra@example.com', status: 'Revoked' }),
    ]);
    await fixture.whenStable();

    expect(root.textContent).not.toContain('Loading candidates...');
    expect(Array.from(root.querySelectorAll('tbody tr')).map((row) => row.querySelector('td')?.textContent)).toEqual([
      'asha@example.com',
      'ben@example.com',
      'chitra@example.com',
    ]);
    const counts = Array.from(root.querySelectorAll('.stat-value')).map((count) => count.textContent?.trim());
    expect(counts).toEqual(['3', '1']);
  });

  it('says no candidates are linked yet, with a way to link one, when the answer is empty', async () => {
    await open();

    httpMock.expectOne(isRead).flush([]);
    await fixture.whenStable();

    expect(root.textContent).not.toContain('Loading candidates...');
    expect(root.textContent).toContain('No candidates linked yet.');
  });

  it('shows why when the links cannot be read, and no longer says it is loading', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await open();

    httpMock.expectOne(isRead).flush({ title: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();

    expect(root.textContent).not.toContain('Loading candidates...');
    expect(root.querySelector('.error-message')?.textContent).toContain('Failed to load linked candidates');
    expect(consoleError).toHaveBeenCalled();
  });

  it('reads nothing and says so when nobody is signed in', async () => {
    await open(null);

    httpMock.expectNone(isRead);
    expect(root.querySelector('.error-message')?.textContent).toContain('Not authenticated');
  });
});
