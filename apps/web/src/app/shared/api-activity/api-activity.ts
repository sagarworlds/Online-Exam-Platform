import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ApiActivityService } from './api-activity.service';

/**
 * A thin, slow-moving bar along the top of the page while the server is being waited on, and a short
 * explanation after a longer wait. It never blocks the page or takes focus, and holds still for people
 * who ask for reduced motion.
 */
@Component({
  selector: 'app-api-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './api-activity.html',
  styleUrl: './api-activity.css',
})
export class ApiActivity {
  protected readonly activity = inject(ApiActivityService);

  private readonly i18n = inject(I18nService);

  /** What is said after a long wait, in the language in use. */
  protected slowMessage(): string {
    return this.i18n.t('activity.slow');
  }
}
