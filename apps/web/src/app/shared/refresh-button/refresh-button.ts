import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { Icon } from '../icon/icon';
import { Tooltip } from '../tooltip/tooltip';

/**
 * The refresh icon at the end of an admin page's header: one icon, one place, one tooltip on every page, so staff learn it once. The page
 * reads its data again when it emits `refresh`, and passes `busy` while that read is on its way, which spins the icon.
 *
 * It does not use `disabled` while busy: a disabled button drops the focus a keyboard user had on it. It says `aria-busy` instead and
 * ignores a second click, so a double click cannot start two reads. The page-wide loading bar already announces that the server is being
 * waited on, so no extra words are added for a screen reader.
 */
@Component({
  selector: 'app-refresh-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, Tooltip, TranslatePipe],
  styleUrl: './refresh-button.css',
  template: `
    <button
      type="button"
      class="icon-btn refresh-button"
      [class.refresh-button--busy]="busy()"
      [appTooltip]="'common.refresh' | t"
      [attr.aria-label]="'common.refresh' | t"
      [attr.aria-busy]="busy() ? 'true' : null"
      (click)="onClick()"
    >
      <app-icon name="refresh" />
    </button>
  `,
})
export class RefreshButton {
  /** Whether the page is reading its data again right now. */
  readonly busy = input(false);

  /** The icon was pressed while no read was on its way. */
  readonly refresh = output<void>();

  protected onClick(): void {
    if (!this.busy()) {
      this.refresh.emit();
    }
  }
}
