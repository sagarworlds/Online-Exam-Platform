import { Component, computed, inject, input, output, signal } from '@angular/core';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ProctoringDto, ProctoringProfileDto } from '../exam.models';

/**
 * The exam's proctoring as a preset (FR-46): which profile its settings amount to, and, always in view, the notice candidates will be
 * shown, which the server writes from those settings. Choosing a profile applies both settings together; the two cards below stay for
 * fine-tuning, in which case the exam reads as custom here. The list of profiles is fetched only when the author asks to change, so a
 * page that only shows the exam costs nothing extra. A profile that promises something not built yet is listed, disabled, with why.
 */
@Component({
  selector: 'app-exam-proctoring-profile',
  templateUrl: './exam-proctoring-profile.html',
})
export class ExamProctoringProfile {
  private readonly api = inject(ExamApiService);

  /** The exam's proctoring as it stands: its profile and the notice written from its settings. Absent from an older API. */
  readonly proctoring = input<ProctoringDto | undefined>(undefined);
  /** Whether the profile can be changed; false only for an archived exam. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);

  /** The author chose a profile to apply; the page sends it. */
  readonly applied = output<string>();

  protected readonly choosing = signal(false);
  protected readonly profiles = signal<ProctoringProfileDto[] | null>(null);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);

  /** The profile the author has ticked, if it is one they may apply. */
  protected readonly selected = computed(() => this.profiles()?.find((p) => p.id === this.selectedId() && p.available) ?? null);

  /** What candidates would be told: under the ticked profile while choosing, otherwise under the exam's own settings. */
  protected readonly notice = computed(() => (this.choosing() ? (this.selected()?.notice ?? this.proctoring()?.notice) : this.proctoring()?.notice) ?? []);

  /** Whether applying would change anything: a profile other than the one the exam already is. */
  protected readonly canApply = computed(() => this.selected() !== null && this.selected()?.id !== this.proctoring()?.profile);

  protected startChoosing(): void {
    this.choosing.set(true);
    this.selectedId.set(this.proctoring()?.profile ?? null);
    this.loadError.set(null);

    if (this.profiles() === null) {
      this.api.listProctoringProfiles().subscribe({
        next: (profiles) => this.profiles.set(profiles),
        error: (error: unknown) => this.loadError.set(extractErrorMessage(error, 'The profiles could not be loaded. Please try again.')),
      });
    }
  }

  protected choose(profile: ProctoringProfileDto): void {
    if (profile.available) {
      this.selectedId.set(profile.id);
    }
  }

  protected apply(): void {
    const profile = this.selected();
    if (profile !== null && this.canApply() && this.editable() && !this.busy()) {
      this.applied.emit(profile.id);
      this.choosing.set(false);
    }
  }

  protected cancel(): void {
    this.choosing.set(false);
  }
}
