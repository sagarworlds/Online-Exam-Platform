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

  it('flags a whitespace-only display name inline and keeps Save disabled', () => {
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

    const displayName = fixture.componentInstance['form'].controls.displayName;
    displayName.setValue('   ');
    displayName.markAsDirty();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const input = compiled.querySelector('#profile-display-name') as HTMLInputElement;
    expect(input.getAttribute('aria-describedby')).toBe('profile-display-name-error');
    expect(compiled.querySelector('#profile-display-name-error')?.textContent).toContain('Enter a display name.');
    expect((compiled.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
  });
});
