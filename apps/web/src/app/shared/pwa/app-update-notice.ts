import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ExamMode } from '../exam-mode/exam-mode';
import { AppUpdateService } from './app-update.service';

/**
 * Says that a newer version of the app is out, with a button that moves the tab onto it. It shows for every new version, stays until the
 * tab is updated, and never takes focus or covers the page. It is never shown during an exam (ExamMode): a reload there is the last thing
 * a candidate needs, so it waits and appears once they leave the exam page.
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
  private readonly examMode = inject(ExamMode);

  protected readonly visible = computed(() => this.updates.updateAvailable() && !this.examMode.active());

  protected update(): void {
    void this.updates.apply();
  }
}
