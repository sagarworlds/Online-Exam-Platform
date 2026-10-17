import { AgeBand } from '../student.models';

/** The age a person born on `dateOfBirth` (`YYYY-MM-DD`) has reached on `today`, or null when it is not a date. */
function ageOn(dateOfBirth: string, today: Date): number | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateOfBirth);
  if (match === null) {
    return null;
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  let age = today.getFullYear() - year;
  // The birthday has not come yet this year.
  if (today.getMonth() + 1 < month || (today.getMonth() + 1 === month && today.getDate() < day)) {
    age--;
  }
  return age;
}

/**
 * Whether a date of birth makes a student a minor, by the platform's rule: anyone under 18 is, and an adult from the day they turn 18
 * (section 7.1). It lets the form say what a corrected date does before it is saved; the API works the age out for itself, by the UTC
 * day, so on a birthday the two can differ for a few hours and the API's answer is the one that counts.
 *
 * @returns The band, or null when the value is not a date yet.
 */
export function ageBandOn(dateOfBirth: string, today: Date = new Date()): AgeBand | null {
  const age = ageOn(dateOfBirth, today);
  if (age === null) {
    return null;
  }
  return age < 18 ? 'Minor' : 'Adult';
}
