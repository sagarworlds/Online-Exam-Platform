import { matchesSearch, phoneSearchOf } from './candidate-search';

describe('phoneSearchOf', () => {
  it.each([
    ['9876543210', '9876543210'],
    ['  98765 43210  ', '98765 43210'],
    ['98765-43210', '98765-43210'],
    ['+91 98765 43210', '+91 98765 43210'],
    ['(+91) 98765.43210', '(+91) 98765.43210'],
    ['098765 43210', '098765 43210'],
    ['0091 98765', '0091 98765'],
    ['432', '432'], // the fewest digits that are worth asking about
  ])('asks the API about %j', (typed, sent) => {
    expect(phoneSearchOf(typed)).toBe(sent);
  });

  it.each([
    [''],
    ['   '],
    ['98'], // too few digits
    ['+91'], // a country code alone
    ['000'], // only trunk zeros
    ['amy@example.com'],
    ['amy'],
    ['amy98765'], // letters make it a name or an address
    ['98765x'],
    ['98+7654'], // a plus only means something at the start
    ['98765#'],
  ])('does not ask about %j', (typed) => {
    expect(phoneSearchOf(typed)).toBeNull();
  });
});

describe('matchesSearch', () => {
  const amy = { candidateId: 'c1', email: 'Amy.Shah@Example.com' };
  const none = new Set<string>();

  it('matches everyone when nothing is typed', () => {
    expect(matchesSearch(amy, '', none)).toBe(true);
    expect(matchesSearch(amy, '   ', none)).toBe(true);
  });

  it('matches part of the email address, in any case', () => {
    expect(matchesSearch(amy, 'shah', none)).toBe(true);
    expect(matchesSearch(amy, ' AMY.SHAH@EXAMPLE ', none)).toBe(true);
    expect(matchesSearch(amy, 'ben', none)).toBe(false);
  });

  it('matches a candidate the API found by phone number, whatever their email says', () => {
    expect(matchesSearch(amy, '98765 43210', new Set(['c1']))).toBe(true);
    expect(matchesSearch(amy, '98765 43210', new Set(['c2']))).toBe(false);
    expect(matchesSearch(amy, '98765 43210', none)).toBe(false);
  });

  it('matches by either way at once', () => {
    // "98" is in this address and was also found as part of a number: either is a match.
    expect(matchesSearch({ candidateId: 'c3', email: 'user98@example.com' }, '98', none)).toBe(true);
    expect(matchesSearch({ candidateId: 'c4', email: 'ben@example.com' }, '98', new Set(['c4']))).toBe(true);
  });
});
