import { TestBed } from '@angular/core/testing';
import { Route } from '@angular/router';
import { Observable, of } from 'rxjs';
import { vi } from 'vitest';
import { OnDemandPreload } from './on-demand-preload';

describe('OnDemandPreload', () => {
  let strategy: OnDemandPreload;

  const exam: Route = { path: 'attempt/:attemptId', data: { preloadOn: 'exam' } };
  const plain: Route = { path: 'my-exams' };
  const loader = () => vi.fn<() => Observable<unknown>>(() => of('loaded'));

  beforeEach(() => {
    strategy = TestBed.inject(OnDemandPreload);
  });

  it('loads nothing for a route that names no key, and holds nothing up', () => {
    const load = loader();
    let done = false;

    strategy.preload(plain, load).subscribe({ complete: () => (done = true) });
    strategy.request('exam');

    expect(load).not.toHaveBeenCalled();
    expect(done).toBe(true);
  });

  it('waits for the request: offering the route loads nothing and does not hold the router’s preloader open', () => {
    const load = loader();
    let done = false;

    strategy.preload(exam, load).subscribe({ complete: () => (done = true) });

    expect(load).not.toHaveBeenCalled();
    expect(done).toBe(true);
  });

  it('loads the route when it is asked for', () => {
    const load = loader();
    strategy.preload(exam, load).subscribe();

    strategy.request('exam');

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('loads it once, however many times it is asked for or offered', () => {
    const load = loader();
    strategy.preload(exam, load).subscribe();
    strategy.preload(exam, load).subscribe();

    strategy.request('exam');
    strategy.request('exam');

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('loads a route offered after it was asked for', () => {
    const load = loader();
    strategy.request('exam');

    strategy.preload(exam, load).subscribe();

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('does not mind a load that fails: the navigation that needs the route asks for it again', () => {
    const load = vi.fn<() => Observable<unknown>>(
      () => new Observable<unknown>((subscriber) => subscriber.error(new Error('chunk could not be fetched'))),
    );
    strategy.preload(exam, load).subscribe();

    expect(() => strategy.request('exam')).not.toThrow();
    expect(load).toHaveBeenCalledTimes(1);
  });
});
