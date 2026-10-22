import { DestroyRef, WritableSignal, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

export interface QueryParamOptions<T extends string> {
  /** The values the address may carry. Anything else in the address is ignored, so a mistyped or old link cannot put the list in a state it has no words for. */
  readonly allowed?: readonly T[];
  /** The value a fresh list starts with; it is left out of the address, so an untouched list has a clean one. Empty text by default. */
  readonly default?: T;
}

/** What a list's own write to the address carries, so the page can tell it from a link followed by the person. */
const OWN_WRITE = 'listShellWrite';

/** The changes waiting to be written to the address, per router: several filters set in one go make one navigation, not one each. */
const pending = new WeakMap<Router, Record<string, string | null>>();

/**
 * Keeps one search or filter of a list in the address bar (`?q=asha&status=Published`), so a filtered list can be bookmarked, sent to a
 * colleague, and survives a reload. Call it in the constructor, before the list is first read: the signal takes the value in the address
 * (when there is one), and from then on the address follows the signal. The address is replaced, not added to, so the back button leaves
 * the page and does not step through keystrokes. Other parameters in the address are left as they are.
 *
 * The writes of one turn are gathered into a single navigation: a navigation that starts while another is on its way takes the other's
 * place, and would take its parameters with it.
 */
export function syncQueryParam<T extends string>(name: string, state: WritableSignal<T>, options: QueryParamOptions<T> = {}): void {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  const fallback = options.default ?? ('' as T);
  const accepts = (value: string): value is T => options.allowed === undefined || options.allowed.includes(value as T);

  const initial = route.snapshot.queryParamMap.get(name);
  if (initial !== null && accepts(initial)) {
    state.set(initial);
  }

  // The address changed under the page (a link to the page without its filters, a pasted address): the list follows it.
  route.queryParamMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
    // Not the echo of this list's own write: by the time it arrives the person may have typed on, and it would put back an older text.
    if (router.getCurrentNavigation()?.extras.state?.[OWN_WRITE] === true) {
      return;
    }
    const value = params.get(name) ?? fallback;
    if (value !== untracked(state) && (value === fallback || accepts(value))) {
      state.set(value as T);
    }
  });

  effect(() => {
    const value = state();
    untracked(() => {
      const waiting = pending.get(router);
      if (waiting !== undefined) {
        waiting[name] = value === '' || value === fallback ? null : value;
        return;
      }

      const queryParams: Record<string, string | null> = { [name]: value === '' || value === fallback ? null : value };
      pending.set(router, queryParams);
      queueMicrotask(() => {
        pending.delete(router);
        // A link the person has just followed is on its way: it must not be taken over by the list writing its filters.
        const under = router.getCurrentNavigation();
        if (under !== null && under.extras.state?.[OWN_WRITE] !== true) {
          return;
        }
        void router.navigate([], { relativeTo: route, queryParams, queryParamsHandling: 'merge', replaceUrl: true, state: { [OWN_WRITE]: true } });
      });
    });
  });
}
