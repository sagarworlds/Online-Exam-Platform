import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { beforeAll } from 'vitest';
import { RichTextEditor } from './rich-text-editor';

@Component({
  imports: [RichTextEditor, ReactiveFormsModule],
  template: `<app-rich-text-editor [formControl]="control" inputId="q-text" labelledBy="q-label" />`,
})
class Host {
  readonly control = new FormControl('', { nonNullable: true });
}

describe('RichTextEditor', () => {
  // jsdom has no layout, which ProseMirror asks about when it moves the cursor into view.
  beforeAll(() => {
    const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, right: 0, bottom: 0, left: 0, toJSON: () => ({}) } as DOMRect;
    Range.prototype.getBoundingClientRect = () => rect;
    Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList;
    document.elementFromPoint = () => null;
  });

  async function open(initial = ''): Promise<{ fixture: ComponentFixture<Host>; root: HTMLElement; host: Host; editor: NonNullable<RichTextEditor['editor']> }> {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.control.setValue(initial);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const editor = (fixture.debugElement.children[0].componentInstance as RichTextEditor).editor;
    if (editor === null) {
      throw new Error('The editor was not created.');
    }
    return { fixture, root: fixture.nativeElement as HTMLElement, host: fixture.componentInstance, editor };
  }

  const button = (root: HTMLElement, label: string) => root.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;

  it('offers the formatting a question can carry, none of it pressed to start with', async () => {
    const { root } = await open();

    const labels = Array.from(root.querySelectorAll('[role="toolbar"] button')).map((b) => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Bold', 'Italic', 'Underline', 'Subscript', 'Superscript', 'Bulleted list', 'Numbered list']);
    expect(Array.from(root.querySelectorAll('[role="toolbar"] button')).every((b) => b.getAttribute('aria-pressed') === 'false')).toBe(true);
  });

  it('shows the value the form already holds', async () => {
    const { root } = await open('<p>Water is H<sub>2</sub>O</p>');

    expect(root.querySelector('.rich-editor__content sub')?.textContent).toBe('2');
  });

  it('shows a value the form sets later, without reporting it back as a change', async () => {
    const { root, host, fixture } = await open();
    let changes = 0;
    host.control.valueChanges.subscribe(() => changes++);

    host.control.setValue('<p>Later <strong>text</strong></p>');
    fixture.detectChanges();

    expect(root.querySelector('.rich-editor__content strong')?.textContent).toBe('text');
    // The one emission is the form's own setValue, not an echo from the editor.
    expect(changes).toBe(1);
  });

  it('gives the form the HTML as it is edited', async () => {
    const { host, editor } = await open();

    editor.commands.setContent('<p>Hello <em>there</em></p>', { emitUpdate: true });

    expect(host.control.value).toBe('<p>Hello <em>there</em></p>');
  });

  it('gives the form an empty string, not an empty paragraph, when there is no text', async () => {
    const { host, editor } = await open('<p>something</p>');

    editor.commands.setContent('', { emitUpdate: true });

    expect(host.control.value).toBe('');
  });

  it('applies a format to the selection from the toolbar and shows it pressed', async () => {
    const { root, host, fixture, editor } = await open('<p>abc</p>');
    editor.commands.selectAll();

    button(root, 'Bold').click();
    fixture.detectChanges();

    expect(host.control.value).toBe('<p><strong>abc</strong></p>');
    expect(button(root, 'Bold').getAttribute('aria-pressed')).toBe('true');
    expect(button(root, 'Italic').getAttribute('aria-pressed')).toBe('false');
  });

  it('makes a list from the toolbar', async () => {
    const { root, host, editor } = await open('<p>one</p>');
    editor.commands.selectAll();

    button(root, 'Bulleted list').click();

    expect(host.control.value).toBe('<ul><li><p>one</p></li></ul>');
  });

  it('has no way to make a heading or a link, which the server would strip', async () => {
    const { host, editor } = await open();

    editor.commands.setContent('<h1>Title</h1><p><a href="https://example.com">link</a></p>', { emitUpdate: true });

    expect(host.control.value).not.toContain('<h1');
    expect(host.control.value).not.toContain('<a ');
  });

  it('is a labelled text box for screen readers', async () => {
    const { root } = await open();

    const area = root.querySelector('.rich-editor__content') as HTMLElement;
    expect(area.getAttribute('role')).toBe('textbox');
    expect(area.getAttribute('aria-multiline')).toBe('true');
    expect(area.id).toBe('q-text');
    expect(area.getAttribute('aria-labelledby')).toBe('q-label');
  });

  it('cannot be edited, and its toolbar is off, while the control is disabled', async () => {
    const { root, host, fixture, editor } = await open('<p>locked</p>');

    host.control.disable();
    fixture.detectChanges();

    expect(editor.isEditable).toBe(false);
    expect(Array.from(root.querySelectorAll('[role="toolbar"] button')).every((b) => (b as HTMLButtonElement).disabled)).toBe(true);
  });
});
