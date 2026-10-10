import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot } from '@angular/router';
import { vi } from 'vitest';
import { I18nService } from '../../i18n/i18n.service';
import { APP_TITLE, PAGE_TITLE_KEYS, PageTitleStrategy, deepestRoutePath } from './page-title.strategy';

/** A router snapshot whose deepest route has the given path, as the router builds it for a lazy-loaded page (one level below the root). */
function snapshotOn(path: string | undefined): RouterStateSnapshot {
  return {
    root: { routeConfig: null, firstChild: { routeConfig: { path }, firstChild: null } },
  } as unknown as RouterStateSnapshot;
}

describe('PageTitleStrategy', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.spyOn(window.navigator, 'languages', 'get').mockReturnValue(['en']);
    TestBed.configureTestingModule({ providers: [PageTitleStrategy] });
  });
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  const strategy = () => TestBed.inject(PageTitleStrategy);
  const setTitle = () => vi.spyOn(TestBed.inject(Title), 'setTitle');

  it('names the deepest route the URL is on, as app.routes.ts writes its path', () => {
    expect(deepestRoutePath(snapshotOn('attempt/:attemptId/review'))).toBe('attempt/:attemptId/review');
  });

  it('puts the page name before the app name in the tab', () => {
    const title = setTitle();

    strategy().updateTitle(snapshotOn('login'));
    TestBed.flushEffects();

    expect(title).toHaveBeenLastCalledWith(`Log in · ${APP_TITLE}`);
  });

  it('keeps the app name alone for a page with no name of its own', () => {
    const title = setTitle();

    strategy().updateTitle(snapshotOn('guardian'));
    TestBed.flushEffects();

    expect(PAGE_TITLE_KEYS['guardian']).toBeUndefined();
    expect(title).toHaveBeenLastCalledWith(APP_TITLE);
  });

  it('names the page in the new language when the language changes while it is open', async () => {
    const title = setTitle();
    strategy().updateTitle(snapshotOn('login'));
    TestBed.flushEffects();

    const i18n = TestBed.inject(I18nService);
    await i18n.setLanguage('hi');
    TestBed.flushEffects();

    expect(title).toHaveBeenLastCalledWith(`${i18n.t('login.title')} · ${APP_TITLE}`);
    expect(i18n.t('login.title')).not.toBe('Log in');
  });
});
