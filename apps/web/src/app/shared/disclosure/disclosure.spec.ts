import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Disclosure, DisclosureGroup, DisclosureStatus } from './disclosure';

@Component({
  imports: [Disclosure],
  template: `
    <app-disclosure
      heading="Schedule"
      summary="Opens Oct 24"
      [status]="status()"
      [statusLabel]="label()"
      [rememberAs]="key()"
      [group]="group()"
      [(open)]="open"
    >
      <p class="inside">The form</p>
    </app-disclosure>
  `,
})
class Host {
  status = signal<DisclosureStatus>('none');
  label = signal('');
  key = signal<string | null>(null);
  group = signal<string | null>(null);
  open = signal(false);
}

@Component({
  imports: [Disclosure, DisclosureGroup],
  template: `
    <app-disclosure-group>
      <app-disclosure heading="One" [(open)]="one" />
      <app-disclosure heading="Two" [(open)]="two" />
    </app-disclosure-group>
  `,
})
class GroupHost {
  one = signal(false);
  two = signal(true);
}

describe('Disclosure', () => {
  beforeEach(() => sessionStorage.clear());

  const make = (setup: (host: Host) => void = () => undefined) => {
    const fixture = TestBed.createComponent(Host);
    setup(fixture.componentInstance);
    fixture.detectChanges();
    return fixture;
  };
  const details = (fixture: ComponentFixture<unknown>) => (fixture.nativeElement as HTMLElement).querySelector('details')!;
  const text = (fixture: ComponentFixture<unknown>, selector: string) =>
    (fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent?.trim();

  /** What the browser does when a person presses the row: the block flips and a `toggle` event follows. */
  const press = (fixture: ComponentFixture<unknown>) => {
    const block = details(fixture);
    block.open = !block.open;
    block.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
  };

  it('is the browser’s own details element, so the keyboard and a screen reader work with nothing added', () => {
    const fixture = make();

    expect(details(fixture).querySelector('summary')).not.toBeNull();
    expect(text(fixture, '.disclosure__title')).toBe('Schedule');
    expect(text(fixture, '.disclosure__summary')).toBe('Opens Oct 24');
    expect(details(fixture).open).toBe(false);
  });

  it('puts the title in plain text, not a heading, which would lose its role inside a summary', () => {
    const fixture = make();

    expect((fixture.nativeElement as HTMLElement).querySelector('summary h1, summary h2, summary h3, summary h4')).toBeNull();
  });

  it('shows a status as a mark and as words, so it is not colour alone, and nothing when there is none', () => {
    const none = make();
    expect((none.nativeElement as HTMLElement).querySelector('.disclosure__mark')).toBeNull();
    expect((none.nativeElement as HTMLElement).querySelector('.disclosure__tag')).toBeNull();

    const warning = make((host) => {
      host.status.set('warning');
      host.label.set('Not scheduled');
    });
    expect(text(warning, '.disclosure__mark')).toBe('!');
    expect(warning.nativeElement.querySelector('.disclosure__mark').getAttribute('aria-hidden')).toBe('true');
    expect(text(warning, '.disclosure__tag')).toBe('Not scheduled');
    expect(details(warning).getAttribute('data-status')).toBe('warning');

    expect(text(make((host) => host.status.set('ok')), '.disclosure__mark')).toBe('✓');
  });

  it('follows the page’s open state, and tells the page when a person presses the row', () => {
    const fixture = make();
    const host = fixture.componentInstance;

    host.open.set(true);
    fixture.detectChanges();
    expect(details(fixture).open).toBe(true);

    press(fixture);
    expect(host.open()).toBe(false);
    press(fixture);
    expect(host.open()).toBe(true);
  });

  it('opens itself when its status becomes an error, so a problem is never behind a closed row', () => {
    const fixture = make();
    expect(details(fixture).open).toBe(false);

    fixture.componentInstance.status.set('error');
    fixture.componentInstance.label.set('2 problems');
    fixture.detectChanges();

    expect(fixture.componentInstance.open()).toBe(true);
    expect(details(fixture).open).toBe(true);
  });

  it('keeps open or closed for the visit under the name it was given, and finds it again', () => {
    const first = make((host) => host.key.set('exam-editor.schedule'));
    press(first);
    expect(sessionStorage.getItem('disclosure.exam-editor.schedule')).toBe('open');
    first.destroy();

    const again = make((host) => host.key.set('exam-editor.schedule'));
    expect(again.componentInstance.open()).toBe(true);
    expect(details(again).open).toBe(true);

    press(again);
    expect(sessionStorage.getItem('disclosure.exam-editor.schedule')).toBe('closed');
  });

  it('keeps nothing without a name', () => {
    const fixture = make();
    press(fixture);

    expect(sessionStorage.length).toBe(0);
  });

  it('carries on when the store is blocked', () => {
    const blocked = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const fixture = make((host) => host.key.set('x'));

    expect(details(fixture).open).toBe(false);
    blocked.mockRestore();
  });

  it('joins a group, so the browser closes the others when this one opens', () => {
    const fixture = make((host) => host.group.set('settings'));

    expect(details(fixture).getAttribute('name')).toBe('settings');
    expect(details(make()).hasAttribute('name')).toBe(false);
  });
});

describe('DisclosureGroup', () => {
  it('opens every block with Expand all and closes every block with Collapse all', () => {
    const fixture = TestBed.createComponent(GroupHost);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('.disclosure-group__tools button'));
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['Expand all', 'Collapse all']);

    buttons[0].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.one()).toBe(true);
    expect(fixture.componentInstance.two()).toBe(true);
    expect(Array.from(root.querySelectorAll('details')).every((d) => d.open)).toBe(true);

    buttons[1].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.one()).toBe(false);
    expect(fixture.componentInstance.two()).toBe(false);
    expect(Array.from(root.querySelectorAll('details')).some((d) => d.open)).toBe(false);
  });
});
