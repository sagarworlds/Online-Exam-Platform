import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
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

@Component({
  imports: [RouterOutlet],
  template: '<a id="header-link" href="#">Header</a><main id="main-content" tabindex="-1"><router-outlet /></main>',
})
class Shell {}

describe('RouteFocus', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: Shell,
            children: [
              { path: 'one', component: One },
              { path: 'two', component: Two },
              { path: 'headless', component: Headless },
              { path: 'own', component: OwnFocus },
            ],
          },
        ]),
      ],
    });
    TestBed.inject(RouteFocus).start();
    harness = await RouterTestingHarness.create();
  });

  const go = async (url: string) => {
    await harness.navigateByUrl(url);
    harness.detectChanges();
    await harness.fixture.whenStable();
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
