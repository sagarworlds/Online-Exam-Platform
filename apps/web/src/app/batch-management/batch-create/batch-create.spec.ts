import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BatchCreate } from './batch-create';

/**
 * Creating a batch shows what the API answered.
 *
 * The app runs without zone.js, so a page is redrawn only for what a signal tells it about. These tests wait for the page to settle by
 * itself after the answer (`whenStable`) and never call `detectChanges` for it, as a manual redraw would hide a page that a browser leaves
 * on the old state: a button stuck on "Creating..." with no reason shown.
 */
describe('BatchCreate', () => {
  let fixture: ComponentFixture<BatchCreate>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  const isCreate = (request: { method: string; url: string }) => request.method === 'POST' && request.url.endsWith('/v1/batches');
  const submitButton = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchCreate],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(BatchCreate);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  });

  afterEach(() => httpMock.verify());

  async function submit(): Promise<void> {
    fixture.componentInstance.form.patchValue({ examId: 'exam-1', name: 'Morning batch' });
    fixture.componentInstance.onSubmit();
    await fixture.whenStable();
  }

  it('says it is creating while the request is out, and locks the button', async () => {
    await submit();

    expect(submitButton().textContent).toContain('Creating...');
    expect(submitButton().disabled).toBe(true);
    httpMock.expectOne(isCreate).flush({});
  });

  it('shows why when the API refuses, and unlocks the button so the form can be fixed and sent again', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await submit();

    httpMock.expectOne(isCreate).flush({ title: 'invalid_request' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(root.querySelector('.error-message')?.textContent).toContain('Failed to create batch');
    expect(submitButton().textContent).toContain('Create Batch');
    expect(submitButton().disabled).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
    expect(consoleError).toHaveBeenCalled();
  });

  it('goes back to the batches once the batch is created', async () => {
    await submit();

    httpMock.expectOne(isCreate).flush({});
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/batches']);
  });
});
