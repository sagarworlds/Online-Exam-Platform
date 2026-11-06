/** The fewest digits a phone search holds, after any leading zeros. Fewer would match nearly everyone; the API applies the same rule. */
export const MIN_PHONE_DIGITS = 3;

/**
 * The text to ask the API about, when what was typed reads as part of a phone number: digits with spaces, dashes, dots and brackets between
 * them, and a `+` at the start. Anything with a letter or an `@` in it is an email address (or a name), which the page matches itself,
 * and a stretch of fewer than {@link MIN_PHONE_DIGITS} digits is too little to look for.
 *
 * This only decides whether to ask. The API decides what matches, so a number written in some way this does not expect is still found
 * as long as the API reads it.
 *
 * @returns what was typed, trimmed, or null when it is not worth asking about.
 */
export function phoneSearchOf(text: string): string | null {
  const trimmed = text.trim();
  if (!/^[(\s]*\+?[\d\s().-]*$/.test(trimmed)) {
    return null;
  }

  const digits = trimmed.replace(/\D/g, '').replace(/^0+/, '');
  return digits.length >= MIN_PHONE_DIGITS ? trimmed : null;
}

/** What the page knows about one candidate when it matches them to a search: who they are, and the address they were invited at. */
export interface SearchableCandidate {
  readonly candidateId: string;
  readonly email: string;
}

/**
 * Whether a candidate matches what was typed in the search box: part of their email address, without regard to case, or a phone number
 * the API said they hold. The page never has the numbers, so the numbers' matches come in as the ids the API returned.
 *
 * @param phoneMatches the candidates the API found for this search by phone number; empty while it has not answered or the search is not a number.
 */
export function matchesSearch(candidate: SearchableCandidate, text: string, phoneMatches: ReadonlySet<string>): boolean {
  const needle = text.trim().toLowerCase();
  return needle === '' || candidate.email.toLowerCase().includes(needle) || phoneMatches.has(candidate.candidateId);
}
