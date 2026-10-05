import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ApiActivityService } from './api-activity.service';

/**
 * A thin, slow-moving bar along the top of the page while the server is being waited on, and a short
 * explanation after a longer wait. It never blocks the page or takes focus, and holds still for people
 * who ask for reduced motion.
 */
@Component({
  selector: 'app-api-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './api-activity.html',
  styleUrl: './api-activity.css',
})
export class ApiActivity {
  protected readonly activity = inject(ApiActivityService);

  protected readonly slowMessage =
    'Still working on it. The server can take a few seconds to wake up, and there is nothing you need to do.';
}
