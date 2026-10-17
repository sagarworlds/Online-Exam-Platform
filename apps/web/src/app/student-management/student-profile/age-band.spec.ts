import { describe, expect, it } from 'vitest';
import { ageBandOn } from './age-band';

describe('ageBandOn', () => {
  const today = new Date(2026, 9, 17); // 17 October 2026, local time

  it('calls someone who turns 18 today an adult', () => {
    expect(ageBandOn('2008-10-17', today)).toBe('Adult');
  });

  it('calls someone who turns 18 tomorrow a minor', () => {
    expect(ageBandOn('2008-10-18', today)).toBe('Minor');
  });

  it('calls a child and an old person what they are', () => {
    expect(ageBandOn('2015-03-01', today)).toBe('Minor');
    expect(ageBandOn('1950-03-01', today)).toBe('Adult');
  });

  it('has no band for something that is not a date', () => {
    expect(ageBandOn('', today)).toBeNull();
    expect(ageBandOn('17/10/2008', today)).toBeNull();
  });
});
