import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotificationDto, NotificationPage } from '../notifications.models';
import { NotificationFeed } from './notification-feed';

const notice = (id: string, overrides: Partial<NotificationDto> = {}): NotificationDto => ({
  id,
  kind: 'ResultReleased',
  subjectId: `attempt-${id}`,
  examName: 'Maths Final',
  createdAtUtc: '2026-10-09T10:00:00Z',
  readAtUtc: null,
  isRead: false,
  ...overrides,
});

const page = (items: NotificationDto[], totalCount = items.length, pageNumber = 1): NotificationPage => ({
  items,
  page: pageNumber,
  pageSize: 20,
  totalCount,
  unreadCount: items.filter((item) => !item.isRead).length,
});

describe('NotificationFeed', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<NotificationFeed>;
  let root: HTMLElement;

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/me/notifications');
  const isCount = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/unread-count');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationFeed],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  /** Opens the feed and answers its first page with the given response, or with a failure when given 'error'. */
  function open(response: NotificationPage | 'error') {
    fixture = TestBed.createComponent(NotificationFeed);
    fixture.detectChanges();
    const list = httpMock.expectOne(isList);
    expect(list.request.params.get('page')).toBe('1');
    if (response === 'error') {
      list.flush({ title: 'server_error', detail: 'The feed is unavailable.' }, { status: 500, statusText: 'Server Error' });
    } else {
      list.flush(response);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  function buttonWithText(text: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes(text));
  }

  it('lists the notices with the unread ones first, and shows the unread count as the headline', () => {
    open(page([notice('a'), notice('b', { isRead: true, readAtUtc: '2026-10-09T11:00:00Z' })]));

    const rows = root.querySelectorAll('.feed__item');
    expect(rows.length).toBe(2);
    expect(rows[0].classList).toContain('feed__item--unread');
    expect(rows[1].classList).not.toContain('feed__item--unread');
    expect(root.querySelector('.feed__headline')?.textContent?.trim()).toBe('1');
    expect(root.textContent).toContain('Your result for Maths Final is out.');
  });

  it('shows the error with a retry, and the retry reads the first page again', () => {
    open('error');

    expect(root.querySelector('.error-message')?.textContent).toContain('The feed is unavailable.');

    buttonWithText('Try again')?.click();
    const retry = httpMock.expectOne(isList);
    retry.flush(page([notice('a')]));
    fixture.detectChanges();

    expect(root.querySelectorAll('.feed__item').length).toBe(1);
  });

  it('shows the empty state when there is no notice yet', () => {
    open(page([]));

    expect(root.querySelector('.empty-state')?.textContent).toContain('You have no notifications yet.');
    expect(root.querySelector('.feed')).toBeNull();
  });

  it('marks one notice read, then reads the header count again', () => {
    open(page([notice('a')]));

    buttonWithText('Mark as read')?.click();
    const mark = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/me/notifications/a/read'));
    mark.flush(notice('a', { isRead: true, readAtUtc: '2026-10-09T11:00:00Z' }));
    fixture.detectChanges();

    const count = httpMock.expectOne(isCount);
    count.flush({ unreadCount: 0 });
    fixture.detectChanges();

    expect(root.querySelector('.feed__item')?.classList).not.toContain('feed__item--unread');
    expect(buttonWithText('Mark as read')).toBeUndefined();
    expect(root.querySelector('.feed__headline')?.textContent?.trim()).toBe('0');
  });

  it('marks every notice read with one call, and announces it', () => {
    open(page([notice('a'), notice('b')]));

    root.querySelector<HTMLButtonElement>('.feed__mark-all')?.click();
    const markAll = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/me/notifications/read-all'));
    markAll.flush({ marked: 2 });
    fixture.detectChanges();

    expect(root.querySelectorAll('.feed__item--unread').length).toBe(0);
    expect(root.querySelector('.feed__headline')?.textContent?.trim()).toBe('0');
    expect(root.querySelector('[aria-live="polite"][role="status"]')?.textContent).toContain('Marked 2 as read.');
  });

  it('loads the next page when asked, and does not show a notice twice', () => {
    open(page([notice('a'), notice('b')], 3));

    buttonWithText('Load more')?.click();
    const next = httpMock.expectOne(isList);
    expect(next.request.params.get('page')).toBe('2');
    next.flush(page([notice('b'), notice('c')], 3, 2));
    fixture.detectChanges();

    expect(root.querySelectorAll('.feed__item').length).toBe(3);
    expect(buttonWithText('Load more')).toBeUndefined();
  });
});
