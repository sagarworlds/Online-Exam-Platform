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
  const codeInput = (root: HTMLElement) => root.querySelector('#invite-code') as HTMLInputElement;

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

  it('says what accepting does before the button, and leaves the button enabled', () => {
    configure({ code: 'AB12CD34' });
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('h1')?.textContent).toContain('Accept your exam invitation');
    expect(root.querySelector('.card p')?.textContent).toContain('This invitation adds an exam to your exams.');
    expect((root.querySelector('button.primary') as HTMLButtonElement).disabled).toBe(false);
  });

  it('asks for the code when the page is opened without one, and trims what is typed', () => {
    configure({});
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const submit = root.querySelector('button.primary') as HTMLButtonElement;
    expect(submit.disabled).toBe(false);

    codeInput(root).value = ' xy99zz00 ';
    codeInput(root).dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    expect(httpMock.expectOne(isAccept).request.body).toEqual({ code: 'xy99zz00' });
  });

  it('names a blank code on the field, and sends nothing', () => {
    configure({});
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    codeInput(root).value = '   ';
    codeInput(root).dispatchEvent(new Event('input'));
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(root.querySelector('#invite-code-error')?.textContent).toContain('Enter the invitation code.');
    expect(codeInput(root).getAttribute('aria-invalid')).toBe('true');
    expect(codeInput(root).getAttribute('aria-describedby')).toContain('invite-code-error');
    httpMock.expectNone(isAccept);
  });

  it('shows the reason when the invitation was sent to a different address, as an alert above the button', () => {
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

    const alert = root.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('sent to a different e-mail address');
    expect(alert?.nextElementSibling?.tagName).toBe('BUTTON');
    expect((root.querySelector('button.primary') as HTMLButtonElement).disabled).toBe(false);
  });

  it('keeps a refused code in the field, so the candidate can correct it', () => {
    configure({});
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    codeInput(root).value = 'AB12CD34';
    codeInput(root).dispatchEvent(new Event('input'));
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    httpMock.expectOne(isAccept).flush({ title: 'invite_not_found', detail: 'No invitation matches that code.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No invitation matches that code.');
    expect(codeInput(root).value).toBe('AB12CD34');
  });

  it('shows Accepting… and holds the button while the request is in flight', () => {
    configure({ code: 'AB12CD34' });
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(InviteAccept);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const button = root.querySelector('button.primary') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    expect(button.textContent?.trim()).toBe('Accepting…');
    expect(button.disabled).toBe(true);
    expect(root.querySelector('section.card')?.getAttribute('aria-busy')).toBe('true');
    httpMock.expectOne(isAccept).flush({ id: 'i1', status: 'Accepted' });
  });
});
