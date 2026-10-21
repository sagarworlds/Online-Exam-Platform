import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { BatchList } from './batch-list';

describe('BatchList', () => {
  let httpMock: HttpTestingController;

  const isRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/batches');
  const batch = (id: string, name: string) => ({
    id,
    examId: 'e1',
    name,
    status: 'Active',
    maxMembers: 30,
    activeMemberCount: 12,
    createdBy: 'u1',
    createdAt: '2026-10-01T05:00:00Z',
    updatedAt: '2026-10-01T05:00:00Z',
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchList],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    // A failed read is logged by the page; the tests that make one fail on purpose do not need the noise.
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  function open(batches: unknown[] | 'error' = [batch('b1', 'Morning batch')]) {
    const fixture = TestBed.createComponent(BatchList);
    fixture.detectChanges();
    if (batches === 'error') {
      httpMock.expectOne(isRead).flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      httpMock.expectOne(isRead).flush(batches);
    }
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, icon: () => root.querySelector<HTMLButtonElement>('app-refresh-button button')! };
  }

  it('lists the batches with their members and a way to manage each', () => {
    const { root } = open();

    expect(root.textContent).toContain('Morning batch');
    expect(root.textContent).toContain('12 / 30');
    expect(root.textContent).toContain('Manage');
  });

  it('says so when there are no batches', () => {
    const { root } = open([]);

    expect(root.textContent).toContain('No batches found.');
  });

  it('keeps the page title, the create link and the refresh icon together in the header', () => {
    const { root } = open();

    const header = root.querySelector('.page-header')!;
    expect(header.querySelector('h1')?.textContent).toContain('Batches');
    expect(header.querySelector('a.btn--primary')?.textContent).toContain('Create New Batch');
    expect(header.querySelector('app-refresh-button')).not.toBeNull();
  });

  describe('the refresh icon', () => {
    it('reads the batches again without blanking the list, and shows the batches added since', () => {
      const { fixture, root, icon } = open();

      icon().click();
      fixture.detectChanges();
      expect(icon().getAttribute('aria-busy')).toBe('true');
      expect(root.textContent).toContain('Morning batch');
      expect(root.textContent).not.toContain('Loading batches');

      httpMock.expectOne(isRead).flush([batch('b1', 'Morning batch'), batch('b2', 'Evening batch')]);
      fixture.detectChanges();

      expect(root.textContent).toContain('Evening batch');
      expect(icon().hasAttribute('aria-busy')).toBe(false);
    });

    it('keeps the list and says why when the read fails, and clears that once it works', () => {
      const { fixture, root, icon } = open();

      icon().click();
      httpMock.expectOne(isRead).flush({}, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(root.textContent).toContain('Morning batch');
      expect(root.textContent).toContain('Failed to load batches');
      expect(icon().hasAttribute('aria-busy')).toBe(false);

      icon().click();
      httpMock.expectOne(isRead).flush([batch('b1', 'Morning batch')]);
      fixture.detectChanges();
      expect(root.textContent).not.toContain('Failed to load batches');
    });

    it('is there when the first read failed, says so instead of "No batches found", and recovers the list', () => {
      const { fixture, root, icon } = open('error');
      expect(root.textContent).toContain('Failed to load batches');
      expect(root.textContent).not.toContain('No batches found');

      icon().click();
      httpMock.expectOne(isRead).flush([batch('b1', 'Morning batch')]);
      fixture.detectChanges();

      expect(root.textContent).toContain('Morning batch');
    });
  });
});
