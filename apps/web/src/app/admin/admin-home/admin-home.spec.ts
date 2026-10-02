import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { AdminHome } from './admin-home';

function render(permissions: string[]): HTMLElement {
  TestBed.configureTestingModule({
    imports: [AdminHome],
    providers: [
      provideRouter([]),
      { provide: AuthSessionService, useValue: { hasPermission: (code: string) => permissions.includes(code) } },
    ],
  });
  const fixture = TestBed.createComponent(AdminHome);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('AdminHome', () => {
  it('offers every admin area to a user holding all the permissions', () => {
    const text = render(['question.manage', 'exam.read', 'invite.manage', 'batch.manage', 'guardian.link.manage']).textContent;

    for (const label of ['Questions', 'Books', 'Exams', 'Invites', 'Batches', 'Guardians']) {
      expect(text).toContain(label);
    }
  });

  it('offers only the areas the permissions open', () => {
    const page = render(['question.manage']);

    expect(page.textContent).toContain('Questions');
    expect(page.textContent).not.toContain('Invites');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/admin/questions');
  });

  it('says so when no area is open to the user', () => {
    expect(render([]).textContent).toContain('no admin areas');
  });
});
