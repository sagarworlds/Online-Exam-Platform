import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UpdateExamDetailsRequest } from '../exam.models';
import { ExamDetailsForm } from './exam-details-form';

describe('ExamDetailsForm', () => {
  let fixture: ComponentFixture<ExamDetailsForm>;
  let root: HTMLElement;
  let saved: UpdateExamDetailsRequest[];
  let cancelled: number;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamDetailsForm] }).compileComponents();
  });

  function show(name = 'Maths mock', description: string | null = 'Chapters 1 to 4', busy = false) {
    fixture = TestBed.createComponent(ExamDetailsForm);
    fixture.componentRef.setInput('name', name);
    fixture.componentRef.setInput('description', description);
    fixture.componentRef.setInput('busy', busy);
    saved = [];
    cancelled = 0;
    fixture.componentInstance.saved.subscribe((request) => saved.push(request));
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const field = (id: string) => root.querySelector(`#${id}`) as HTMLInputElement | HTMLTextAreaElement;
  const type = (id: string, value: string) => {
    field(id).value = value;
    field(id).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

  it('starts from the name and description the exam has now', () => {
    show();

    expect(field('exam-name').value).toBe('Maths mock');
    expect(field('exam-description').value).toBe('Chapters 1 to 4');
  });

  it('starts with an empty description for an exam that has none', () => {
    show('Maths mock', null);

    expect(field('exam-description').value).toBe('');
  });

  it('sends the trimmed name and description', () => {
    show();
    type('exam-name', '  Maths mock 2 ');
    type('exam-description', ' Chapters 1 to 5 ');

    submit();

    expect(saved).toEqual([{ name: 'Maths mock 2', description: 'Chapters 1 to 5' }]);
  });

  it('sends null for a blank description, so it is cleared', () => {
    show();
    type('exam-description', '   ');

    submit();

    expect(saved).toEqual([{ name: 'Maths mock', description: null }]);
  });

  it('will not send a blank name', () => {
    show();
    type('exam-name', '   ');

    expect(button('Save details').disabled).toBe(true);
    submit();

    expect(saved).toEqual([]);
  });

  it('will not send while a request is running', () => {
    show('Maths mock', null, true);

    expect(button('Save details').disabled).toBe(true);
    expect(button('Cancel').disabled).toBe(true);
    submit();

    expect(saved).toEqual([]);
  });

  it('limits the fields to what the API accepts', () => {
    show();

    expect(field('exam-name').getAttribute('maxlength')).toBe('255');
    expect(field('exam-description').getAttribute('maxlength')).toBe('1000');
  });

  it('can be backed out of with nothing sent', () => {
    show();

    button('Cancel').click();

    expect(cancelled).toBe(1);
    expect(saved).toEqual([]);
  });
});
