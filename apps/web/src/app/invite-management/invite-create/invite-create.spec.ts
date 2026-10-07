import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InviteCreate } from './invite-create';

describe('InviteCreate', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InviteCreate],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const exams = [
    { id: 'e1', name: 'Published Exam', status: 'Published' },
    { id: 'e2', name: 'Draft Exam', status: 'Draft' },
  ];

  function open() {
    const fixture = TestBed.createComponent(InviteCreate);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams')).flush(exams);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function fill(fixture: ReturnType<typeof open>['fixture'], root: HTMLElement) {
    const select = root.querySelector('#invite-exam') as HTMLSelectElement;
    select.value = 'e1';
    select.dispatchEvent(new Event('change'));
    const email = root.querySelector('#invite-email') as HTMLInputElement;
    email.value = 'student@example.com';
    email.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('offers only published exams', () => {
    const { root } = open();

    const options = Array.from(root.querySelectorAll('#invite-exam option')).map((o) => o.textContent?.trim());
    expect(options).toContain('Published Exam');
    expect(options).not.toContain('Draft Exam');
  });

  it('sends the exam and e-mail, and says so when the mail went out', () => {
    const { fixture, root } = open();
    fill(fixture, root);

    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/invites'));
    expect(post.request.body).toEqual({ examId: 'e1', email: 'student@example.com' });
    post.flush({ id: 'i1', email: 'student@example.com', emailSent: true, inviteLink: null }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Invitation e-mailed to student@example.com');
    expect(root.textContent).not.toContain('Send this link');
  });

  it('says so when the exam code also went to their phone on WhatsApp', () => {
    const { fixture, root } = open();
    fill(fixture, root);
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush(
        { id: 'i1', email: 'student@example.com', emailSent: true, whatsAppSent: true, inviteLink: null },
        { status: 201, statusText: 'Created' },
      );
    fixture.detectChanges();

    expect(root.textContent).toContain('Invitation e-mailed to student@example.com');
    expect(root.textContent).toContain('also sent to their phone on WhatsApp');
  });

  it('does not ask the inviter to pass a link on when only WhatsApp delivered', () => {
    const { fixture, root } = open();
    fill(fixture, root);
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush(
        { id: 'i1', email: 'student@example.com', emailSent: false, whatsAppSent: true, inviteLink: null },
        { status: 201, statusText: 'Created' },
      );
    fixture.detectChanges();

    expect(root.textContent).toContain('sent to their phone on WhatsApp');
    expect(root.textContent).not.toContain('Send this link');
  });

  it('does not mention WhatsApp when it was not used', () => {
    const { fixture, root } = open();
    fill(fixture, root);
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush(
        { id: 'i1', email: 'student@example.com', emailSent: true, whatsAppSent: false, inviteLink: null },
        { status: 201, statusText: 'Created' },
      );
    fixture.detectChanges();

    expect(root.textContent).not.toContain('WhatsApp');
  });

  it('shows the link to pass on when no e-mail could be sent', () => {
    const { fixture, root } = open();
    fill(fixture, root);
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush(
        { id: 'i1', email: 'student@example.com', emailSent: false, inviteLink: 'http://localhost:4200/invite?code=AB12CD34' },
        { status: 201, statusText: 'Created' },
      );
    fixture.detectChanges();

    expect(root.textContent).toContain('no e-mail could be sent');
    expect(root.textContent).toContain('http://localhost:4200/invite?code=AB12CD34');
  });

  it('shows the API error when the invitation is refused', () => {
    const { fixture, root } = open();
    fill(fixture, root);
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush({ title: 'exam_not_found', detail: 'Exam does not exist.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Exam does not exist.');
  });
});
