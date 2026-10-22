import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { BatchList } from './batch-list';

describe('BatchList', () => {
  let httpMock: HttpTestingController;

  const isRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/batches');
  const isExams = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/exams');
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

  function open(batches: unknown[] | 'error' = [batch('b1', 'Morning batch')], exams: unknown[] | 'error' = [{ id: 'e1', name: 'Maths Final' }]) {
    const fixture = TestBed.createComponent(BatchList);
    fixture.detectChanges();
    // The exam names dress the list; they are asked for with the batches, and the list does not wait for them.
    if (exams === 'error') {
      httpMock.expectOne(isExams).flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      httpMock.expectOne(isExams).flush(exams);
    }
    if (batches === 'error') {
      httpMock.expectOne(isRead).flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      httpMock.expectOne(isRead).flush(batches);
    }
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, icon: () => root.querySelector<HTMLButtonElement>('app-refresh-button button')! };
  }

  it('lists the batches with their exam, status and members', () => {
    const { root } = open();

    const cells = Array.from(root.querySelectorAll('tbody tr:first-child > *')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    expect(cells.slice(0, 4)).toEqual(['Morning batch', 'Maths Final', '✓ Active', '12 / 30']);
  });

  it('shows a dash for the exam when the exams could not be read, and still lists the batches', () => {
    const { root } = open([batch('b1', 'Morning batch')], 'error');

    expect(root.querySelector('tbody tr:first-child td:nth-of-type(1)')?.textContent).toContain('—');
    expect(root.textContent).toContain('Morning batch');
  });

  it('takes a batch to its roster from its name and from the Manage members button, which names the batch', () => {
    const { root } = open();

    const links = Array.from(root.querySelectorAll('tbody a'));
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/batches/b1/roster', '/batches/b1/roster']);
    expect(links[1].getAttribute('aria-label')).toBe('Manage the members of Morning batch');
    expect(links[1].textContent).toContain('Manage members');
    expect(root.querySelector('tbody button')).toBeNull();
  });

  it('names each row with a row header, so a screen reader reads the batch with each of its cells', () => {
    const { root } = open();

    expect(root.querySelector('tbody th[scope="row"]')?.textContent).toContain('Morning batch');
  });

  it('says so when there are no batches', () => {
    const { root } = open([]);

    expect(root.textContent).toContain('No batches yet');
    expect(root.querySelector('app-empty-state a[href="/batches/create"]')).not.toBeNull();
  });

  it('keeps the page title, the create link and the refresh icon together in the header', () => {
    const { root } = open();

    const header = root.querySelector('.page-header')!;
    expect(header.querySelector('h1')?.textContent).toContain('Batches');
    expect(header.querySelector('a.btn--primary')?.textContent).toContain('Create batch');
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
      expect(root.textContent).toContain('The batches could not be loaded');
      expect(icon().hasAttribute('aria-busy')).toBe(false);

      icon().click();
      httpMock.expectOne(isRead).flush([batch('b1', 'Morning batch')]);
      fixture.detectChanges();
      expect(root.textContent).not.toContain('The batches could not be loaded');
    });

    it('is there when the first read failed, says so instead of "No batches found", and recovers the list', () => {
      const { fixture, root, icon } = open('error');
      expect(root.textContent).toContain('The batches could not be loaded');
      expect(root.textContent).not.toContain('No batches yet');

      icon().click();
      httpMock.expectOne(isRead).flush([batch('b1', 'Morning batch')]);
      fixture.detectChanges();

      expect(root.textContent).toContain('Morning batch');
    });
  });

  describe('finding a batch', () => {
    const names = (root: HTMLElement) => Array.from(root.querySelectorAll('tbody th a')).map((a) => a.textContent?.trim());
    const type = (fixture: { detectChanges(): void }, root: HTMLElement, text: string) => {
      const box = root.querySelector<HTMLInputElement>('input[type="search"]')!;
      box.value = text;
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    it('narrows the list by the name of the batch or of its exam', () => {
      const { fixture, root } = open([batch('b1', 'Morning batch'), { ...batch('b2', 'Evening batch'), examId: 'e2' }], [
        { id: 'e1', name: 'Maths Final' },
        { id: 'e2', name: 'Physics Mock' },
      ]);

      type(fixture, root, 'morning');
      expect(names(root)).toEqual(['Morning batch']);

      type(fixture, root, 'physics');
      expect(names(root)).toEqual(['Evening batch']);
    });

    it('narrows the list by status, shows a chip for it, and says so when nothing matches', () => {
      const { fixture, root } = open([batch('b1', 'Morning batch'), { ...batch('b2', 'Old batch'), status: 'Closed' }]);

      const select = root.querySelector<HTMLSelectElement>('#filter-batch-status')!;
      select.value = 'Closed';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(names(root)).toEqual(['Old batch']);
      expect(root.querySelector('.list-chip')?.textContent).toContain('Status: Closed');

      type(fixture, root, 'zzz');
      expect(names(root)).toEqual([]);
      expect(root.textContent).toContain('Nothing matches');
    });

    it('draws twenty batches at a time', () => {
      const { fixture, root } = open(Array.from({ length: 25 }, (_, i) => batch(`b${i}`, `Batch ${i}`)));
      expect(names(root).length).toBe(20);

      root.querySelector<HTMLButtonElement>('app-load-more button')!.click();
      fixture.detectChanges();

      expect(names(root).length).toBe(25);
    });
  });
});
