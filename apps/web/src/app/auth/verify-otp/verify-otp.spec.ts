import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { VerifyOtp } from './verify-otp';

describe('VerifyOtp', () => {
  async function createWithQueryParams(params: Record<string, string>) {
    await TestBed.configureTestingModule({
      imports: [VerifyOtp],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(params) } } },
      ],
    }).compileComponents();

    return TestBed.createComponent(VerifyOtp);
  }

  it('renders the code form when a challenge id is present', async () => {
    const fixture = await createWithQueryParams({ challengeId: 'challenge-1', destination: 'a@b.com' });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('a@b.com');
    expect(compiled.querySelector('#verify-otp-code')).toBeTruthy();
  });

  it('shows an error and no form when the challenge id is missing', async () => {
    const fixture = await createWithQueryParams({});
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Missing verification challenge');
    expect(compiled.querySelector('#verify-otp-code')).toBeNull();
  });
});
