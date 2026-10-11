import { vi } from 'vitest';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { AttemptResultDto } from '../candidate.models';
import { AttemptResult } from './attempt-result';

describe('AttemptResult', () => {
  let httpMock: HttpTestingController;

  // Two sections: one with a right answer and a skipped one, one with a wrong answer, with negative marking so the marks differ.
  const result = (overrides: Partial<AttemptResultDto> = {}): AttemptResultDto => ({
    attemptId: 'a1',
    examId: 'e1',
    examName: 'Maths Final',
    number: 1,
    submittedAtUtc: '2026-10-05T04:50:00Z',
    autoSubmitted: false,
    score: 2.75,
    maxScore: 12,
    correctCount: 1,
    wrongCount: 1,
    partialCount: 0,
    unansweredCount: 1,
    sections: [
      { id: 's1', name: 'Algebra', score: 3.75, correctCount: 1, wrongCount: 0, partialCount: 0, unansweredCount: 1 },
      { id: 's2', name: 'Geometry', score: -1, correctCount: 0, wrongCount: 1, partialCount: 0, unansweredCount: 0 },
    ],
    rank: 2,
    percentile: 66.66,
    cohortSize: 4,
    provisional: false,
    resultVersion: 1,
    ...overrides,
  });

  const problem = (title: string, detail: string) => ({ title, detail });

  const root = (fixture: ComponentFixture<AttemptResult>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<AttemptResult>) => root(fixture).textContent ?? '';

  /** Creates the page and leaves its result request open, so each test answers it as the case needs. */
  async function start(): Promise<ComponentFixture<AttemptResult>> {
    await TestBed.configureTestingModule({
      imports: [AttemptResult],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ attemptId: 'a1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(AttemptResult);
    fixture.detectChanges();
    return fixture;
  }

  const resultRequest = (): TestRequest =>
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/result') && r.method === 'GET');
  const attemptRequest = (): TestRequest =>
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET');

  /** Starts the page and answers its one result request with the given body, or with a problem if an error is given. */
  async function open(body: AttemptResultDto | { error: object; status: number }) {
    const fixture = await start();
    const request = resultRequest();
    if ('error' in body) {
      request.flush(body.error, { status: body.status, statusText: 'Error' });
    } else {
      request.flush(body);
    }
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('shows a loading notice while the result is read', async () => {
    const fixture = await start();

    expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain('Loading');
    expect(root(fixture).querySelector('.stat-grid')).toBeNull();
    resultRequest().flush(result());
  });

  it('shows the score, the rank and the percentile as the headline figures', async () => {
    const fixture = await open(result());

    const tiles = root(fixture).querySelectorAll('.stat-card');
    expect(tiles.length).toBe(3);
    expect(tiles[0].textContent).toContain('2.75 / 12');
    expect(tiles[1].textContent).toContain('2');
    expect(tiles[1].textContent).toContain('Rank out of 4');
    expect(tiles[2].textContent).toContain('66.66%');
    expect(textOf(fixture)).toContain('Maths Final');
    expect(textOf(fixture)).toContain('the same as or higher than 66.66% of the 4 results');
  });

  it('shows the marks and the answers of each section, with the sign of the marks', async () => {
    const fixture = await open(result());

    const sections = root(fixture).querySelectorAll('.result-section');
    expect(sections.length).toBe(2);
    expect(sections[0].textContent).toContain('Algebra');
    expect(sections[0].textContent).toContain('+3.75');
    expect(sections[1].textContent).toContain('Geometry');
    expect(sections[1].textContent).toContain('-1');
  });

  it('says the rank may still move only while the exam is open', async () => {
    const fixture = await open(result({ provisional: true }));
    expect(textOf(fixture)).toContain('the rank can still change');
  });

  it('does not say the rank may move once the exam has closed', async () => {
    const fixture = await open(result({ provisional: false }));
    expect(textOf(fixture)).not.toContain('the rank can still change');
  });

  it('shows an empty notice, not an empty list, when no question was scored', async () => {
    const fixture = await open(result({ sections: [] }));

    expect(textOf(fixture)).toContain('No questions were scored');
    expect(root(fixture).querySelector('.result-sections')).toBeNull();
  });

  it('shows the result as held, with the date it opens, when the API refuses it before release', async () => {
    const fixture = await start();
    resultRequest().flush(problem('results_not_released', 'The correct answers have not been released yet.'), { status: 409, statusText: 'Conflict' });
    attemptRequest().flush({ id: 'a1', review: { available: false, mode: 'Scheduled', availableFromUtc: '2026-10-12T09:00:00Z' } });
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('Your result is not out yet');
    expect(textOf(fixture)).toContain('Your result opens on');
    expect(root(fixture).querySelector('.stat-grid')).toBeNull();
    expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
  });

  it('says the result is held until an organiser releases it, when no date is decided', async () => {
    const fixture = await start();
    resultRequest().flush(problem('results_not_released', 'Held.'), { status: 409, statusText: 'Conflict' });
    attemptRequest().flush({ id: 'a1', review: { available: false, mode: 'Manual', availableFromUtc: null } });
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('will be shown once the exam organiser releases it');
  });

  it('still says the result is held when the date cannot be read, and logs why', async () => {
    const logged = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const fixture = await start();
    resultRequest().flush(problem('results_not_released', 'Held.'), { status: 409, statusText: 'Conflict' });
    attemptRequest().flush(problem('boom', 'no'), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('will be shown once the exam organiser releases it');
    expect(logged).toHaveBeenCalled();
  });

  it('shows the reason and a retry when the result cannot be read, and reads it again on retry', async () => {
    const fixture = await start();
    resultRequest().flush(problem('internal', 'The server had a problem.'), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('The server had a problem.');
    const retry = [...root(fixture).querySelectorAll('button')].find((b) => b.textContent?.includes('Try again'));
    expect(retry).toBeDefined();

    retry!.click();
    fixture.detectChanges();
    resultRequest().flush(result());
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('2.75 / 12');
    expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
  });

  it('reads in the language the candidate chose', async () => {
    const fixture = await open(result());

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('खंड के अनुसार');
    expect(textOf(fixture)).toContain('4 में से रैंक');
    expect(textOf(fixture)).toContain('पर्सेंटाइल');
  });

  describe('the certificate (FR-34)', () => {
    const certificateButton = (fixture: ComponentFixture<AttemptResult>) =>
      [...root(fixture).querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Download certificate'));

    /** A problem document as the API sends it when it refuses a certificate: a file whose text is the problem's JSON. */
    function refusalBlob(title: string): Blob {
      const json = JSON.stringify({ title, status: 409, detail: 'Refused.' });
      const blob = new Blob([json], { type: 'application/problem+json' });
      // jsdom's Blob does not read its own text; the browser does, so the test supplies the same text the API wrote.
      Object.defineProperty(blob, 'text', { value: () => Promise.resolve(json) });
      return blob;
    }

    it('offers the download once the result is out, and saves the PDF the API returns', async () => {
      const createUrl = vi.fn(() => 'blob:certificate');
      const revoke = vi.fn();
      const [originalCreate, originalRevoke] = [URL.createObjectURL, URL.revokeObjectURL];
      URL.createObjectURL = createUrl;
      URL.revokeObjectURL = revoke;
      const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      const fixture = await open(result());

      const button = certificateButton(fixture);
      expect(button?.disabled).toBe(false);
      button?.click();
      const request = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/certificate') && r.method === 'GET');
      expect(request.request.responseType).toBe('blob');
      request.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));
      fixture.detectChanges();

      expect(createUrl).toHaveBeenCalled();
      expect(click).toHaveBeenCalled();
      expect(revoke).toHaveBeenCalledWith('blob:certificate');
      click.mockRestore();
      URL.createObjectURL = originalCreate;
      URL.revokeObjectURL = originalRevoke;
    });

    it('is disabled, with the reason, while the result is held', async () => {
      const fixture = await start();
      resultRequest().flush({ title: 'results_not_released', detail: 'Held.' }, { status: 409, statusText: 'Conflict' });
      attemptRequest().flush({ id: 'a1', review: { available: false, mode: 'Manual', availableFromUtc: null } });
      fixture.detectChanges();

      const button = certificateButton(fixture);
      expect(button?.disabled).toBe(true);
      expect(root(fixture).querySelector(`#${button?.getAttribute('aria-describedby')}`)?.textContent).toContain('once your result is released');
    });

    it('is disabled, with the reason, when the result was invalidated, which has no retry', async () => {
      const fixture = await open({ status: 409, error: { title: 'attempt_invalidated', detail: 'Invalidated.' } });

      expect(textOf(fixture)).toContain('This result was invalidated');
      expect(certificateButton(fixture)?.disabled).toBe(true);
      expect([...root(fixture).querySelectorAll('button')].some((b) => b.textContent?.includes('Try again'))).toBe(false);
    });

    it('explains a refused download with the API’s reason, when the name is missing from the profile', async () => {
      const fixture = await open(result());
      certificateButton(fixture)?.click();
      httpMock
        .expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/certificate'))
        .flush(refusalBlob('certificate_name_missing'), { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('Add your name to your profile');
    });

    it('explains a refused download when the name uses characters the certificate cannot show', async () => {
      const fixture = await open(result());
      certificateButton(fixture)?.click();
      httpMock
        .expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/certificate'))
        .flush(refusalBlob('certificate_text_unsupported'), { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('Latin letters');
    });
  });
});
