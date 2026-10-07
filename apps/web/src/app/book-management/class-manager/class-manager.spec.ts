import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClassDto } from '../class.models';
import { ClassManager } from './class-manager';

const url = (r: { url: string }, suffix = '') => r.url.endsWith(`/v1/classes${suffix}`);

const schoolClass = (id: string, name: string, overrides: Partial<ClassDto> = {}): ClassDto => ({
  id, name, isArchived: false, bookCount: 0, createdAtUtc: '2026-10-02T00:00:00Z', ...overrides,
});

const CLASSES = [schoolClass('k10', '10th', { bookCount: 3 }), schoolClass('k2', '2nd', { bookCount: 1 }), schoolClass('k5', '5th', { isArchived: true, bookCount: 2 })];

describe('ClassManager', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<ClassManager>;
  let root: HTMLElement;
  let changed: number;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ClassManager],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(classes: ClassDto[] = CLASSES) {
    fixture = TestBed.createComponent(ClassManager);
    fixture.componentRef.setInput('classes', classes);
    changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const rowOf = (name: string) => Array.from(root.querySelectorAll('.class-list__item')).find((li) => li.querySelector('strong')?.textContent === name) as HTMLElement;
  const button = (label: string, within: Element = root) =>
    Array.from(within.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const type = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const submit = (label: string) => (root.querySelector(`form[aria-label="${label}"]`) as HTMLFormElement).dispatchEvent(new Event('submit'));
  const addInput = () => root.querySelector('#class-name') as HTMLInputElement;

  it('lists the classes in natural order, with how many books each holds and an Archived badge', () => {
    open();

    const names = Array.from(root.querySelectorAll('.class-list__item strong')).map((s) => s.textContent);
    expect(names).toEqual(['2nd', '5th', '10th']);
    expect(rowOf('2nd').textContent).toContain('1 book');
    expect(rowOf('2nd').textContent).not.toContain('1 books');
    expect(rowOf('10th').textContent).toContain('3 books');
    expect(rowOf('5th').textContent).toContain('Archived');
    expect(rowOf('2nd').textContent).not.toContain('Archived');
  });

  it('says so when there are no classes, and still offers the form to add one', () => {
    open([]);

    expect(root.textContent).toContain('No classes yet');
    expect(root.querySelector('.class-list')).toBeNull();
    expect(root.querySelector('form[aria-label="New class"]')).not.toBeNull();
  });

  it('adds a class with the name trimmed, tells the page, and clears the field', () => {
    open();
    type(addInput(), '  4th  ');

    submit('New class');
    const post = httpMock.expectOne((r) => r.method === 'POST' && url(r));
    expect(post.request.body).toEqual({ name: '4th' });
    post.flush(schoolClass('k4', '4th'), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(changed).toBe(1);
    expect(addInput().value).toBe('');
  });

  it('keeps Add class off until the name has a letter in it, and limits the length', () => {
    open();
    const add = () => button('Add class');
    expect(add().disabled).toBe(true);
    expect(addInput().getAttribute('maxlength')).toBe('100');

    type(addInput(), '   ');
    expect(add().disabled).toBe(true);
    type(addInput(), 'x'.repeat(101));
    expect(add().disabled).toBe(true);
    type(addInput(), '4th');
    expect(add().disabled).toBe(false);
  });

  it('shows the API message when a class is refused as a repeat, keeping what was typed and telling nobody', () => {
    open();
    type(addInput(), '2ND');

    submit('New class');
    httpMock
      .expectOne((r) => r.method === 'POST' && url(r))
      .flush({ title: 'duplicate_class', detail: 'There is already a class called "2nd".' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('There is already a class called "2nd".');
    expect(addInput().value).toBe('2ND');
    expect(changed).toBe(0);
  });

  it('shows the API message when a class name is refused as invalid', () => {
    open();
    type(addInput(), 'Nursery');

    submit('New class');
    httpMock
      .expectOne((r) => r.method === 'POST' && url(r))
      .flush({ title: 'invalid_class', detail: 'A class name is at most 100 characters.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.textContent).toContain('A class name is at most 100 characters.');
  });

  it('clears an earlier error when the next change goes through', () => {
    open();
    type(addInput(), 'x');
    submit('New class');
    httpMock.expectOne((r) => r.method === 'POST').flush({ title: 'duplicate_class', detail: 'Nope.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).not.toBeNull();

    submit('New class');
    httpMock.expectOne((r) => r.method === 'POST').flush(schoolClass('k9', 'x'), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('renames a class in place, starting from its current name, and tells the page', () => {
    open();

    button('Rename', rowOf('2nd')).click();
    fixture.detectChanges();
    const input = root.querySelector('form[aria-label="Rename class"] input') as HTMLInputElement;
    expect(input.value).toBe('2nd');
    expect(input.getAttribute('aria-label')).toBe('New name for 2nd');
    type(input, ' Second ');
    submit('Rename class');

    const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, '/k2'));
    expect(put.request.body).toEqual({ name: 'Second' });
    put.flush(schoolClass('k2', 'Second'));
    fixture.detectChanges();

    expect(root.querySelector('form[aria-label="Rename class"]')).toBeNull();
    expect(changed).toBe(1);
  });

  it('can back out of a rename, or leave the name as it was, without sending anything', () => {
    open();

    button('Rename', rowOf('2nd')).click();
    fixture.detectChanges();
    button('Cancel').click();
    fixture.detectChanges();
    expect(root.querySelector('form[aria-label="Rename class"]')).toBeNull();

    button('Rename', rowOf('2nd')).click();
    fixture.detectChanges();
    submit('Rename class');
    fixture.detectChanges();

    expect(root.querySelector('form[aria-label="Rename class"]')).toBeNull();
    expect(changed).toBe(0);
  });

  it('shows why a rename was refused and leaves the field open to correct', () => {
    open();
    button('Rename', rowOf('2nd')).click();
    fixture.detectChanges();
    type(root.querySelector('form[aria-label="Rename class"] input') as HTMLInputElement, '10th');

    submit('Rename class');
    httpMock
      .expectOne((r) => r.method === 'PUT' && url(r, '/k2'))
      .flush({ title: 'duplicate_class', detail: 'There is already a class called "10th".' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.textContent).toContain('There is already a class called "10th".');
    expect(root.querySelector('form[aria-label="Rename class"]')).not.toBeNull();
    expect(changed).toBe(0);
  });

  it('archives an open class and restores an archived one, telling the page each time', () => {
    open();

    button('Archive', rowOf('2nd')).click();
    httpMock.expectOne((r) => r.method === 'POST' && url(r, '/k2/archive')).flush(schoolClass('k2', '2nd', { isArchived: true }));
    expect(changed).toBe(1);

    expect(button('Restore', rowOf('5th'))).toBeDefined();
    expect(button('Archive', rowOf('5th'))).toBeUndefined();
    button('Restore', rowOf('5th')).click();
    httpMock.expectOne((r) => r.method === 'POST' && url(r, '/k5/restore')).flush(schoolClass('k5', '5th'));
    expect(changed).toBe(2);
  });

  it('shows why an archive was refused and does not tell the page', () => {
    open();

    button('Archive', rowOf('10th')).click();
    httpMock
      .expectOne((r) => r.method === 'POST' && url(r, '/k10/archive'))
      .flush({ title: 'class_not_found', detail: 'No class matches the given id.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No class matches the given id.');
    expect(changed).toBe(0);
  });

  it('locks its buttons while a request is running', () => {
    open();

    button('Archive', rowOf('2nd')).click();
    fixture.detectChanges();

    expect(button('Archive', rowOf('10th')).disabled).toBe(true);
    expect(button('Rename', rowOf('10th')).disabled).toBe(true);
    httpMock.expectOne((r) => r.method === 'POST').flush(schoolClass('k2', '2nd', { isArchived: true }));
    fixture.detectChanges();
    expect(button('Archive', rowOf('10th')).disabled).toBe(false);
  });
});
