import { provideHttpClient } from '@angular/common/http';
import { HttpRequest } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { LeaderboardDto } from '../candidate.models';
import { Leaderboard } from './leaderboard';

describe('Leaderboard', () => {
  let httpMock: HttpTestingController;

  const board = (overrides: Partial<LeaderboardDto> = {}): LeaderboardDto => ({
    examId: 'e1',
    examName: 'Maths Final',
    board: 'overall',
    batchId: null,
    subject: null,
    entries: [
      { rank: 1, name: 'Ravi S.', score: 9, isYou: false },
      { rank: 2, name: 'Asha K.', score: 7, isYou: true },
      { rank: 3, name: null, score: 4, isYou: false },
    ],
    you: null,
    candidateCount: 3,
    batches: [],
    subjects: [],
    truncated: false,
    ...overrides,
  });

  const root = (fixture: ComponentFixture<Leaderboard>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<Leaderboard>) => root(fixture).textContent ?? '';
  const boardRequest = (): TestRequest =>
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams/e1/leaderboard') && r.method === 'GET');

  /** Starts the page and leaves its first request open, so each test answers it as the case needs. */
  async function start(): Promise<ComponentFixture<Leaderboard>> {
    await TestBed.configureTestingModule({
      imports: [Leaderboard],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ examId: 'e1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(Leaderboard);
    fixture.detectChanges();
    return fixture;
  }

  async function open(body: LeaderboardDto): Promise<ComponentFixture<Leaderboard>> {
    const fixture = await start();
    boardRequest().flush(body);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('shows a loading notice while the board is read', async () => {
    const fixture = await start();

    expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain('Loading');
    expect(root(fixture).querySelector('.board')).toBeNull();
    boardRequest().flush(board());
  });

  it('lists the places as cards, with the candidate’s own place marked and the names shortened', async () => {
    const fixture = await open(board());

    const rows = root(fixture).querySelectorAll('.board-row');
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('Ravi S.');
    expect(rows[1].classList.contains('board-row--you')).toBe(true);
    expect(rows[1].getAttribute('aria-current')).toBe('true');
    expect(rows[1].textContent).toContain('You');
    expect(rows[2].textContent).toContain('Candidate');
    expect(root(fixture).querySelector('table')).toBeNull();
  });

  it('shows the candidate’s rank and the number of candidates in the headline figures', async () => {
    const fixture = await open(board());

    const tiles = root(fixture).querySelectorAll('.stat-card');
    expect(tiles[0].textContent).toContain('2');
    expect(tiles[0].textContent).toContain('Your rank');
    expect(tiles[1].textContent).toContain('3');
  });

  it('lists the candidate’s own row below the places when it falls outside them, and says the list is shortened', async () => {
    const fixture = await open(
      board({
        entries: [{ rank: 1, name: 'Ravi S.', score: 9, isYou: false }],
        you: { rank: 40, name: 'Asha K.', score: 1, isYou: true },
        candidateCount: 120,
        truncated: true,
      }),
    );

    const rows = root(fixture).querySelectorAll('.board-row');
    expect(rows.length).toBe(2);
    expect(rows[1].classList.contains('board-row--you')).toBe(true);
    expect(textOf(fixture)).toContain('Showing the top 1 places');
  });

  it('says so when no one has a place on the board, instead of an empty list', async () => {
    const fixture = await open(board({ entries: [], candidateCount: 0 }));

    expect(textOf(fixture)).toContain('No one has a place on this board yet');
    expect(root(fixture).querySelector('.board')).toBeNull();
  });

  it('says the candidate has no place when they have no result on the board', async () => {
    const fixture = await open(board({ entries: [{ rank: 1, name: 'Ravi S.', score: 9, isYou: false }], candidateCount: 1 }));

    expect(textOf(fixture)).toContain('you have no place on it');
  });

  it('shows the leaderboard as held, not as an error, when the results are not released yet', async () => {
    const fixture = await start();
    boardRequest().flush({ title: 'results_not_released', detail: 'Held.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('.board-held')?.textContent).toContain('The leaderboard is not out yet');
    expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
  });

  it('shows the reason and a retry when the board cannot be read, and reads it again on retry', async () => {
    const fixture = await start();
    boardRequest().flush({ title: 'internal', detail: 'The board could not be built.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('The board could not be built.');
    const retry = [...root(fixture).querySelectorAll('button')].find((b) => b.textContent?.includes('Try again'));
    retry?.click();
    boardRequest().flush(board());
    fixture.detectChanges();

    expect(root(fixture).querySelectorAll('.board-row').length).toBe(3);
  });

  it('switches to the batch board, asking for it, and offers the batches the candidate is in', async () => {
    const fixture = await open(board());

    const tabs = [...root(fixture).querySelectorAll<HTMLButtonElement>('[role="tab"]')];
    tabs[1].click();
    httpMock
      .expectOne((r: HttpRequest<unknown>) => r.url.endsWith('/v1/me/exams/e1/leaderboard') && r.params.get('board') === 'batch')
      .flush(board({ board: 'batch', batchId: 'b1', batches: [{ id: 'b1', name: 'Morning' }, { id: 'b2', name: 'Evening' }] }));
    fixture.detectChanges();

    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    const select = root(fixture).querySelector<HTMLSelectElement>('#leaderboard-batch');
    expect(select?.value).toBe('b1');
    expect([...(select?.options ?? [])].map((o) => o.textContent?.trim())).toEqual(['Morning', 'Evening']);
  });

  it('asks for another batch when the candidate picks it', async () => {
    const fixture = await open(board({ board: 'batch', batchId: 'b1', batches: [{ id: 'b1', name: 'Morning' }, { id: 'b2', name: 'Evening' }] }));
    fixture.componentInstance['board'].set('batch');
    fixture.componentInstance['chooseBatch']('b2');

    httpMock
      .expectOne((r: HttpRequest<unknown>) => r.params.get('board') === 'batch' && r.params.get('batchId') === 'b2')
      .flush(board({ board: 'batch', batchId: 'b2', batches: [{ id: 'b1', name: 'Morning' }, { id: 'b2', name: 'Evening' }] }));
    fixture.detectChanges();

    expect(root(fixture).querySelector<HTMLSelectElement>('#leaderboard-batch')?.value).toBe('b2');
  });

  it('says so on the batch board when the candidate is in no batch', async () => {
    const fixture = await open(board({ board: 'batch', batchId: null, batches: [], entries: [], candidateCount: 0 }));

    expect(textOf(fixture)).toContain('not in a batch for this exam');
  });

  it('shows the subject board for the first subject, with the exam’s subjects to choose from', async () => {
    const fixture = await start();
    boardRequest().flush(board({ board: 'subject', subject: 'Maths', subjects: ['Maths', 'Physics'] }));
    fixture.detectChanges();

    expect(root(fixture).querySelector<HTMLSelectElement>('#leaderboard-subject')?.value).toBe('Maths');
    expect([...(root(fixture).querySelector<HTMLSelectElement>('#leaderboard-subject')?.options ?? [])].map((o) => o.textContent?.trim())).toEqual([
      'Maths',
      'Physics',
    ]);
  });

  it('reads in the language the candidate chose', async () => {
    const fixture = await open(board());

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('आपकी रैंक');
    expect(root(fixture).querySelectorAll('.board-row').length).toBe(3);
  });
});
