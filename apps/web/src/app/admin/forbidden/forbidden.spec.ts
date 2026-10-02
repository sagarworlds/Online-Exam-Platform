import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Forbidden } from './forbidden';

describe('Forbidden', () => {
  it('tells the user they have no access and links home', () => {
    TestBed.configureTestingModule({ imports: [Forbidden], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(Forbidden);
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('does not have access');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/');
  });
});
