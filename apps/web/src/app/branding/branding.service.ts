import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { BrandingApiService } from './branding-api.service';
import { applyBrandPalette, paletteFor } from './branding-theme';
import { BrandingDto } from './branding.models';

/** Where the start-up read of the branding stands. */
export type BrandingState = 'loading' | 'ready' | 'failed';

/**
 * Holds the institute's branding for the whole app (FR-41). It is the one place a branding takes effect: the start-up read and every
 * save both go through {@link apply}, so the header and the colours can never disagree.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly api = inject(BrandingApiService);
  private readonly root = inject(DOCUMENT).documentElement;

  /** Whether the start-up read has finished. A failed read leaves the platform's default look in place: the exam pages must still open. */
  readonly state = signal<BrandingState>('loading');

  /** The branding in effect, or null while it is loading or when the read failed. */
  readonly settings = signal<BrandingDto | null>(null);

  /** The institute's name for the header, or null to show the platform's own name. */
  readonly instituteName = computed(() => this.settings()?.instituteName ?? null);

  /** The address of the logo for the header, or null when there is none. */
  readonly logoUrl = computed(() => {
    const settings = this.settings();
    return settings === null ? null : this.api.logoUrlFor(settings);
  });

  /** Reads the branding when the app starts. It does not hold up the first screen: until the answer comes the default look is shown. */
  load(): void {
    this.state.set('loading');
    this.api.get().subscribe({
      next: (settings) => {
        this.apply(settings);
        this.state.set('ready');
      },
      error: () => this.state.set('failed'),
    });
  }

  /** Makes a branding take effect: records it and writes its colours onto the page. */
  apply(settings: BrandingDto): void {
    this.settings.set(settings);
    applyBrandPalette(this.root, paletteFor(settings.primaryColour));
  }
}
