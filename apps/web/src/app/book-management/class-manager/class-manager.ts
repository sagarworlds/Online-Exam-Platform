import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { extractErrorMessage } from '../../shared/problem-details';
import { compareNames } from '../book-class';
import { ClassApiService } from '../class-api.service';
import { CLASS_LIMITS, ClassDto } from '../class.models';

/**
 * The classes (such as "4th") books are filed under: add one, rename it, archive it or restore it. A class is archived,
 * never deleted, so the books under it are not lost. The page owns the list and reads it again when told that something
 * changed, since a rename also changes what its books are called and an archive changes what the book form offers.
 */
@Component({
  selector: 'app-class-manager',
  imports: [ReactiveFormsModule],
  templateUrl: './class-manager.html',
})
export class ClassManager {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ClassApiService);

  /** Every class, archived ones included. */
  readonly classes = input.required<readonly ClassDto[]>();
  /** A class was added, renamed, archived or restored, so whatever shows classes or their books is out of date. */
  readonly changed = output<void>();

  protected readonly limits = CLASS_LIMITS;
  protected readonly sorted = computed(() => [...this.classes()].sort((a, b) => compareNames(a.name, b.name)));
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** The class whose name is being edited, if any. */
  protected readonly renamingId = signal<string | null>(null);

  // A name of spaces only is a blank name, so it is refused here as the API would.
  private readonly nameValidators = [Validators.required, Validators.pattern(/\S/), Validators.maxLength(CLASS_LIMITS.name)];
  protected readonly addForm = this.formBuilder.nonNullable.group({ name: ['', this.nameValidators] });
  protected readonly renameForm = this.formBuilder.nonNullable.group({ name: ['', this.nameValidators] });

  protected add(): void {
    if (this.addForm.invalid || this.busy()) {
      return;
    }

    this.run(this.api.create({ name: this.addForm.getRawValue().name.trim() }), () => this.addForm.reset({ name: '' }));
  }

  protected startRename(item: ClassDto): void {
    this.renamingId.set(item.id);
    this.renameForm.reset({ name: item.name });
  }

  protected cancelRename(): void {
    this.renamingId.set(null);
  }

  protected saveRename(item: ClassDto): void {
    if (this.renameForm.invalid || this.busy()) {
      return;
    }

    const name = this.renameForm.getRawValue().name.trim();
    if (name === item.name) {
      this.renamingId.set(null);
      return;
    }

    this.run(this.api.rename(item.id, { name }), () => this.renamingId.set(null));
  }

  protected setArchived(item: ClassDto, archived: boolean): void {
    if (!this.busy()) {
      this.run(archived ? this.api.archive(item.id) : this.api.restore(item.id));
    }
  }

  // Runs one change; a refusal is shown here and nothing is reported as changed.
  private run(call: Observable<ClassDto>, afterSuccess?: () => void): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        afterSuccess?.();
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
