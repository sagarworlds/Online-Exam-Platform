import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';
import { InviteHandover } from './invite-handover';

describe('InviteHandover', () => {
  let copy: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [InviteHandover] }).compileComponents();
    copy = vi.spyOn(TestBed.inject(ClipboardService), 'copy').mockResolvedValue(true);
  });

  function open(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(InviteHandover);
    fixture.componentRef.setInput('code', 'K7M2QX9A');
    fixture.componentRef.setInput('email', 'student@example.com');
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const button = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;

  async function click(fixture: ReturnType<typeof open>['fixture'], root: HTMLElement, label: string) {
    button(root, label)!.click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows the code, whom it works for and when it expires, as text that can be selected', () => {
    const { root } = open({ expiresAt: '2026-10-10T09:00:00Z' });

    expect(root.textContent).toContain('K7M2QX9A');
    expect(root.textContent).toContain('only for the account that holds student@example.com');
    expect(root.textContent).toContain('It expires');
    expect(root.querySelector('[aria-label="Invite code for student@example.com"]')).not.toBeNull();
  });

  it('copies the code when asked, and says so', async () => {
    const { fixture, root } = open();

    await click(fixture, root, 'Copy code');

    expect(copy).toHaveBeenCalledWith('K7M2QX9A');
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Invite code copied');
  });

  it('copies the link when asked, and says so', async () => {
    const { fixture, root } = open({ link: 'http://localhost:4200/invite?code=K7M2QX9A' });

    expect(root.textContent).toContain('http://localhost:4200/invite?code=K7M2QX9A');
    await click(fixture, root, 'Copy link');

    expect(copy).toHaveBeenCalledWith('http://localhost:4200/invite?code=K7M2QX9A');
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Link copied');
  });

  it('has no link row, and no Copy link button, when there is no link', () => {
    const { root } = open();

    expect(button(root, 'Copy link')).toBeUndefined();
  });

  it('tells the user to select the text themselves when the browser refuses', async () => {
    copy.mockResolvedValue(false);
    const { fixture, root } = open();

    await click(fixture, root, 'Copy code');

    expect(root.querySelector('[role="status"]')?.textContent).toContain('Select the text above and copy it yourself');
  });

  it('copies the code the moment it opens when asked to, and not otherwise', async () => {
    const quiet = open();
    await quiet.fixture.whenStable();
    expect(copy).not.toHaveBeenCalled();

    const eager = open({ autoCopy: true });
    await eager.fixture.whenStable();
    eager.fixture.detectChanges();

    expect(copy).toHaveBeenCalledTimes(1);
    expect(copy).toHaveBeenCalledWith('K7M2QX9A');
    expect(eager.root.querySelector('[role="status"]')?.textContent).toContain('Invite code copied');
  });

  it('offers Hide only when it is dismissible, and says it was dismissed', () => {
    expect(button(open().root, 'Hide')).toBeUndefined();

    const { fixture, root } = open({ dismissible: true });
    const dismissed = vi.fn();
    fixture.componentInstance.dismissed.subscribe(dismissed);

    button(root, 'Hide')!.click();

    expect(dismissed).toHaveBeenCalledTimes(1);
  });
});
