import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { InviteAccept } from './invite-accept';

describe('InviteAccept', () => {
  let httpMock: HttpTestingController;

  function configure(query: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [InviteAccept],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  const isAccept = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/invites/accept');

  it('accepts with the code from the link and goes to the exams page', () => {
    configure({ code: 'AB12CD34' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    (root.querySelector('button.primary') as HTMLButtonElement).click();

    const post = httpMock.expectOne(isAccept);
    expect(post.request.body).toEqual({ code: 'AB12CD34' });
    post.flush({ id: 'i1', status: 'Accepted' });
    expect(navigate).toHaveBeenCalledWith('/my-exams');
  });

  it('asks for the code when the page is opened without one', () => {
    configure({});
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const submit = root.querySelector('button.primary') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);

    const input = root.querySelector('#invite-code') as HTMLInputElement;
    input.value = ' xy99zz00 ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    expect(httpMock.expectOne(isAccept).request.body).toEqual({ code: 'xy99zz00' });
  });

  it('shows the reason when the invitation was sent to a different address', () => {
    configure({ code: 'AB12CD34' });
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    (root.querySelector('button.primary') as HTMLButtonElement).click();
    httpMock.expectOne(isAccept).flush(
      { title: 'invite_email_mismatch', detail: 'This invitation was sent to a different e-mail address. Sign in with the address it was sent to.' },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    expect(root.textContent).toContain('sent to a different e-mail address');
    expect((root.querySelector('button.primary') as HTMLButtonElement).disabled).toBe(false);
  });
});
