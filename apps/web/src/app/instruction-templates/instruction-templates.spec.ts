import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { InstructionTemplateDto } from './instruction-template.models';
import { InstructionTemplates } from './instruction-templates';

const template = (id: string, title: string, body = 'Bring a pencil.'): InstructionTemplateDto => ({
  id,
  title,
  body,
  createdAtUtc: '2026-10-10T05:00:00Z',
  updatedAtUtc: '2026-10-10T05:00:00Z',
});

describe('InstructionTemplates', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<InstructionTemplates>;
  let root: HTMLElement;

  const isList = (request: { method: string; url: string }) =>
    request.method === 'GET' && request.url.endsWith('/v1/instruction-templates');

  function buttonWithText(text: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === text);
  }

  function type(selector: string, value: string): void {
    const field = root.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function open(rows: InstructionTemplateDto[] | 'error'): void {
    fixture = TestBed.createComponent(InstructionTemplates);
    fixture.detectChanges();
    const list = httpMock.expectOne(isList);
    if (rows === 'error') {
      list.flush({ title: 'server_error', detail: 'Try later.' }, { status: 500, statusText: 'Server Error' });
    } else {
      list.flush(rows);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InstructionTemplates],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists the templates by title', () => {
    open([template('t1', 'Board rules'), template('t2', 'Practice rules')]);

    const titles = Array.from(root.querySelectorAll('.template__body')).length;
    expect(titles).toBe(2);
    expect(root.textContent).toContain('Board rules');
    expect(root.textContent).toContain('Practice rules');
  });

  it('says so, and offers a new template, when there are none yet', () => {
    open([]);

    expect(root.querySelector('.empty-state')?.textContent).toContain('No templates yet');
    expect(buttonWithText('New template')).toBeDefined();
  });

  it('shows the error and retries the read when the list cannot be loaded', () => {
    open('error');

    expect(root.querySelector('.error-message')?.textContent).toContain('Try later.');
    buttonWithText('Try again')?.click();
    httpMock.expectOne(isList).flush([template('t1', 'Board rules')]);
    fixture.detectChanges();
    expect(root.textContent).toContain('Board rules');
  });

  it('creates a template from the form and places it in the list', () => {
    open([]);
    buttonWithText('New template')?.click();
    fixture.detectChanges();
    type('#template-title', '  Board rules ');
    type('#template-body', ' Bring a pencil. ');

    root.querySelector('form')?.dispatchEvent(new Event('submit'));
    const create = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/v1/instruction-templates'));
    expect(create.request.body).toEqual({ title: 'Board rules', body: 'Bring a pencil.' });
    create.flush(template('t1', 'Board rules', 'Bring a pencil.'));
    fixture.detectChanges();

    expect(root.querySelector('.success-message')?.textContent).toContain('Template saved.');
    expect(root.textContent).toContain('Board rules');
  });

  it('deletes a template only after the deletion is confirmed', () => {
    open([template('t1', 'Board rules')]);
    buttonWithText('Delete')?.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Exams that already copied its text keep it.');
    httpMock.expectNone((request) => request.method === 'DELETE');

    buttonWithText('Yes, delete')?.click();
    httpMock.expectOne((request) => request.method === 'DELETE' && request.url.endsWith('/v1/instruction-templates/t1'))
      .flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(root.textContent).not.toContain('Board rules');
    expect(root.querySelector('.success-message')?.textContent).toContain('Template deleted.');
  });
});
