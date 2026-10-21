/**
 * Times in a named time zone, for the exam scheduler. A `datetime-local` field holds a wall-clock reading with no zone ("2026-10-05T10:00"),
 * and the API takes UTC instants. The scheduler lets the author say which zone the reading is in, so the reading has to be turned into the
 * instant it names in that zone, and back. The browser's own `Date` only does that for the browser's zone.
 */

const formatters = new Map<string, Intl.DateTimeFormat>();

function formatterFor(timeZone: string): Intl.DateTimeFormat {
  let formatter = formatters.get(timeZone);
  if (formatter === undefined) {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
    formatters.set(timeZone, formatter);
  }
  return formatter;
}

/** The wall-clock reading of an instant in a zone, as milliseconds since the epoch of that reading taken as if it were UTC. */
function wallClockAsUtc(instant: number, timeZone: string): number {
  const parts = formatterFor(timeZone).formatToParts(new Date(instant));
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value);
  return Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'), part('second'));
}

/** Whether the browser knows the zone, so a name from the API that it does not know is not handed to `Intl` to throw on. */
export function isKnownTimeZone(timeZone: string): boolean {
  try {
    formatterFor(timeZone);
    return true;
  } catch {
    return false;
  }
}

/**
 * The instant, as an ISO string in UTC, that a wall-clock reading names in a zone: "2026-10-05T10:00" in Asia/Kolkata is
 * "2026-10-05T04:30:00.000Z". It is worked out twice, so a reading near a change of clocks lands on the instant that zone's clocks showed.
 */
export function localToUtcIso(local: string, timeZone: string): string {
  const [date, time] = local.split('T');
  const [year, month, day] = date.split('-').map(Number);
  const [hour, minute] = (time ?? '00:00').split(':').map(Number);
  const asIfUtc = Date.UTC(year, month - 1, day, hour, minute);

  const first = asIfUtc - (wallClockAsUtc(asIfUtc, timeZone) - asIfUtc);
  const second = asIfUtc - (wallClockAsUtc(first, timeZone) - first);
  return new Date(second).toISOString();
}

/** The wall-clock reading of an instant in a zone, in the form a `datetime-local` field takes: "2026-10-05T10:00". */
export function utcToLocalInput(utcIso: string, timeZone: string): string {
  const wall = new Date(wallClockAsUtc(Date.parse(utcIso), timeZone));
  return wall.toISOString().slice(0, 16);
}
