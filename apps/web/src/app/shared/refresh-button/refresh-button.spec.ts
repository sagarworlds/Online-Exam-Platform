import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { I18nService } from '../../i18n/i18n.service';
import { RefreshButton } from './refresh-button';

@Component({
  imports: [RefreshButton],
  template: `<app-refresh-button [busy]="busy()" (refresh)="refreshed()" />`,
})
class Host {
  readonly busy = signal(false);
  readonly refreshed = vi.fn();
}

describe('RefreshButton', () => {
  function render() {
    TestBed.configureTestingModule({ imports: [Host] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!;
    return { fixture, host: fixture.componentInstance, button };
  }

  it('is named Refresh for assistive technology and draws the refresh icon', () => {
    const { fixture, button } = render();

    expect(button.getAttribute('aria-label')).toBe('Refresh');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-icon svg')).not.toBeNull();
  });

  it('shows its name in the chosen language', async () => {
    const { fixture, button } = render();

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(button.getAttribute('aria-label')).toBe('ताज़ा करें');
    localStorage.clear();
  });

  it('tells the page to read its data again when pressed', () => {
    const { host, button } = render();

    button.click();

    expect(host.refreshed).toHaveBeenCalledTimes(1);
  });

  it('says it is busy while a read is on its way, and ignores a press meanwhile', () => {
    const { fixture, host, button } = render();
    host.busy.set(true);
    fixture.detectChanges();

    button.click();

    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(host.refreshed).not.toHaveBeenCalled();
  });

  it('stays enabled while busy, so a keyboard user keeps their focus', () => {
    const { fixture, host, button } = render();
    host.busy.set(true);
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
  });

  it('is pressable again once the read is over', () => {
    const { fixture, host, button } = render();
    host.busy.set(true);
    fixture.detectChanges();
    host.busy.set(false);
    fixture.detectChanges();

    button.click();

    expect(button.hasAttribute('aria-busy')).toBe(false);
    expect(host.refreshed).toHaveBeenCalledTimes(1);
  });
});
