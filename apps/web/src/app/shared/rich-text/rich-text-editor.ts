import { ChangeDetectorRef, Component, DestroyRef, ElementRef, afterNextRender, forwardRef, inject, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { ChainedCommands, Editor } from '@tiptap/core';
import Subscript from '@tiptap/extension-subscript';
import Superscript from '@tiptap/extension-superscript';
import StarterKit from '@tiptap/starter-kit';

/** One formatting button in the toolbar. `name` is the TipTap node or mark it toggles, which also tells whether it is active. */
interface ToolbarAction {
  readonly name: string;
  readonly label: string;
  readonly glyph: string;
  readonly run: (chain: ChainedCommands) => ChainedCommands;
}

const ACTIONS: readonly ToolbarAction[] = [
  { name: 'bold', label: 'Bold', glyph: 'B', run: (chain) => chain.toggleBold() },
  { name: 'italic', label: 'Italic', glyph: 'I', run: (chain) => chain.toggleItalic() },
  { name: 'underline', label: 'Underline', glyph: 'U', run: (chain) => chain.toggleUnderline() },
  { name: 'subscript', label: 'Subscript', glyph: 'x₂', run: (chain) => chain.toggleSubscript() },
  { name: 'superscript', label: 'Superscript', glyph: 'x²', run: (chain) => chain.toggleSuperscript() },
  { name: 'bulletList', label: 'Bulleted list', glyph: '• List', run: (chain) => chain.toggleBulletList() },
  { name: 'orderedList', label: 'Numbered list', glyph: '1. List', run: (chain) => chain.toggleOrderedList() },
];

/**
 * A rich-text field for forms: `<app-rich-text-editor formControlName="text" />`. Its value is HTML, and `''` while
 * empty, so `Validators.required` treats a blank document as blank.
 *
 * The editor only produces the formatting the API's sanitizer allows (headings, links and the rest of the starter
 * kit are switched off), but that is a convenience for the author, not a safeguard: the server sanitizes whatever
 * it is sent, and the text is sanitized again where it is shown.
 */
@Component({
  selector: 'app-rich-text-editor',
  templateUrl: './rich-text-editor.html',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => RichTextEditor), multi: true }],
})
export class RichTextEditor implements ControlValueAccessor {
  /** The id of the editable area, so a label can point at it. */
  readonly inputId = input<string | null>(null);
  /** The id of the element that labels the editable area, for screen readers. */
  readonly labelledBy = input<string | null>(null);

  protected readonly actions = ACTIONS;
  protected readonly disabled = signal(false);

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly changeDetector = inject(ChangeDetectorRef);

  /** Bumped on every editor transaction so the toolbar's pressed states are re-read; the editor itself is not a signal. */
  private readonly revision = signal(0);

  private tiptap: Editor | null = null;
  private value = '';
  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  constructor() {
    afterNextRender(() => this.createEditor());
    inject(DestroyRef).onDestroy(() => this.tiptap?.destroy());
  }

  /** The underlying TipTap editor, once the view exists. Exposed for tests; the form control is the real interface. */
  get editor(): Editor | null {
    return this.tiptap;
  }

  /** Whether a toolbar button's format applies at the cursor or selection (drives `aria-pressed`). */
  protected isActive(action: ToolbarAction): boolean {
    this.revision();
    return this.tiptap?.isActive(action.name) ?? false;
  }

  protected run(action: ToolbarAction): void {
    if (this.tiptap !== null) {
      action.run(this.tiptap.chain().focus()).run();
    }
  }

  writeValue(value: string | null): void {
    this.value = value ?? '';
    // The form already holds this value, so loading it into the editor must not report it back as a change.
    this.tiptap?.commands.setContent(this.value, { emitUpdate: false });
  }

  registerOnChange(onChange: (value: string) => void): void {
    this.onChange = onChange;
  }

  registerOnTouched(onTouched: () => void): void {
    this.onTouched = onTouched;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    this.tiptap?.setEditable(!isDisabled);
  }

  private createEditor(): void {
    this.tiptap = new Editor({
      element: this.host().nativeElement,
      content: this.value,
      editable: !this.disabled(),
      extensions: [
        // Only what the sanitizer on the API accepts: headings, rules and links would be stripped on save.
        StarterKit.configure({ heading: false, horizontalRule: false, link: false, trailingNode: false }),
        Subscript,
        Superscript,
      ],
      editorProps: {
        attributes: {
          class: 'rich-editor__content rich-text',
          role: 'textbox',
          'aria-multiline': 'true',
          ...(this.inputId() ? { id: this.inputId() as string } : {}),
          ...(this.labelledBy() ? { 'aria-labelledby': this.labelledBy() as string } : {}),
        },
      },
      onTransaction: () => this.revision.update((n) => n + 1),
      onUpdate: ({ editor }) => {
        // An empty document is '' rather than '<p></p>', so a required field sees it as blank.
        this.onChange(editor.isEmpty ? '' : editor.getHTML());
        // The editor reports from outside Angular's event handling, and the app is zoneless: tell it the form changed.
        this.changeDetector.markForCheck();
      },
      onBlur: () => this.onTouched(),
    });
  }
}
