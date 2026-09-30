import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Profile } from './profile';

describe('Profile', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Profile],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads and displays the profile', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    httpMock.expectOne(() => true).flush({
      userId: 'u1',
      email: 'a@b.com',
      phoneNumber: null,
      displayName: 'Ada',
      status: 'Active',
      roles: ['Candidate'],
    });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Active');
    expect(compiled.textContent).toContain('Candidate');
  });
});
