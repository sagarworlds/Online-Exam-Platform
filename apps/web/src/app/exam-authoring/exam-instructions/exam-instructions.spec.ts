import { ComponentFixture, TestBed } from '@angular/core/testing';
import { InstructionTemplateDto } from '../../instruction-templates/instruction-template.models';
import { ExamInstructions } from './exam-instructions';

const template: InstructionTemplateDto = {
  id: 'tpl-1',
  title: 'Board rules',
  body: 'Bring a pencil.',
  createdAtUtc: '2026-10-10T05:00:00Z',
  updatedAtUtc: '2026-10-10T05:00:00Z',
};

describe('ExamInstructions', () => {
  let fixture: ComponentFixture<ExamInstructions>;
  let root: HTMLElement;

  function create(inputs: { instructions?: string | null; editable?: boolean; templates?: InstructionTemplateDto[] }): void {
    fixture = TestBed.createComponent(ExamInstructions);
    fixture.componentRef.setInput('instructions', inputs.instructions ?? null);
    fixture.componentRef.setInput('editable', inputs.editable ?? true);
    fixture.componentRef.setInput('templates', inputs.templates ?? []);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  function buttonWithText(text: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === text);
  }

  function typeInto(value: string): void {
    const field = root.querySelector('#exam-instructions-text') as HTMLTextAreaElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('keeps Save off until the text differs from what the exam has', () => {
    create({ instructions: 'Read every question twice.' });

    expect(buttonWithText('Save')?.disabled).toBe(true);

    typeInto('Read every question twice. Then answer.');
    expect(buttonWithText('Save')?.disabled).toBe(false);
  });

  it('saves the text trimmed, so stray spaces and blank lines are not kept', () => {
    create({ instructions: null });
    const changed: string[] = [];
    fixture.componentInstance.changed.subscribe((text) => changed.push(text));

    typeInto('   Sit quietly.   ');
    buttonWithText('Save')?.click();

    expect(changed).toEqual(['Sit quietly.']);
  });

  it('shows the text as fixed, with no form, once the exam is published', () => {
    create({ instructions: 'Sit quietly.', editable: false });

    expect(root.querySelector('#exam-instructions-text')).toBeNull();
    expect(root.querySelector('.instructions__text')?.textContent).toContain('Sit quietly.');
  });

  it('copies the chosen template by its id, and sets aside text typed but not saved', () => {
    create({ instructions: null, templates: [template] });
    const chosen: string[] = [];
    fixture.componentInstance.templateChosen.subscribe((id) => chosen.push(id));
    typeInto('Half-written text');

    const select = root.querySelector('#exam-instructions-template') as HTMLSelectElement;
    select.value = 'tpl-1';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    buttonWithText('Copy the template’s text')?.click();
    fixture.detectChanges();

    expect(chosen).toEqual(['tpl-1']);
    expect((root.querySelector('#exam-instructions-text') as HTMLTextAreaElement).value).toBe('');
  });

  it('says there are no templates to copy from, instead of an empty list', () => {
    create({ instructions: null, templates: [] });

    expect(root.textContent).toContain('There are no templates yet.');
    expect(root.querySelector('#exam-instructions-template')).toBeNull();
  });
});
