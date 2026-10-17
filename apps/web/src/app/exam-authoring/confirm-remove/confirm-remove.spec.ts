import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmRemove } from './confirm-remove';

describe('ConfirmRemove', () => {
  let fixture: ComponentFixture<ConfirmRemove>;
  let root: HTMLElement;
  let confirmed: number;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ConfirmRemove] }).compileComponents();
  });

  function show(busy?: boolean) {
    fixture = TestBed.createComponent(ConfirmRemove);
    fixture.componentRef.setInput('label', 'Remove Algebra from the exam');
    fixture.componentRef.setInput('prompt', 'Remove this section from the exam?');
    fixture.componentRef.setInput('confirmLabel', 'Remove section');
    if (busy !== undefined) fixture.componentRef.setInput('busy', busy);
    confirmed = 0;
    fixture.componentInstance.confirmed.subscribe(() => confirmed++);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const trigger = () => root.querySelector('.confirm-remove__trigger') as HTMLButtonElement;
  const dialog = () => root.querySelector('[role="alertdialog"]') as HTMLElement | null;
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  /** Lets the render that follows a click finish, which is when focus moves. */
  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function ask() {
    trigger().click();
    await settle();
  }

  it('is a trash icon with no words of its own, named by its label for a screen reader and a mouse', () => {
    show();

    expect(trigger().textContent?.trim()).toBe('');
    expect(trigger().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(trigger().getAttribute('aria-label')).toBe('Remove Algebra from the exam');
    expect(trigger().getAttribute('title')).toBe('Remove Algebra from the exam');
    expect(dialog()).toBeNull();
  });

  it('asks before removing: the question and two buttons take the icon\'s place, and nothing is sent', async () => {
    show();

    await ask();

    expect(trigger()).toBeNull();
    expect(dialog()?.textContent).toContain('Remove this section from the exam?');
    expect(dialog()?.getAttribute('aria-label')).toBe('Remove Algebra from the exam');
    expect(button('Remove section')).toBeDefined();
    expect(button('Cancel')).toBeDefined();
    expect(confirmed).toBe(0);
  });

  it('puts focus on Cancel, so a key pressed again does not remove', async () => {
    show();

    await ask();

    expect(document.activeElement).toBe(button('Cancel'));
  });

  it('confirms once the author says so', async () => {
    show();
    await ask();

    button('Remove section').click();
    await settle();

    expect(confirmed).toBe(1);
    expect(dialog()).toBeNull();
  });

  it('removes nothing when cancelled, and gives focus back to the icon', async () => {
    show();
    await ask();

    button('Cancel').click();
    await settle();

    expect(confirmed).toBe(0);
    expect(dialog()).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('takes the question back when Escape is pressed, and gives focus back to the icon', async () => {
    show();
    await ask();

    button('Cancel').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(confirmed).toBe(0);
    expect(dialog()).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('does nothing for Escape when it is not asking', async () => {
    show();
    trigger().focus();

    trigger().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(dialog()).toBeNull();
    expect(confirmed).toBe(0);
  });

  it('cannot be started while a request is running', () => {
    show(true);

    expect(trigger().disabled).toBe(true);
  });

  it('cannot be confirmed while a request is running, and asks again once it is over', async () => {
    show();
    await ask();

    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();
    expect(button('Remove section').disabled).toBe(true);
    button('Remove section').click();
    expect(confirmed).toBe(0);

    fixture.componentRef.setInput('busy', false);
    fixture.detectChanges();
    expect(button('Remove section').disabled).toBe(false);
  });
});
