import { TestBed } from '@angular/core/testing';
import { THEME_STORAGE_KEY, ThemeService } from './theme.service';

describe('ThemeService', () => {
  const metas: HTMLMetaElement[] = [];

  /** The two theme-color tags index.html has: the brand blue for a light system, the dark page's colour for a dark one. */
  function addToolbarTags(): { light: HTMLMetaElement; dark: HTMLMetaElement } {
    const make = (media: string, content: string) => {
      const tag = document.createElement('meta');
      tag.name = 'theme-color';
      tag.media = media;
      tag.content = content;
      document.head.appendChild(tag);
      metas.push(tag);
      return tag;
    };
    return { light: make('(prefers-color-scheme: light)', '#1a56db'), dark: make('(prefers-color-scheme: dark)', '#0e1320') };
  }

  const root = () => document.documentElement;

  afterEach(() => {
    localStorage.clear();
    root().removeAttribute('data-theme');
    metas.splice(0).forEach((tag) => tag.remove());
  });

  it('follows the system when nothing was chosen: no attribute on the page', () => {
    expect(TestBed.inject(ThemeService).choice()).toBe('system');
    expect(root().hasAttribute('data-theme')).toBe(false);
  });

  it('puts a chosen theme on the page and keeps it, so the next visit starts with it', () => {
    const service = TestBed.inject(ThemeService);

    service.set('dark');

    expect(service.choice()).toBe('dark');
    expect(root().getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('starts with the theme kept from before', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'light');

    const service = TestBed.inject(ThemeService);

    expect(service.choice()).toBe('light');
    expect(root().getAttribute('data-theme')).toBe('light');
  });

  it('ignores a kept value it does not know', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'purple');

    expect(TestBed.inject(ThemeService).choice()).toBe('system');
    expect(root().hasAttribute('data-theme')).toBe(false);
  });

  it('goes back to the system, and forgets the choice', () => {
    const service = TestBed.inject(ThemeService);
    service.set('dark');

    service.set('system');

    expect(root().hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
  });

  it('sets both toolbar colours to the chosen theme’s, and puts each back for the system', () => {
    const tags = addToolbarTags();
    const service = TestBed.inject(ThemeService);

    service.set('dark');
    expect([tags.light.content, tags.dark.content]).toEqual(['#0e1320', '#0e1320']);

    service.set('light');
    expect([tags.light.content, tags.dark.content]).toEqual(['#1a56db', '#1a56db']);

    service.set('system');
    expect([tags.light.content, tags.dark.content]).toEqual(['#1a56db', '#0e1320']);
  });

  it('still applies the theme for this visit when storage is blocked', () => {
    const service = TestBed.inject(ThemeService);
    const blocked = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    service.set('dark');

    expect(root().getAttribute('data-theme')).toBe('dark');
    blocked.mockRestore();
  });
});
