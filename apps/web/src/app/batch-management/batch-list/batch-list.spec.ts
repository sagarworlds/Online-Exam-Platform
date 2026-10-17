import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BatchDto } from '../batch.models';
import { BatchList } from './batch-list';

/**
 * The Batches page leaves "Loading batches..." when the answer comes.
 *
 * The app runs without zone.js, so a page is redrawn only for what a signal tells it about. These tests therefore wait for the page to
 * settle by itself after the answer (`whenStable`) and never call `detectChanges` for it: a manual redraw would show the new state even
 * for a page that a browser leaves on the old one, which is how this page once stuck on "Loading batches..." unnoticed.
 */
describe('BatchList', () => {
  let fixture: ComponentFixture<BatchList>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;

  const isRead = (request: { method: string; url: string }) => request.method === 'GET' && request.url.endsWith('/v1/batches');

  const batch = (overrides: Partial<BatchDto> & { id: string; name: string }): BatchDto => ({
    examId: 'exam-1',
    status: 'Active',
    maxMembers: 50,
    activeMemberCount: 3,
    createdBy: 'staff-1',
    createdAt: new Date('2026-10-01T09:00:00Z'),
    updatedAt: new Date('2026-10-01T09:00:00Z'),
    ...overrides,
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchList],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(BatchList);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  });

  afterEach(() => httpMock.verify());

  it('says it is loading while the batches are being read', () => {
    expect(root.textContent).toContain('Loading batches...');
    httpMock.expectOne(isRead).flush([]);
  });

  it('shows the batches in a table once they arrive, and no longer says it is loading', async () => {
    httpMock.expectOne(isRead).flush([batch({ id: 'b1', name: 'Morning batch' }), batch({ id: 'b2', name: 'Evening batch', activeMemberCount: 12 })]);
    await fixture.whenStable();

    expect(root.textContent).not.toContain('Loading batches...');
    expect(Array.from(root.querySelectorAll('tbody tr')).map((row) => row.querySelector('strong')?.textContent)).toEqual([
      'Morning batch',
      'Evening batch',
    ]);
    expect(root.querySelectorAll('tbody tr')[1].textContent).toContain('12 / 50');
  });

  it('says there are no batches, with a way to create one, when the answer is empty', async () => {
    httpMock.expectOne(isRead).flush([]);
    await fixture.whenStable();

    expect(root.textContent).not.toContain('Loading batches...');
    expect(root.textContent).toContain('No batches found.');
    expect(root.querySelector('.empty-state a')?.getAttribute('href')).toBe('/batches/create');
  });

  it('shows why when the batches cannot be read, and no longer says it is loading', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    httpMock.expectOne(isRead).flush({ title: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();

    expect(root.textContent).not.toContain('Loading batches...');
    expect(root.querySelector('.error-message')?.textContent).toContain('Failed to load batches');
    expect(consoleError).toHaveBeenCalled();
  });
});
