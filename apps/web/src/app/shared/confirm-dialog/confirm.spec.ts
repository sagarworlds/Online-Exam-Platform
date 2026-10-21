import { TestBed } from '@angular/core/testing';
import { ConfirmHost } from './confirm-host';
import { ConfirmService } from './confirm.service';

describe('ConfirmService and ConfirmHost', () => {
  const make = () => {
    const fixture = TestBed.createComponent(ConfirmHost);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, confirm: TestBed.inject(ConfirmService) };
  };
  const settle = async (fixture: { detectChanges(): void }) => {
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    fixture.detectChanges();
  };
  const button = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  it('draws nothing until a question is asked', () => {
    const { root } = make();

    expect(root.querySelector('app-modal-dialog')).toBeNull();
  });

  it('shows the question with what it means and what it will change, and says yes when the confirm button is pressed', async () => {
    const { fixture, root, confirm } = make();

    const answer = confirm.ask({
      title: 'Publish this exam?',
      message: 'Candidates can start it at its opening time.',
      consequences: ['Scope and marking can no longer be changed', 'Sections are fixed'],
      confirmLabel: 'Publish exam',
    });
    await settle(fixture);

    expect(root.querySelector('.modal__title')?.textContent).toContain('Publish this exam?');
    expect(root.querySelector('.confirm__message')?.textContent).toContain('Candidates can start it');
    expect(Array.from(root.querySelectorAll('.confirm__consequences li')).map((li) => li.textContent?.trim())).toEqual([
      'Scope and marking can no longer be changed',
      'Sections are fixed',
    ]);
    button(root, 'Publish exam').click();
    await settle(fixture);

    await expect(answer).resolves.toBe(true);
    expect(root.querySelector('app-modal-dialog')).toBeNull();
  });

  it('says no for Cancel, in the language of the page unless the caller words it', async () => {
    const { fixture, root, confirm } = make();
    const plain = confirm.ask({ title: 'Delete?', confirmLabel: 'Delete' });
    await settle(fixture);
    expect(button(root, 'Cancel')).toBeTruthy();
    button(root, 'Cancel').click();
    await settle(fixture);
    await expect(plain).resolves.toBe(false);

    const worded = confirm.ask({ title: 'Delete?', confirmLabel: 'Delete', cancelLabel: 'Keep it' });
    await settle(fixture);
    expect(button(root, 'Keep it')).toBeTruthy();
    button(root, 'Keep it').click();
    await settle(fixture);
    await expect(worded).resolves.toBe(false);
  });

  it('says no when the dialog is closed any other way, with its own close button', async () => {
    const { fixture, root, confirm } = make();
    const answer = confirm.ask({ title: 'Delete?', confirmLabel: 'Delete' });
    await settle(fixture);

    root.querySelector<HTMLButtonElement>('.modal__close')!.click();
    await settle(fixture);

    await expect(answer).resolves.toBe(false);
  });

  it('puts the focus on Cancel when the answer cannot be undone, and on the confirm button when it can', async () => {
    const { fixture, root, confirm } = make();

    void confirm.ask({ title: 'Revoke?', confirmLabel: 'Revoke', danger: true });
    await settle(fixture);
    expect(document.activeElement).toBe(button(root, 'Cancel'));
    expect(button(root, 'Revoke').classList.contains('danger')).toBe(true);
    expect(button(root, 'Revoke').classList.contains('primary')).toBe(false);
    button(root, 'Cancel').click();
    await settle(fixture);

    void confirm.ask({ title: 'Publish?', confirmLabel: 'Publish' });
    await settle(fixture);
    expect(document.activeElement).toBe(button(root, 'Publish'));
    expect(button(root, 'Publish').classList.contains('primary')).toBe(true);
  });

  it('asks a second question after the first is answered, not over it', async () => {
    const { fixture, root, confirm } = make();
    const first = confirm.ask({ title: 'First?', confirmLabel: 'Yes first' });
    const second = confirm.ask({ title: 'Second?', confirmLabel: 'Yes second' });
    await settle(fixture);
    expect(root.querySelectorAll('app-modal-dialog')).toHaveLength(1);
    expect(root.querySelector('.modal__title')?.textContent).toContain('First?');

    button(root, 'Yes first').click();
    await settle(fixture);
    await expect(first).resolves.toBe(true);
    expect(root.querySelector('.modal__title')?.textContent).toContain('Second?');

    button(root, 'Cancel').click();
    await settle(fixture);
    await expect(second).resolves.toBe(false);
  });

  it('ignores an answer when no question is on screen', () => {
    const { confirm } = make();

    expect(() => confirm.answer(true)).not.toThrow();
  });
});
