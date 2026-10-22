import { WritableSignal, effect, inject, untracked } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

export interface QueryParamOptions<T extends string> {
  /** The values the address may carry. Anything else in the address is ignored, so a mistyped or old link cannot put the list in a state it has no words for. */
  readonly allowed?: readonly T[];
  /** The value a fresh list starts with; it is left out of the address, so an untouched list has a clean one. Empty text by default. */
  readonly default?: T;
}

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

  const initial = route.snapshot.queryParamMap.get(name);
  if (initial !== null && (options.allowed === undefined || options.allowed.includes(initial as T))) {
    state.set(initial as T);
  }

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
        void router.navigate([], { relativeTo: route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
      });
    });
  });
}
