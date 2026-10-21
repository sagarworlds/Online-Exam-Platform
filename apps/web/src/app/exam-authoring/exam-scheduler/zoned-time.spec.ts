import { existsInZone, isKnownTimeZone, localToUtcIso, utcToLocalInput } from './zoned-time';

describe('times in a named time zone', () => {
  it('turns a reading in India into the instant it names (UTC+5:30)', () => {
    expect(localToUtcIso('2026-10-05T10:00', 'Asia/Kolkata')).toBe('2026-10-05T04:30:00.000Z');
  });

  it('keeps a reading in UTC as it is', () => {
    expect(localToUtcIso('2026-10-05T10:00', 'UTC')).toBe('2026-10-05T10:00:00.000Z');
  });

  it('knows a zone’s summer and winter clocks (London, then New York)', () => {
    expect(localToUtcIso('2026-07-01T09:00', 'Europe/London')).toBe('2026-07-01T08:00:00.000Z');
    expect(localToUtcIso('2026-01-15T09:00', 'Europe/London')).toBe('2026-01-15T09:00:00.000Z');
    expect(localToUtcIso('2026-07-01T09:00', 'America/New_York')).toBe('2026-07-01T13:00:00.000Z');
    expect(localToUtcIso('2026-01-15T09:00', 'America/New_York')).toBe('2026-01-15T14:00:00.000Z');
  });

  it('gives the same reading a different instant in a different zone, which is why the zone chosen has to be used', () => {
    const india = Date.parse(localToUtcIso('2026-10-05T10:00', 'Asia/Kolkata'));
    const sydney = Date.parse(localToUtcIso('2026-10-05T10:00', 'Australia/Sydney'));

    // Sydney is on summer time (UTC+11) in October, India UTC+5:30: five and a half hours apart.
    expect(india - sydney).toBe(5.5 * 3_600_000);
  });

  it('reads an instant back as the wall clock of the zone, and a reading goes there and back unchanged', () => {
    expect(utcToLocalInput('2026-10-05T04:30:00.000Z', 'Asia/Kolkata')).toBe('2026-10-05T10:00');
    for (const zone of ['Asia/Kolkata', 'UTC', 'Europe/London', 'America/New_York', 'America/Los_Angeles', 'Australia/Sydney']) {
      for (const reading of ['2026-01-15T09:00', '2026-07-01T23:45', '2026-12-31T00:00']) {
        expect(utcToLocalInput(localToUtcIso(reading, zone), zone)).toBe(reading);
      }
    }
  });

  it('shows midnight as 00:00, not 24:00', () => {
    expect(utcToLocalInput('2026-10-05T00:00:00.000Z', 'UTC')).toBe('2026-10-05T00:00');
  });

  describe('where the clocks change', () => {
    it('knows a reading the clocks skip when they go forward does not exist: 2:30 on the night New York springs forward', () => {
      expect(existsInZone('2026-03-08T02:30', 'America/New_York')).toBe(false);
      expect(existsInZone('2026-03-08T01:30', 'America/New_York')).toBe(true);
      expect(existsInZone('2026-03-08T03:30', 'America/New_York')).toBe(true);
      expect(existsInZone('2026-03-29T01:30', 'Europe/London')).toBe(false);
      expect(existsInZone('2026-10-04T02:30', 'Australia/Sydney')).toBe(false);
    });

    it('knows an ordinary reading exists, in every zone, and in zones that never change their clocks', () => {
      for (const zone of ['Asia/Kolkata', 'UTC', 'Europe/London', 'America/New_York', 'America/Los_Angeles', 'Australia/Sydney']) {
        expect(existsInZone('2026-06-15T12:00', zone)).toBe(true);
      }
      expect(existsInZone('2026-03-08T02:30', 'Asia/Kolkata')).toBe(true);
    });

    it('accepts a reading in the hour the clocks repeat when they go back, since both moments of it exist', () => {
      expect(existsInZone('2026-11-01T01:30', 'America/New_York')).toBe(true);
    });
  });

  it('reads a year below 100 as that year, not as one in the 1900s', () => {
    expect(localToUtcIso('0020-01-01T00:00', 'UTC')).toBe('0020-01-01T00:00:00.000Z');
    expect(utcToLocalInput('0020-01-01T00:00:00.000Z', 'UTC')).toBe('0020-01-01T00:00');
  });

  it('tells a zone the browser knows from one it does not', () => {
    expect(isKnownTimeZone('Asia/Kolkata')).toBe(true);
    expect(isKnownTimeZone('Mars/Olympus_Mons')).toBe(false);
  });
});
