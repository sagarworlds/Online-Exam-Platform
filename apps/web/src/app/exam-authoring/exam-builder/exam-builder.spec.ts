import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ExamBuilder } from './exam-builder';

describe('ExamBuilder', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamBuilder],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function submitWith(seriesId: string) {
    const fixture = TestBed.createComponent(ExamBuilder);
    fixture.detectChanges();
    fixture.componentInstance.form.patchValue({ name: 'Maths Final', seriesId });
    fixture.componentInstance.onSubmit();
    return httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`);
  }

  it('sends null, not an empty string, when the series field is blank', () => {
    const req = submitWith('');

    expect(req.request.body.seriesId).toBeNull();
  });

  it('sends null when the series field holds only whitespace', () => {
    const req = submitWith('   ');

    expect(req.request.body.seriesId).toBeNull();
  });

  it('sends the trimmed series id when one is given', () => {
    const req = submitWith(' 7c9e6679-7425-40de-944b-e07fc1f90ae7 ');

    expect(req.request.body.seriesId).toBe('7c9e6679-7425-40de-944b-e07fc1f90ae7');
  });

  it('does not send the creator, which the API takes from the access token', () => {
    const req = submitWith('');

    expect('createdBy' in req.request.body).toBe(false);
  });

  it('refuses a series id that is not a GUID, says so, and sends nothing', () => {
    const fixture = TestBed.createComponent(ExamBuilder);
    fixture.detectChanges();
    fixture.componentInstance.form.patchValue({ name: 'Maths Final', seriesId: 'series-1' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const submit = root.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    expect(root.textContent).toContain('A series ID looks like');

    fixture.componentInstance.onSubmit();
    httpMock.expectNone(`${environment.apiBaseUrl}/v1/exams`);
  });

  it('shows the reason the API gives when creating the exam fails', () => {
    const fixture = TestBed.createComponent(ExamBuilder);
    fixture.detectChanges();
    fixture.componentInstance.form.patchValue({ name: 'Maths Final' });
    fixture.componentInstance.onSubmit();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/v1/exams`)
      .flush({ title: 'invalid_request', detail: 'The request could not be read.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('The request could not be read.');
  });
});
