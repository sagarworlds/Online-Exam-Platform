import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { InvitedAddress } from './invited-address';

describe('InvitedAddress', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InvitedAddress],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const isChange = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/invites/change-email');

  function open() {
    const fixture = TestBed.createComponent(InvitedAddress);
    fixture.componentRef.setInput('email', 'old@example.com');
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, changed };
  }

  const input = (root: HTMLElement) => root.querySelector<HTMLInputElement>('input[type="email"]')!;
  const submit = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;

  function type(fixture: ReturnType<typeof open>['fixture'], value: string) {
    const field = input(fixture.nativeElement);
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('moves the invitations to the new address and says how many moved', () => {
    const { fixture, root, changed } = open();
    type(fixture, ' new@example.com ');

    submit(root).click();
    const post = httpMock.expectOne(isChange);
    expect(post.request.body).toEqual({ currentEmail: 'old@example.com', newEmail: 'new@example.com' });
    post.flush({ changed: 3 });

    expect(changed).toHaveBeenCalledWith({ from: 'old@example.com', to: 'new@example.com', invitationsMoved: 3 });
  });

  it('will not send an address that is not one', () => {
    const { fixture, root, changed } = open();
    type(fixture, 'not-an-address');

    submit(root).click();
    fixture.detectChanges();

    httpMock.expectNone(isChange);
    expect(root.querySelector('.field-error')?.textContent).toContain('valid e-mail address');
    expect(changed).not.toHaveBeenCalled();
  });

  it("shows the API's reason when it refuses and does not say the invitations moved", () => {
    const { fixture, root, changed } = open();
    type(fixture, 'old@example.com');

    submit(root).click();
    httpMock
      .expectOne(isChange)
      .flush({ title: 'invite_email_unchanged', detail: 'That is the address the invitations already have.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('That is the address the invitations already have.');
    expect(changed).not.toHaveBeenCalled();
  });

  it('does not send twice while one change is on its way', () => {
    const { fixture, root } = open();
    type(fixture, 'new@example.com');

    submit(root).click();
    const request = httpMock.expectOne(isChange);
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock.expectNone(isChange);
    request.flush({ changed: 1 });
  });
});
