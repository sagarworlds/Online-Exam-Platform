import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { StudentApiService } from './student-api.service';

describe('StudentApiService', () => {
  let service: StudentApiService;
  let httpMock: HttpTestingController;
  const url = `${environment.apiBaseUrl}/v1/admin/students`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(StudentApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists students with no search parameter when nothing was typed', () => {
    service.getStudents('   ').subscribe();

    const req = httpMock.expectOne((r) => r.url === url);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('search')).toBe(false);
  });

  it('sends what was typed, trimmed, as the search', () => {
    service.getStudents('  asha ').subscribe();

    expect(httpMock.expectOne((r) => r.url === url).request.params.get('search')).toBe('asha');
  });

  it('finds students by address with a POST, since the addresses would not fit in a URL', () => {
    service.findByEmail(['a@example.com', 'b@example.com']).subscribe();

    const req = httpMock.expectOne(`${url}/lookup`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ emails: ['a@example.com', 'b@example.com'] });
  });

  it('opens one student', () => {
    service.getStudent('s1').subscribe();

    const req = httpMock.expectOne(`${url}/s1`);
    expect(req.request.method).toBe('GET');
  });

  it('puts the whole profile', () => {
    const profile = { displayName: 'Asha', email: 'asha@example.com', phoneNumber: null, dateOfBirth: '2000-01-01' };
    service.updateStudent('s1', profile).subscribe();

    const req = httpMock.expectOne(`${url}/s1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(profile);
  });
});
