import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ContentEncryptionStatusPanel } from './content-encryption-status';

describe('ContentEncryptionStatusPanel (#57)', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<ContentEncryptionStatusPanel>;
  let root: HTMLElement;

  const isStatus = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/encryption-status');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContentEncryptionStatusPanel],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function open(response: { contentValues: number; plaintextValues: number; complete: boolean } | 'error') {
    fixture = TestBed.createComponent(ContentEncryptionStatusPanel);
    fixture.detectChanges();
    const request = httpMock.expectOne(isStatus);
    if (response === 'error') {
      request.flush({ title: 'server_error', detail: 'The status is unavailable.' }, { status: 500, statusText: 'Server Error' });
    } else {
      request.flush(response);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  it('says everything is encrypted, with the count, once the backfill has finished', () => {
    open({ contentValues: 42, plaintextValues: 0, complete: true });

    expect(root.querySelector('.badge--active')?.textContent).toContain('Encrypted');
    expect(root.textContent).toContain('All 42 stored question values are encrypted at rest.');
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('says how many values are still plaintext, and that the backfill has to finish, when it is not complete', () => {
    open({ contentValues: 42, plaintextValues: 5, complete: false });

    expect(root.querySelector('.badge--danger')?.textContent).toContain('Not complete');
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('5 of 42 stored question values are still plain text.');
  });

  it('says there is no content yet when nothing is stored', () => {
    open({ contentValues: 0, plaintextValues: 0, complete: true });

    expect(root.textContent).toContain('No question content is stored yet.');
  });

  it('shows the failure, and a retry that reads the status again', () => {
    open('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('The status is unavailable.');

    const retry = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Try again'));
    retry?.click();
    httpMock.expectOne(isStatus).flush({ contentValues: 3, plaintextValues: 0, complete: true });
    fixture.detectChanges();

    expect(root.textContent).toContain('All 3 stored question values are encrypted at rest.');
  });
});
