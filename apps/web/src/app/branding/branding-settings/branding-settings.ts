import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { BrandingApiService } from '../branding-api.service';
import { BrandingService } from '../branding.service';
import { DEFAULT_PRIMARY_COLOUR } from '../branding-theme';
import { BrandingDto, LOGO_MEDIA_TYPES, MAX_INSTITUTE_NAME_LENGTH, isHexColour, logoProblem } from '../branding.models';

/**
 * Admin page: the institute's name, primary colour and logo for the candidate pages (FR-41). Saving applies the change at once, through
 * the branding service, so the header and the colours update without a reload. Nothing is chosen for the institute: a blank field keeps
 * the platform's own look, and the picker's starting colour is shown but not saved.
 */
@Component({
  selector: 'app-branding-settings',
  imports: [TranslatePipe],
  templateUrl: './branding-settings.html',
  styleUrl: './branding-settings.css',
})
export class BrandingSettings {
  private readonly api = inject(BrandingApiService);
  private readonly branding = inject(BrandingService);
  private readonly i18n = inject(I18nService);
  private readonly logoInput = viewChild<ElementRef<HTMLInputElement>>('logoInput');

  protected readonly maxNameLength = MAX_INSTITUTE_NAME_LENGTH;
  protected readonly logoAccept = LOGO_MEDIA_TYPES.join(',');

  /** The settings the API holds; null until the first read succeeds. */
  protected readonly current = signal<BrandingDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  /** What the form shows, as typed. It starts from the saved settings. */
  protected readonly name = signal('');
  protected readonly colour = signal('');
  protected readonly saving = signal(false);
  protected readonly saveMessage = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);

  /** The picker shows the typed colour when it is valid, and the platform's own colour otherwise. The picker's colour is never saved by itself. */
  protected readonly pickerValue = computed(() =>
    (isHexColour(this.colour()) ? this.colour().trim() : DEFAULT_PRIMARY_COLOUR).toLowerCase(),
  );
  protected readonly colourInvalid = computed(() => this.colour().trim() !== '' && !isHexColour(this.colour()));

  /** A logo chosen but not yet uploaded, and a preview of it. */
  protected readonly chosenLogo = signal<File | null>(null);
  protected readonly chosenPreviewUrl = signal<string | null>(null);
  protected readonly logoError = signal<string | null>(null);
  protected readonly logoBusy = signal(false);
  protected readonly logoMessage = signal<string | null>(null);

  /** The logo the institute has now, for its preview; null when there is none. */
  protected readonly currentLogoUrl = computed(() => {
    const settings = this.current();
    return settings === null ? null : this.api.logoUrlFor(settings);
  });

  /** True when the institute has set nothing at all, so the candidate pages show the default look. */
  protected readonly hasNothingSet = computed(() => {
    const settings = this.current();
    return settings !== null && settings.instituteName === null && settings.primaryColour === null && !settings.hasLogo;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revokePreview());
    this.load();
  }

  /** Reads the settings the form starts from. */
  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.get().subscribe({
      next: (settings) => {
        this.show(settings);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(extractErrorMessage(error, this.i18n.t('branding.loadFailed')));
      },
    });
  }

  /** Saves the name and colour. A blank field clears it back to the platform's default. */
  protected save(): void {
    if (this.saving() || this.colourInvalid()) {
      return;
    }

    this.saving.set(true);
    this.saveMessage.set(null);
    this.saveError.set(null);
    this.api.save({ instituteName: this.name().trim() || null, primaryColour: this.colour().trim() || null }).subscribe({
      next: (settings) => {
        this.saving.set(false);
        // The form takes the saved values back, so what is shown is exactly what the API now holds, trimmed and in capitals.
        this.show(settings);
        this.branding.apply(settings);
        this.saveMessage.set(this.i18n.t('branding.saved'));
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Checks a chosen file against the logo rules and keeps it for upload. A file that breaks a rule is refused with the reason. */
  protected chooseLogo(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.item(0) ?? null;
    this.logoMessage.set(null);
    this.logoError.set(null);
    this.forgetChoice();
    if (file === null) {
      return;
    }

    const problem = logoProblem(file);
    if (problem !== null) {
      this.logoError.set(this.i18n.t(problem === 'type' ? 'branding.logoTypeProblem' : 'branding.logoSizeProblem'));
      return;
    }

    this.chosenLogo.set(file);
    this.chosenPreviewUrl.set(URL.createObjectURL(file));
  }

  /** Uploads the chosen logo. The API checks the file's own bytes and has the last word. */
  protected uploadLogo(): void {
    const file = this.chosenLogo();
    if (file === null || this.logoBusy()) {
      return;
    }

    this.logoBusy.set(true);
    this.logoMessage.set(null);
    this.logoError.set(null);
    this.api.uploadLogo(file).subscribe({
      next: (settings) => {
        this.logoBusy.set(false);
        this.clearChoice();
        this.rememberLogo(settings);
        this.logoMessage.set(this.i18n.t('branding.logoSaved'));
      },
      error: (error: unknown) => {
        this.logoBusy.set(false);
        this.logoError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Removes the logo the institute has. */
  protected removeLogo(): void {
    if (this.logoBusy()) {
      return;
    }

    this.logoBusy.set(true);
    this.logoMessage.set(null);
    this.logoError.set(null);
    this.api.removeLogo().subscribe({
      next: (settings) => {
        this.logoBusy.set(false);
        this.rememberLogo(settings);
        this.logoMessage.set(this.i18n.t('branding.logoRemoved'));
      },
      error: (error: unknown) => {
        this.logoBusy.set(false);
        this.logoError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Takes saved settings into the form and the page's state. */
  private show(settings: BrandingDto): void {
    this.current.set(settings);
    this.name.set(settings.instituteName ?? '');
    this.colour.set(settings.primaryColour ?? '');
  }

  /**
   * Takes a logo change into the header and the page's state, but not into the form: a name or colour typed and not yet saved must not be
   * replaced just because the logo changed.
   */
  private rememberLogo(settings: BrandingDto): void {
    this.current.set(settings);
    this.branding.apply(settings);
  }

  /** Drops the chosen file and its preview, but leaves the file input showing what the person picked. */
  private forgetChoice(): void {
    this.revokePreview();
    this.chosenPreviewUrl.set(null);
    this.chosenLogo.set(null);
  }

  /** Drops the chosen file after it was uploaded, and empties the file input so the page no longer shows it as pending. */
  private clearChoice(): void {
    this.forgetChoice();
    const input = this.logoInput();
    if (input !== undefined) {
      input.nativeElement.value = '';
    }
  }

  private revokePreview(): void {
    const url = this.chosenPreviewUrl();
    if (url !== null) {
      URL.revokeObjectURL(url);
    }
  }
}
