import { isKnownTimeZone, localToUtcIso, utcToLocalInput } from './zoned-time';

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

  it('tells a zone the browser knows from one it does not', () => {
    expect(isKnownTimeZone('Asia/Kolkata')).toBe(true);
    expect(isKnownTimeZone('Mars/Olympus_Mons')).toBe(false);
  });
});
