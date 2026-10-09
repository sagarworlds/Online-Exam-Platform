import { ChangeDetectorRef, Component, DestroyRef, ElementRef, afterNextRender, forwardRef, inject, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { ChainedCommands, Editor } from '@tiptap/core';
import Subscript from '@tiptap/extension-subscript';
import Superscript from '@tiptap/extension-superscript';
import Image from '@tiptap/extension-image';
import StarterKit from '@tiptap/starter-kit';

import { IMAGE_LIMITS, ImageRejectedError } from './image-limits';
import { IMAGE_PREPARER } from './image-resizer';

/** One formatting button in the toolbar. `name` is the TipTap node or mark it toggles, which also tells whether it is active. */
interface ToolbarAction {
  readonly name: string;
  readonly label: string;
  readonly glyph: string;
  readonly run: (chain: ChainedCommands) => ChainedCommands;
}

/** What the formula button inserts: a worked example to overwrite, since the author sees the typeset result only when the question is shown. */
export const FORMULA_TEMPLATE = '$x^{2}$';

const ACTIONS: readonly ToolbarAction[] = [
  { name: 'bold', label: 'Bold', glyph: 'B', run: (chain) => chain.toggleBold() },
  { name: 'italic', label: 'Italic', glyph: 'I', run: (chain) => chain.toggleItalic() },
  { name: 'underline', label: 'Underline', glyph: 'U', run: (chain) => chain.toggleUnderline() },
  { name: 'subscript', label: 'Subscript', glyph: 'x₂', run: (chain) => chain.toggleSubscript() },
  { name: 'superscript', label: 'Superscript', glyph: 'x²', run: (chain) => chain.toggleSuperscript() },
  { name: 'bulletList', label: 'Bulleted list', glyph: '• List', run: (chain) => chain.toggleBulletList() },
  { name: 'orderedList', label: 'Numbered list', glyph: '1. List', run: (chain) => chain.toggleOrderedList() },
  { name: 'code', label: 'Inline code', glyph: '</>', run: (chain) => chain.toggleCode() },
  { name: 'codeBlock', label: 'Code block', glyph: '{ }', run: (chain) => chain.toggleCodeBlock() },
  // A formula is plain text between dollar signs, typeset where the question is shown (see math-typesetter.ts).
  { name: 'formula', label: 'Insert formula (LaTeX)', glyph: 'Σ', run: (chain) => chain.insertContent(FORMULA_TEMPLATE) },
];

/** The editor keeps an empty paragraph after a list or picture so there is always somewhere to keep typing; it is not worth storing. */
function withoutTrailingEmptyParagraphs(html: string): string {
  return html.replace(/(?:<p><\/p>|<p><br\s*\/?><\/p>)+$/, '');
}

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
  protected readonly acceptedImageTypes = IMAGE_LIMITS.acceptedTypes.join(',');
  protected readonly disabled = signal(false);
  /** Why the last picture was refused, shown under the toolbar until the next attempt. */
  protected readonly imageError = signal<string | null>(null);

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly prepareImage = inject(IMAGE_PREPARER);

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

  /**
   * The name a screen reader announces for a formatting button: the glyph it shows, then its label, so the visible text is in the name
   * (WCAG 2.5.3). The glyph is not wrapped in brackets: axe does not match a bracketed glyph (such as "Subscript (x₂)") to the visible text.
   */
  protected accessibleName(action: ToolbarAction): string {
    return `${action.glyph} ${action.label}`;
  }

  protected run(action: ToolbarAction): void {
    if (this.tiptap !== null) {
      action.run(this.tiptap.chain().focus()).run();
    }
  }

  /** Adds the picture chosen with the toolbar's file picker, shrunk to the limits; says why when it cannot be used. */
  protected async onImagePicked(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Cleared so choosing the same file again still fires a change.
    input.value = '';
    if (file === undefined || this.tiptap === null) {
      return;
    }

    this.imageError.set(null);
    if (this.imageCount() >= IMAGE_LIMITS.maxPerQuestion) {
      this.imageError.set(`A question can have at most ${IMAGE_LIMITS.maxPerQuestion} pictures.`);
      return;
    }

    try {
      const src = await this.prepareImage(file);
      // Inserted after the selection, never over it: a picture just added stays selected, and replacing the
      // selection would swap the new picture for the one before it instead of adding a second.
      const after = this.tiptap.state.selection.to;
      this.tiptap.chain().focus().insertContentAt(after, { type: 'image', attrs: { src, alt: 'Question image' } }).run();
    } catch (error) {
      if (error instanceof ImageRejectedError) {
        this.imageError.set(error.message);
      } else {
        // Not the author's file: something is broken. Keep the evidence and still tell the author it failed.
        console.error('Preparing a picture failed', error);
        this.imageError.set('The picture could not be added. Please try another one.');
      }
    }
  }

  private imageCount(): number {
    let count = 0;
    this.tiptap?.state.doc.descendants((node) => {
      if (node.type.name === 'image') {
        count++;
      }
    });
    return count;
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
        StarterKit.configure({ heading: false, horizontalRule: false, link: false }),
        Subscript,
        Superscript,
        // Pictures live inside the HTML as data URLs; the server refuses any other source.
        Image.configure({ allowBase64: true }),
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
        this.onChange(editor.isEmpty ? '' : withoutTrailingEmptyParagraphs(editor.getHTML()));
        // The editor reports from outside Angular's event handling, and the app is zoneless: tell it the form changed.
        this.changeDetector.markForCheck();
      },
      onBlur: () => this.onTouched(),
    });
  }
}
