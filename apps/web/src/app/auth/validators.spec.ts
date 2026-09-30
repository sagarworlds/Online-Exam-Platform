import { FormControl, Validators } from '@angular/forms';
import {
  MAX_DISPLAY_NAME_LENGTH,
  dateOfBirthErrorMessage,
  dateOfBirthValidator,
  displayNameErrorMessage,
  notBlankValidator,
  toLocalIsoDate,
} from './validators';

describe('dateOfBirthValidator', () => {
  // Pinned "now": 2026-10-01 12:00 UTC.
  const validate = (value: string, now = new Date(Date.UTC(2026, 9, 1, 12))) =>
    dateOfBirthValidator(() => now)(new FormControl(value));

  it('accepts today', () => {
    expect(validate('2026-10-01')).toBeNull();
  });

  it('accepts a date one day ahead of UTC today, for users east of UTC', () => {
    expect(validate('2026-10-02')).toBeNull();
  });

  it('reports futureDate beyond the one-day tolerance', () => {
    expect(validate('2026-10-03')).toEqual({ futureDate: true });
  });

  it('accepts exactly the maximum plausible age', () => {
    expect(validate('1906-10-01')).toBeNull();
  });

  it('reports implausibleAge for someone 121 years old', () => {
    expect(validate('1905-10-01')).toEqual({ implausibleAge: true });
  });

  it('clamps 29 February like the API when the oldest year is not a leap year', () => {
    const leapDay = new Date(Date.UTC(2020, 1, 29, 12));

    expect(validate('1900-02-28', leapDay)).toBeNull();
    expect(validate('1900-02-27', leapDay)).toEqual({ implausibleAge: true });
  });

  it('reports required when empty', () => {
    expect(validate('')).toEqual({ required: true });
  });

  it('reports invalidDate for a date that does not exist or is malformed', () => {
    expect(validate('2026-02-30')).toEqual({ invalidDate: true });
    expect(validate('01/10/2000')).toEqual({ invalidDate: true });
  });

  it('treats two-digit years literally instead of as 19xx', () => {
    expect(validate('0050-01-01')).toEqual({ implausibleAge: true });
  });

  it('maps each error to a message', () => {
    expect(dateOfBirthErrorMessage({ futureDate: true })).toContain('cannot be in the future');
    expect(dateOfBirthErrorMessage({ implausibleAge: true })).toContain('120 years');
    expect(dateOfBirthErrorMessage(null)).toBeNull();
  });
});

describe('notBlankValidator', () => {
  it('reports blank for a whitespace-only name', () => {
    expect(notBlankValidator(new FormControl('   '))).toEqual({ blank: true });
  });

  it('accepts a real name and leaves empty values to Validators.required', () => {
    expect(notBlankValidator(new FormControl('Ada'))).toBeNull();
    expect(notBlankValidator(new FormControl(''))).toBeNull();
  });
});

describe('displayNameErrorMessage', () => {
  it('asks for a name when it is blank', () => {
    expect(displayNameErrorMessage({ blank: true })).toBe('Enter a display name.');
  });

  it('explains the length limit shared with the API', () => {
    const control = new FormControl('x'.repeat(MAX_DISPLAY_NAME_LENGTH + 1), Validators.maxLength(MAX_DISPLAY_NAME_LENGTH));

    expect(displayNameErrorMessage(control.errors)).toContain(`${MAX_DISPLAY_NAME_LENGTH} characters`);
  });
});

describe('toLocalIsoDate', () => {
  it('formats a local date as YYYY-MM-DD', () => {
    expect(toLocalIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
});
