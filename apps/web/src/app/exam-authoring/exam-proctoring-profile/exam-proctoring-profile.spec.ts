import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProctoringDto, ProctoringProfileDto } from '../exam.models';
import { ExamProctoringProfile } from './exam-proctoring-profile';

describe('ExamProctoringProfile', () => {
  let fixture: ComponentFixture<ExamProctoringProfile>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let applied: string[];

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/proctoring-profiles');

  const CUSTOM: ProctoringDto = { profile: 'CUSTOM', profileName: 'Custom', notice: ['Your IP address is recorded.', 'No camera is used.'] };
  const BROWSER_LOCK_NOTICE = ['Your IP address is recorded.', 'Copying is turned off.', 'If you leave 5 times, the exam is submitted.', 'No camera is used.'];
  const PROFILES: ProctoringProfileDto[] = [
    { id: 'OFF', name: 'No proctoring', description: 'Nothing is watched.', available: true, unavailableReason: null, contentProtection: false, focusViolationLimit: 0, notice: CUSTOM.notice },
    { id: 'BROWSER_LOCK', name: 'Browser lock', description: 'Copy block and a limit.', available: true, unavailableReason: null, contentProtection: true, focusViolationLimit: 5, notice: BROWSER_LOCK_NOTICE },
    { id: 'FULL', name: 'Full proctoring', description: 'Camera and screen.', available: false, unavailableReason: 'Not built yet.', contentProtection: true, focusViolationLimit: 5, notice: BROWSER_LOCK_NOTICE },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamProctoringProfile],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function show(proctoring: ProctoringDto | undefined, inputs: { editable?: boolean; busy?: boolean } = {}) {
    fixture = TestBed.createComponent(ExamProctoringProfile);
    fixture.componentRef.setInput('proctoring', proctoring);
    if (inputs.editable !== undefined) fixture.componentRef.setInput('editable', inputs.editable);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    applied = [];
    fixture.componentInstance.applied.subscribe((id) => applied.push(id));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;
  const radio = (id: string) => root.querySelector(`input[type="radio"][value="${id}"]`) as HTMLInputElement;
  const openChooser = () => {
    button('Choose a profile')!.click();
    httpMock.expectOne(isList).flush(PROFILES);
    fixture.detectChanges();
  };

  it('shows the exam\'s profile and the notice written from its settings, without asking for the list', () => {
    show({ profile: 'BROWSER_LOCK', profileName: 'Browser lock', notice: BROWSER_LOCK_NOTICE });

    expect(text()).toContain('Browser lock');
    expect(text()).toContain('If you leave 5 times, the exam is submitted.');
    expect(text()).not.toContain('match no preset');
  });

  it('says a custom exam matches no preset', () => {
    show(CUSTOM);

    expect(text()).toContain('Custom');
    expect(text()).toContain('match no preset');
  });

  it('copes with an older API that does not send the proctoring', () => {
    show(undefined);

    expect(text()).toContain("This exam's notice is not available.");
  });

  it('fetches the profiles only when asked, lists them, and disables one that is not built yet, saying why', () => {
    show(CUSTOM);

    openChooser();

    expect(radio('OFF').disabled).toBe(false);
    expect(radio('BROWSER_LOCK').disabled).toBe(false);
    expect(radio('FULL').disabled).toBe(true);
    expect(text()).toContain('Not built yet.');
  });

  it('shows what candidates would be told under the ticked profile, before it is applied', () => {
    show(CUSTOM);
    openChooser();

    radio('BROWSER_LOCK').click();
    fixture.detectChanges();

    expect(text()).toContain('Copying is turned off.');
    expect(applied).toEqual([]);
  });

  it('applies the ticked profile, once', () => {
    show(CUSTOM);
    openChooser();
    radio('BROWSER_LOCK').click();
    fixture.detectChanges();

    button('Apply profile')!.click();
    fixture.detectChanges();

    expect(applied).toEqual(['BROWSER_LOCK']);
    expect(button('Choose a profile')).toBeDefined();
  });

  it('offers nothing to apply until a different profile is ticked, and never the one the exam already is', () => {
    show({ profile: 'BROWSER_LOCK', profileName: 'Browser lock', notice: BROWSER_LOCK_NOTICE });
    openChooser();

    expect(radio('BROWSER_LOCK').checked).toBe(true);
    expect(button('Apply profile')!.disabled).toBe(true);

    radio('OFF').click();
    fixture.detectChanges();
    expect(button('Apply profile')!.disabled).toBe(false);
  });

  it('does not fetch the profiles a second time', () => {
    show(CUSTOM);
    openChooser();
    button('Cancel')!.click();
    fixture.detectChanges();

    button('Choose a profile')!.click();
    fixture.detectChanges();

    httpMock.expectNone(isList);
    expect(radio('OFF')).not.toBeNull();
  });

  it('Cancel closes the chooser and goes back to the exam\'s own notice', () => {
    show(CUSTOM);
    openChooser();
    radio('BROWSER_LOCK').click();
    fixture.detectChanges();

    button('Cancel')!.click();
    fixture.detectChanges();

    expect(root.querySelector('input[type="radio"]')).toBeNull();
    expect(text()).not.toContain('Copying is turned off.');
    expect(applied).toEqual([]);
  });

  it('says why the profiles could not be loaded', () => {
    show(CUSTOM);

    button('Choose a profile')!.click();
    httpMock.expectOne(isList).flush({ title: 'forbidden', detail: 'No permission.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(text()).toContain('No permission.');
    expect(button('Apply profile')!.disabled).toBe(true);
  });

  it('cannot be changed while a request is running', () => {
    show(CUSTOM, { busy: true });

    expect(button('Choose a profile')!.disabled).toBe(true);
  });

  it('only shows the profile for an archived exam, offering no change', () => {
    show(CUSTOM, { editable: false });

    expect(button('Choose a profile')).toBeUndefined();
    expect(text()).toContain('Custom');
  });
});
