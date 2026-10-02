import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { INSTANT_RELEASE, ReleaseSelection } from './exam-release';
import { ExamReleaseFields } from './exam-release-fields';

@Component({
  imports: [ExamReleaseFields],
  template: `<app-exam-release-fields [(selection)]="selection" [disabled]="disabled()" />`,
})
class Host {
  readonly selection = signal<ReleaseSelection>({ ...INSTANT_RELEASE });
  readonly disabled = signal(false);
}

describe('ExamReleaseFields', () => {
  function open() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  const radio = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).find((r) => r.parentElement?.textContent?.includes(label)) as HTMLInputElement;

  it('offers the three ways to release the answers, starting on "right after they submit"', () => {
    const { root } = open();

    expect(Array.from(root.querySelectorAll('input[type="radio"]'))).toHaveLength(3);
    expect(radio(root, 'Right after they submit').checked).toBe(true);
    expect(root.querySelector('input[type="datetime-local"]')).toBeNull();
  });

  it('warns that answers shown right away can be passed on to those still sitting the exam', () => {
    const { root } = open();

    expect(root.textContent).toContain('pass the correct answers');
  });

  it('asks for a time only for a scheduled release, and reports the choice to its parent', () => {
    const { fixture, host, root } = open();

    radio(root, 'From a set time').click();
    fixture.detectChanges();
    const time = root.querySelector('input[type="datetime-local"]') as HTMLInputElement;
    time.value = '2026-10-08T14:30';
    time.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(host.selection()).toEqual({ mode: 'Scheduled', localTime: '2026-10-08T14:30' });
    expect(root.textContent).not.toContain('pass the correct answers');
  });

  it('keeps the time when the mode changes, so a slip of the mouse does not lose it', () => {
    const { fixture, host, root } = open();
    host.selection.set({ mode: 'Scheduled', localTime: '2026-10-08T14:30' });
    fixture.detectChanges();

    radio(root, 'When I release them').click();
    fixture.detectChanges();
    expect(host.selection()).toEqual({ mode: 'Manual', localTime: '2026-10-08T14:30' });
    expect(root.querySelector('input[type="datetime-local"]')).toBeNull();

    radio(root, 'From a set time').click();
    fixture.detectChanges();
    expect((root.querySelector('input[type="datetime-local"]') as HTMLInputElement).value).toBe('2026-10-08T14:30');
  });

  it('is disabled as a whole while a save is in flight', () => {
    const { fixture, host, root } = open();

    host.disabled.set(true);
    fixture.detectChanges();

    expect(root.querySelector('fieldset')?.disabled).toBe(true);
  });
});
