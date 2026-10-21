import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RowAction, RowActions } from './row-actions';

@Component({
  imports: [RowActions],
  template: `<app-row-actions [actions]="actions()" rowLabel="Asha K." (chosen)="picked.push($event)" />`,
})
class Host {
  picked: string[] = [];
  actions = signal<RowAction[]>([
    { id: 'edit', label: 'Edit', icon: 'edit' },
    { id: 'invite', label: 'Send invitation', icon: 'mail' },
    { id: 'deactivate', label: 'Deactivate', icon: 'user-off', danger: true },
    { id: 'reset', label: 'Reset password', icon: 'key', disabled: true, disabledReason: 'Asha has no password yet.' },
  ]);
}

describe('RowActions', () => {
  const make = () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      host: fixture.componentInstance,
      root,
      icons: () => Array.from(root.querySelectorAll<HTMLButtonElement>('.row-actions--icons button')),
      words: () => Array.from(root.querySelectorAll<HTMLButtonElement>('.row-actions--words button')),
    };
  };

  it('has an icon button and a worded button for each action, and shows one form at a time by the stylesheet', () => {
    const { icons, words } = make();

    expect(icons()).toHaveLength(4);
    expect(words()).toHaveLength(4);
  });

  it('names every button for what it acts on, so a list of "Edit" buttons is not read out as one', () => {
    const { icons, words } = make();

    expect(icons().map((b) => b.getAttribute('aria-label'))).toEqual([
      'Edit Asha K.',
      'Send invitation Asha K.',
      'Deactivate Asha K.',
      'Reset password Asha K.',
    ]);
    expect(words().map((b) => b.getAttribute('aria-label'))).toEqual(icons().map((b) => b.getAttribute('aria-label')));
  });

  it('puts the words beside the icon in the form for a phone, so nothing rests on a tooltip', () => {
    const { words } = make();

    expect(words().map((b) => b.querySelector('.row-actions__label')?.textContent?.trim())).toEqual([
      'Edit',
      'Send invitation',
      'Deactivate',
      'Reset password',
    ]);
    expect(words()[0].querySelector('.row-actions__icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('tells the page which action was pressed, from either form', () => {
    const { host, icons, words } = make();

    icons()[0].click();
    words()[1].click();

    expect(host.picked).toEqual(['edit', 'invite']);
  });

  it('draws what cannot be undone as a danger button', () => {
    const { icons, words } = make();

    expect(icons()[2].classList.contains('icon-btn--danger')).toBe(true);
    expect(words()[2].classList.contains('btn--danger')).toBe(true);
    expect(words()[0].classList.contains('btn--danger')).toBe(false);
  });

  it('keeps an unavailable action on screen and focusable, says why, and does not act on it', () => {
    const { host, icons, words, root } = make();

    expect(icons()[3].getAttribute('aria-disabled')).toBe('true');
    expect(icons()[3].disabled).toBe(false);
    icons()[3].click();
    words()[3].click();
    expect(host.picked).toEqual([]);

    const reason = root.querySelector('.row-actions__reason')!;
    expect(reason.textContent).toContain('Asha has no password yet.');
    expect(words()[3].getAttribute('aria-describedby')).toBe(reason.id);
  });

  it('gives each row its own reason ids, so two rows never share one', () => {
    const first = make();
    const second = make();

    expect(first.root.querySelector('.row-actions__reason')!.id).not.toBe(second.root.querySelector('.row-actions__reason')!.id);
  });

  it('groups the buttons under the row’s name', () => {
    const { root } = make();

    const groups = Array.from(root.querySelectorAll('[role="group"]'));
    expect(groups).toHaveLength(2);
    expect(groups.every((g) => g.getAttribute('aria-label') === 'Asha K.')).toBe(true);
  });

  it('is named by the action alone when no row is given', () => {
    @Component({ imports: [RowActions], template: `<app-row-actions [actions]="[{ id: 'a', label: 'Edit', icon: 'edit' }]" />` })
    class Bare {}
    const fixture = TestBed.createComponent(Bare);
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    expect(button.getAttribute('aria-label')).toBe('Edit');
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="group"]')?.hasAttribute('aria-label')).toBe(false);
  });
});
