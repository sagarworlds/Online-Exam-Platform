import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { beforeAll, vi } from 'vitest';
import { ImageRejectedError } from './image-limits';
import { IMAGE_PREPARER, ImagePreparer } from './image-resizer';
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

  const PICTURE = 'data:image/png;base64,AAAA';

  async function open(
    initial = '',
    prepare: ImagePreparer = () => Promise.resolve(PICTURE),
  ): Promise<{ fixture: ComponentFixture<Host>; root: HTMLElement; host: Host; editor: NonNullable<RichTextEditor['editor']> }> {
    await TestBed.configureTestingModule({ imports: [Host], providers: [{ provide: IMAGE_PREPARER, useValue: prepare }] }).compileComponents();
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

  /** Chooses a file in the toolbar's picker the way the browser would, then lets the async work finish. */
  async function pick(fixture: ComponentFixture<Host>, root: HTMLElement, file = new File(['x'], 'p.png', { type: 'image/png' })): Promise<void> {
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('offers the formatting a question can carry, none of it pressed to start with', async () => {
    const { root } = await open();

    // The formatting buttons are toggles and report whether they apply; the image button is an action, not a toggle.
    const toggles = Array.from(root.querySelectorAll('[role="toolbar"] button[aria-pressed]'));
    expect(toggles.map((b) => b.getAttribute('aria-label'))).toEqual(['Bold', 'Italic', 'Underline', 'Subscript', 'Superscript', 'Bulleted list', 'Numbered list', 'Inline code', 'Code block', 'Insert formula (LaTeX)']);
    expect(toggles.every((b) => b.getAttribute('aria-pressed') === 'false')).toBe(true);
    expect(button(root, 'Insert image').hasAttribute('aria-pressed')).toBe(false);
  });

  it('makes a code block, which the API keeps as pre and code', async () => {
    const { host, root, fixture, editor } = await open('<p>x</p>');

    button(root, 'Code block').click();
    fixture.detectChanges();

    expect(host.control.value).toBe('<pre><code>x</code></pre>');
    expect(editor.isActive('codeBlock')).toBe(true);
  });

  it('inserts a formula as plain text between dollar signs for the author to change', async () => {
    const { host, root } = await open('<p>Solve </p>');

    button(root, 'Insert formula (LaTeX)').click();

    expect(host.control.value).toContain('$x^{2}$');
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

  it('has an image button that opens the file picker, which offers only the picture types the server takes', async () => {
    const { root } = await open();
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    const opened = vi.spyOn(input, 'click');

    button(root, 'Insert image').click();

    expect(opened).toHaveBeenCalled();
    expect(input.accept).toContain('image/png');
    expect(input.accept).not.toContain('svg');
  });

  it('adds a chosen picture to the text', async () => {
    const { fixture, root, host } = await open('<p>Which shape?</p>');

    await pick(fixture, root);

    expect(host.control.value).toContain(`<img src="${PICTURE}" alt="Question image">`);
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('says why a picture cannot be used, and adds nothing', async () => {
    const { fixture, root, host } = await open('<p>Q</p>', () => Promise.reject(new ImageRejectedError('Use a PNG, JPEG, GIF or WebP picture.')));

    await pick(fixture, root);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Use a PNG, JPEG, GIF or WebP picture.');
    expect(host.control.value).toBe('<p>Q</p>');
    expect(root.querySelector('.rich-editor__content img')).toBeNull();
  });

  it('tells the author, and logs the cause, when preparing a picture fails for another reason', async () => {
    const logged = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { fixture, root } = await open('', () => Promise.reject(new Error('canvas exploded')));

    await pick(fixture, root);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could not be added');
    expect(logged).toHaveBeenCalled();
    logged.mockRestore();
  });

  it('adds a second picture after the first instead of replacing it', async () => {
    let n = 0;
    const { fixture, root, host } = await open('<p>Which shape?</p>', () => Promise.resolve(`data:image/png;base64,AAA${++n}`));

    await pick(fixture, root);
    await pick(fixture, root);
    await pick(fixture, root);

    const pictures = host.control.value.match(/<img /g) ?? [];
    expect(pictures.length).toBe(3);
    expect(host.control.value).toContain('AAA1');
    expect(host.control.value).toContain('AAA2');
    expect(host.control.value).toContain('AAA3');
  });

  it('does not store the empty paragraph the editor keeps after a picture or list', async () => {
    const { fixture, root, host } = await open('<p>Which shape?</p>');

    await pick(fixture, root);

    expect(host.control.value).not.toMatch(/<p><\/p>$/);
    expect(host.control.value.endsWith('>')).toBe(true);
  });

  it('refuses a sixth picture without preparing it', async () => {
    const prepare = vi.fn(() => Promise.resolve(PICTURE));
    const { fixture, root, editor } = await open('', prepare);
    editor.commands.setContent(Array.from({ length: 5 }, () => `<img src="${PICTURE}">`).join(''), { emitUpdate: false });

    await pick(fixture, root);

    expect(prepare).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('at most 5 pictures');
  });

  it('clears an earlier complaint when the next picture is fine', async () => {
    let fail = true;
    const { fixture, root } = await open('', () => (fail ? Promise.reject(new ImageRejectedError('Nope.')) : Promise.resolve(PICTURE)));
    await pick(fixture, root);
    expect(root.querySelector('[role="alert"]')).not.toBeNull();

    fail = false;
    await pick(fixture, root);

    expect(root.querySelector('[role="alert"]')).toBeNull();
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
