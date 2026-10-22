import { movedBy } from './reorder';

describe('movedBy', () => {
  const ids = ['a', 'b', 'c', 'd'];

  it('moves an id a place toward the start', () => {
    expect(movedBy(ids, 'c', 'up')).toEqual(['a', 'c', 'b', 'd']);
  });

  it('moves an id a place toward the end', () => {
    expect(movedBy(ids, 'b', 'down')).toEqual(['a', 'c', 'b', 'd']);
  });

  it('can move the first down and the last up, which are the ends it can still go from', () => {
    expect(movedBy(ids, 'a', 'down')).toEqual(['b', 'a', 'c', 'd']);
    expect(movedBy(ids, 'd', 'up')).toEqual(['a', 'b', 'd', 'c']);
  });

  it('is nothing for a move off either end, so no request is made for it', () => {
    expect(movedBy(ids, 'a', 'up')).toBeNull();
    expect(movedBy(ids, 'd', 'down')).toBeNull();
  });

  it('is nothing for an id that is not in the list, and for a list of one', () => {
    expect(movedBy(ids, 'z', 'up')).toBeNull();
    expect(movedBy(['only'], 'only', 'up')).toBeNull();
    expect(movedBy(['only'], 'only', 'down')).toBeNull();
  });

  it('leaves the list it was given as it was', () => {
    const original = ['a', 'b', 'c'];

    movedBy(original, 'b', 'up');

    expect(original).toEqual(['a', 'b', 'c']);
  });
});
