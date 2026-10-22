import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { syncQueryParam } from './query-state';

type Status = '' | 'Draft' | 'Published';

@Component({ template: '' })
class Page {
  readonly search = signal('');
  readonly status = signal<Status>('');
  readonly tab = signal<'open' | 'done'>('open');

  constructor() {
    syncQueryParam('q', this.search);
    syncQueryParam('status', this.status, { allowed: ['Draft', 'Published'] });
    syncQueryParam('tab', this.tab, { allowed: ['open', 'done'], default: 'open' });
  }
}

/** Lets the change reach the address: effects run in change detection, the write follows in a microtask, and the navigation after it. */
async function settle(harness: RouterTestingHarness): Promise<void> {
  harness.detectChanges();
  await new Promise((resolve) => setTimeout(resolve));
  await harness.fixture.whenStable();
}

describe('syncQueryParam', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([{ path: 'list', component: Page }])] }));

  const urlOf = () => TestBed.inject(Router).url;

  it('takes what the address carries, so a shared link opens the list as it was', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/list?q=asha&status=Published&tab=done', Page);

    expect(page.search()).toBe('asha');
    expect(page.status()).toBe('Published');
    expect(page.tab()).toBe('done');
  });

  it('ignores a value the list has no words for, rather than showing a state it cannot describe', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/list?status=Banana&tab=sideways', Page);

    expect(page.status()).toBe('');
    expect(page.tab()).toBe('open');
  });

  it('writes a change into the address, replacing the entry, and leaves out what is empty or the default', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/list', Page);

    page.search.set('asha');
    page.status.set('Draft');
    page.tab.set('done');
    harness.detectChanges();
    await settle(harness);
    expect(urlOf()).toBe('/list?q=asha&status=Draft&tab=done');

    page.status.set('');
    page.tab.set('open');
    harness.detectChanges();
    await settle(harness);
    expect(urlOf()).toBe('/list?q=asha');
  });

  it('follows the address when it changes under the page, and back to the default when the filters are left out', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/list?q=asha&status=Draft', Page);

    await harness.navigateByUrl('/list?q=bob', Page);
    expect(page.search()).toBe('bob');
    expect(page.status()).toBe('');

    await harness.navigateByUrl('/list', Page);
    expect(page.search()).toBe('');
  });

  it('does not put an older text back when the person types on before their own write has landed', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/list', Page);

    page.search.set('a');
    harness.detectChanges();
    await Promise.resolve();
    page.search.set('ab');
    await settle(harness);

    expect(page.search()).toBe('ab');
    expect(urlOf()).toBe('/list?q=ab');
  });

  it('keeps other parameters of the address as they are', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/list?keep=me', Page);

    page.search.set('x');
    harness.detectChanges();
    await settle(harness);

    expect(urlOf()).toContain('keep=me');
    expect(urlOf()).toContain('q=x');
  });
});
