import { Component, ElementRef, Injector, OnInit, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { map, startWith } from 'rxjs';
import {
  MAX_DISPLAY_NAME_LENGTH,
  dateOfBirthErrorMessage,
  dateOfBirthValidator,
  displayNameErrorMessage,
  maxTrimmedLengthValidator,
  notBlankValidator,
  toLocalIsoDate,
  visibleErrorMessage,
} from '../../auth/validators';
import { extractErrorMessage } from '../../shared/problem-details';
import { StudentApiService } from '../student-api.service';
import { StudentDto, StudentField, UpdateStudentResult } from '../student.models';
import { ageBandOn } from './age-band';

/** What each field a change can touch is called in the words staff read. */
const FIELD_LABELS: Record<StudentField, string> = {
  name: 'name',
  email: 'e-mail address',
  phoneNumber: 'mobile number',
  dateOfBirth: 'date of birth',
};

/**
 * One student's profile. Staff who may change it (`canEdit`) get a form for the name, e-mail address, mobile number and date of birth;
 * the rest see the same details to read.
 *
 * A change takes effect at once, and a new e-mail address or mobile number signs the student out, so before saving one of those, or a
 * date of birth that moves the student across the age of 18, the form says what will happen and waits for a second click.
 */
@Component({
  selector: 'app-student-profile',
  imports: [ReactiveFormsModule],
  templateUrl: './student-profile.html',
  styleUrl: './student-profile.css',
})
export class StudentProfile implements OnInit {
  private readonly studentApi = inject(StudentApiService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly injector = inject(Injector);

  /** The account to show. */
  readonly studentId = input.required<string>();

  /** Whether the signed-in staff member may change the profile (`student.manage`). */
  readonly canEdit = input(false);

  /** Emitted after the profile was saved, so the page can read its list again. */
  readonly saved = output<UpdateStudentResult>();

  protected readonly maxNameLength = MAX_DISPLAY_NAME_LENGTH;
  protected readonly today = toLocalIsoDate(new Date());

  protected readonly student = signal<StudentDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly result = signal<UpdateStudentResult | null>(null);

  /** Whether the form is showing what the change will do and waiting for the staff member to go ahead. */
  protected readonly confirming = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, notBlankValidator, maxTrimmedLengthValidator(MAX_DISPLAY_NAME_LENGTH)]],
    email: [''],
    phoneNumber: [''],
    dateOfBirth: ['', dateOfBirthValidator()],
  });

  private readonly confirmButton = viewChild<ElementRef<HTMLButtonElement>>('confirmButton');
  private readonly resultNotice = viewChild<ElementRef<HTMLElement>>('resultNotice');

  private readonly typed = toSignal(
    this.form.valueChanges.pipe(
      startWith(null),
      map(() => this.form.getRawValue()),
    ),
    { requireSync: true },
  );

  /** Which fields differ from what is saved. */
  protected readonly changes = computed<StudentField[]>(() => {
    const current = this.student();
    if (current === null) {
      return [];
    }

    const typed = this.typed();
    const changes: StudentField[] = [];
    if (typed.displayName.trim() !== current.displayName) changes.push('name');
    if (typed.email.trim() !== (current.email ?? '')) changes.push('email');
    if (typed.phoneNumber.trim() !== (current.phoneNumber ?? '')) changes.push('phoneNumber');
    if (typed.dateOfBirth !== current.dateOfBirth) changes.push('dateOfBirth');
    return changes;
  });

  /** The student has to be reachable somewhere to sign in: an account with neither an address nor a number could never get a code. */
  protected readonly noContact = computed(() => this.typed().email.trim() === '' && this.typed().phoneNumber.trim() === '');

  /** The band the typed date of birth puts the student in, when it differs from the one they are in now. */
  protected readonly ageBandChange = computed<{ from: string; to: string } | null>(() => {
    const current = this.student();
    const to = ageBandOn(this.typed().dateOfBirth);
    return current !== null && to !== null && to !== current.ageBand ? { from: current.ageBand, to } : null;
  });

  /** What saving will do beyond changing the details, said before it is done. Empty when saving needs no second click. */
  protected readonly consequences = computed<string[]>(() => {
    const changes = this.changes();
    const consequences: string[] = [];
    if (changes.includes('email')) {
      consequences.push(
        'The student is signed out and signs in again with a code sent to the new address. Their invitations move to it. Nothing is sent to the old address.',
      );
    }
    if (changes.includes('phoneNumber')) {
      consequences.push('The student is signed out and signs in again with a code sent to the new number. Nothing is sent to the old number.');
    }
    const band = this.ageBandChange();
    if (band !== null && changes.includes('dateOfBirth')) {
      consequences.push(
        band.to === 'Minor'
          ? 'This makes the student a minor, so guardian consent applies to them.'
          : 'This makes the student an adult, so guardian consent no longer applies to them.',
      );
    }
    return consequences;
  });

  /** The sentences that say what the last save did. */
  protected readonly resultLines = computed<string[]>(() => {
    const result = this.result();
    if (result === null) {
      return [];
    }
    if (result.changed.length === 0) {
      return ['Nothing was different, so nothing was changed.'];
    }

    const lines = [`Saved: ${result.changed.map((field) => FIELD_LABELS[field]).join(', ')}.`];
    if (result.invitationsMoved > 0) {
      lines.push(`${result.invitationsMoved} ${result.invitationsMoved === 1 ? 'invitation' : 'invitations'} moved to the new e-mail address.`);
    }
    if (result.signedOut) {
      lines.push('The student was signed out. They sign in again with a code sent to their e-mail address or mobile number.');
    }
    return lines;
  });

  constructor() {
    // Editing again after being asked to confirm is a different change, which has to be asked about again.
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.confirming.set(false));
  }

  ngOnInit(): void {
    this.studentApi.getStudent(this.studentId()).subscribe({
      next: (student) => {
        this.show(student);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(extractErrorMessage(error));
      },
    });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.noContact() || this.changes().length === 0 || this.saving() || this.confirming()) {
      return;
    }

    if (this.consequences().length > 0) {
      this.confirming.set(true);
      afterNextRender(() => this.confirmButton()?.nativeElement.focus(), { injector: this.injector });
      return;
    }

    this.save();
  }

  protected cancelConfirmation(): void {
    this.confirming.set(false);
  }

  protected save(): void {
    if (this.saving()) {
      return;
    }

    const typed = this.form.getRawValue();
    this.saving.set(true);
    this.saveError.set(null);
    this.confirming.set(false);
    this.studentApi
      .updateStudent(this.studentId(), {
        displayName: typed.displayName.trim(),
        email: typed.email.trim() === '' ? null : typed.email.trim(),
        phoneNumber: typed.phoneNumber.trim() === '' ? null : typed.phoneNumber.trim(),
        dateOfBirth: typed.dateOfBirth,
      })
      .subscribe({
        next: (result) => {
          this.saving.set(false);
          this.show(result.student);
          this.result.set(result);
          this.saved.emit(result);
          // The notice is where the eye is not, below the button: moving focus to it makes a screen reader say what happened.
          afterNextRender(() => this.resultNotice()?.nativeElement.focus(), { injector: this.injector });
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.saveError.set(extractErrorMessage(error));
        },
      });
  }

  /** Shows a profile as saved, in the form too. */
  private show(student: StudentDto): void {
    this.student.set(student);
    this.form.reset({
      displayName: student.displayName,
      email: student.email ?? '',
      phoneNumber: student.phoneNumber ?? '',
      dateOfBirth: student.dateOfBirth,
    });
  }

  // Methods and not computed values: whether a control has been touched is not a signal, so a computed would never see a field being left.
  protected nameError(): string | null {
    return visibleErrorMessage(this.form.controls.displayName, displayNameErrorMessage);
  }

  protected dateOfBirthError(): string | null {
    return visibleErrorMessage(this.form.controls.dateOfBirth, dateOfBirthErrorMessage);
  }
}
