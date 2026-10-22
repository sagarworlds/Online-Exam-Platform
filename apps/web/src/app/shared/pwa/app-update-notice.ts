import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AppUpdateService } from './app-update.service';

/** Whether this address is a candidate's exam in progress (`/attempt/:id`), as opposed to its review or result, which hang below it. */
export function isAttemptInProgress(url: string): boolean {
  return /^\/attempt\/[^/?#]+\/?(?:[?#].*)?$/.test(url);
}

/**
 * Says that a newer version of the app is out, with a button that moves the tab onto it. It shows for every new version, stays until the
 * tab is updated, and never takes focus or covers the page. It is never shown during an exam: a reload there is the last thing a candidate
 * needs, so it waits and appears once they leave the exam page.
 */
@Component({
  selector: 'app-update-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './app-update-notice.html',
  styleUrl: './app-update-notice.css',
})
export class AppUpdateNotice {
  protected readonly updates = inject(AppUpdateService);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly visible = computed(() => this.updates.updateAvailable() && !isAttemptInProgress(this.url()));

  protected update(): void {
    void this.updates.apply();
  }
}
