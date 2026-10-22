import { ApplicationRef, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, Routes, provideRouter } from '@angular/router';
import { RouteFocus } from './route-focus';

@Component({ template: '<h1>One</h1>' })
class One {}

@Component({ template: '<h1>Two</h1><p>A page about two.</p>' })
class Two {}

@Component({ template: '<p>No heading here.</p>' })
class Headless {}

@Component({ template: '<h1>Own focus</h1><button id="own">Own</button>' })
class OwnFocus {
  constructor() {
    queueMicrotask(() => document.getElementById('own')?.focus());
  }
}

/** A page with a child route for a panel, as the question bank has: the list stays while the panel opens and closes. */
@Component({ imports: [RouterOutlet], template: '<h1>Bank</h1><router-outlet />' })
class Bank {}

@Component({ template: '<p>Panel</p>' })
class Panel {}

@Component({ template: '' })
class Nothing {}

@Component({
  imports: [RouterOutlet],
  template: '<a id="header-link" href="#">Header</a><main id="main-content" tabindex="-1"><router-outlet /></main>',
})
class Shell {}

const routes: Routes = [
  { path: 'one', component: One },
  { path: 'two', component: Two },
  { path: 'item/:id', component: One },
  { path: 'headless', component: Headless },
  { path: 'own', component: OwnFocus },
  {
    path: 'bank',
    component: Bank,
    children: [
      { path: '', pathMatch: 'full', component: Nothing },
      { path: ':id/edit', component: Panel },
    ],
  },
];

describe('RouteFocus', () => {
  let fixture: ComponentFixture<Shell>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
    TestBed.inject(RouteFocus).start();
    fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
  });

  const go = async (url: string) => {
    await TestBed.inject(Router).navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    TestBed.inject(ApplicationRef).tick();
  };

  it('leaves the focus alone for the first page the app opens', async () => {
    await go('/one');

    expect(document.activeElement).toBe(document.body);
  });

  it('moves the focus to the new page’s heading, which is reachable by script only', async () => {
    await go('/one');

    await go('/two');

    const heading = document.querySelector('h1');
    expect(heading?.textContent).toBe('Two');
    expect(document.activeElement).toBe(heading);
    expect(heading?.getAttribute('tabindex')).toBe('-1');
  });

  it('focuses the main region when the page has no heading', async () => {
    await go('/one');

    await go('/headless');

    expect(document.activeElement).toBe(document.getElementById('main-content'));
  });

  it('does not move the focus for a change of only the query or the fragment', async () => {
    await go('/one');
    const link = document.getElementById('header-link');
    link?.focus();

    await go('/one?page=2');
    await go('/one?page=2#top');

    expect(document.activeElement).toBe(link);
  });

  it('does not move the focus when only a panel inside the page opens or closes', async () => {
    await go('/bank');
    const link = document.getElementById('header-link');
    link?.focus();

    await go('/bank/q1/edit');
    expect(document.activeElement).toBe(link);

    await go('/bank');
    expect(document.activeElement).toBe(link);
  });

  it('moves the focus when the same page opens for another item', async () => {
    await go('/item/1');
    document.getElementById('header-link')?.focus();

    await go('/item/2');

    expect(document.activeElement).toBe(document.querySelector('h1'));
  });

  it('keeps the focus a page placed inside itself', async () => {
    await go('/one');

    await go('/own');

    expect(document.activeElement).toBe(document.getElementById('own'));
  });

  it('leaves the focus to a dialog that is open', async () => {
    await go('/one');
    const dialog = document.createElement('dialog');
    dialog.setAttribute('open', '');
    document.body.appendChild(dialog);

    await go('/two');

    expect(document.activeElement).toBe(document.body);
    dialog.remove();
  });
});
