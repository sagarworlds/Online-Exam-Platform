import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Longest display name the API accepts, after trimming. Must match
 * User.MaxDisplayNameLength in the backend Identity domain (which also sizes
 * the database column); change both together.
 */
export const MAX_DISPLAY_NAME_LENGTH = 200;

/**
 * Oldest plausible age, in years, for a date of birth. Must match
 * User.MaximumPlausibleAgeYears in the backend Identity domain; change both together.
 */
export const MAX_PLAUSIBLE_AGE_YEARS = 120;

const ONE_DAY_MS = 24 * 60 * 60 * 1000;
const ISO_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Validates a `YYYY-MM-DD` date of birth (the value of an `<input type="date">`)
 * the same way the API does at registration (FR-43), so the user sees the problem
 * inline instead of after a round trip.
 *
 * @param now Clock to validate against; injectable so tests can pin "today".
 * @returns A validator reporting `required` (empty), `invalidDate` (not a real
 *   calendar date), `futureDate` (after UTC today + 1 day) or `implausibleAge`
 *   (more than {@link MAX_PLAUSIBLE_AGE_YEARS} years ago); null when valid.
 */
export function dateOfBirthValidator(now: () => Date = () => new Date()): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    if (typeof value !== 'string' || value.trim().length === 0) {
      return { required: true };
    }

    const dateOfBirth = parseIsoDate(value.trim());
    if (dateOfBirth === null) {
      return { invalidDate: true };
    }

    // The API compares against *UTC* today and allows one extra day, because a
    // user east of UTC (e.g. IST, +5:30) can already be on "tomorrow" in UTC
    // terms. Mirroring that exactly keeps client and server from disagreeing.
    const today = now();
    const utcToday = utcDate(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate());
    if (dateOfBirth > utcToday + ONE_DAY_MS) {
      return { futureDate: true };
    }

    if (dateOfBirth < subtractYears(today, MAX_PLAUSIBLE_AGE_YEARS)) {
      return { implausibleAge: true };
    }

    return null;
  };
}

/**
 * Rejects a value made only of whitespace, which `Validators.required` lets
 * through but the API trims to empty. Like Angular's own validators other than
 * `required`, an empty value is left to `Validators.required`.
 *
 * @returns `{ blank: true }` for a non-empty, whitespace-only string; otherwise null.
 */
export const notBlankValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: unknown = control.value;
  return typeof value === 'string' && value.length > 0 && value.trim().length === 0 ? { blank: true } : null;
};

/**
 * Message for the first problem {@link dateOfBirthValidator} reported.
 *
 * @param errors The control's current errors.
 * @returns A sentence to show under the field, or null when there is nothing to report.
 */
export function dateOfBirthErrorMessage(errors: ValidationErrors | null): string | null {
  if (errors === null) {
    return null;
  }
  if (errors['required']) {
    return 'Enter your date of birth.';
  }
  if (errors['invalidDate']) {
    return 'Enter a real date of birth.';
  }
  if (errors['futureDate']) {
    return 'Date of birth cannot be in the future.';
  }
  if (errors['implausibleAge']) {
    return `Date of birth cannot be more than ${MAX_PLAUSIBLE_AGE_YEARS} years ago.`;
  }
  return null;
}

/**
 * Message for the first problem with a display name validated by
 * `Validators.required`, {@link notBlankValidator} and
 * `Validators.maxLength(MAX_DISPLAY_NAME_LENGTH)`.
 *
 * @param errors The control's current errors.
 * @returns A sentence to show under the field, or null when there is nothing to report.
 */
export function displayNameErrorMessage(errors: ValidationErrors | null): string | null {
  if (errors === null) {
    return null;
  }
  if (errors['required'] || errors['blank']) {
    return 'Enter a display name.';
  }
  if (errors['maxlength']) {
    return `Display name must be ${MAX_DISPLAY_NAME_LENGTH} characters or fewer.`;
  }
  return null;
}

/**
 * A control's error message, shown only once the user has edited or left the
 * field, so an untouched form doesn't open covered in errors.
 *
 * @param control The form control to report on.
 * @param describe Maps the control's errors to a message, e.g. {@link displayNameErrorMessage}.
 * @returns The message to render, or null when the field is valid or untouched.
 */
export function visibleErrorMessage(
  control: AbstractControl,
  describe: (errors: ValidationErrors | null) => string | null,
): string | null {
  return control.invalid && (control.dirty || control.touched) ? describe(control.errors) : null;
}

/**
 * Formats a date as `YYYY-MM-DD` in the user's local time zone, e.g. for the
 * `max` attribute of a date picker.
 */
export function toLocalIsoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${String(date.getFullYear()).padStart(4, '0')}-${month}-${day}`;
}

/** Parses `YYYY-MM-DD` into UTC-midnight epoch ms, or null when it is not a real calendar date. */
function parseIsoDate(value: string): number | null {
  const match = ISO_DATE_PATTERN.exec(value);
  if (match === null) {
    return null;
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(utcDate(year, month - 1, day));
  // Date silently rolls invalid dates over (2026-02-30 becomes 2026-03-02); reject those.
  const isRealDate = date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
  return isRealDate ? date.getTime() : null;
}

/**
 * UTC midnight of the given day in epoch ms. Uses setUTCFullYear because
 * Date.UTC maps years 0-99 to 1900-1999.
 */
function utcDate(year: number, monthIndex: number, day: number): number {
  const date = new Date(0);
  date.setUTCFullYear(year, monthIndex, day);
  return date.getTime();
}

/**
 * UTC today minus whole years, clamping 29 February to the 28th in a non-leap
 * target year, the way .NET's DateOnly.AddYears does, so the oldest accepted
 * date matches the API's to the day.
 */
function subtractYears(now: Date, years: number): number {
  const year = now.getUTCFullYear() - years;
  const monthIndex = now.getUTCMonth();
  const daysInTargetMonth = new Date(utcDate(year, monthIndex + 1, 0)).getUTCDate();
  return utcDate(year, monthIndex, Math.min(now.getUTCDate(), daysInTargetMonth));
}
