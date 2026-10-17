import { InviteDto } from '../invite-management/invite.models';
import { InviteGroup, groupByStudent, studentKey } from './invite-groups';
import { StudentSummaryDto } from './student.models';

/** One line of the Students page: a student's account, or an address that was invited and has not registered yet. */
export interface StudentRow {
  /** What the row is identified by, so it stays open when the list is read again: the account's id, or the invited address. */
  key: string;
  /** The account, or null for an invited address nobody has registered with. */
  account: StudentSummaryDto | null;
  /** What names the row: the student's name, or for an address with no account the address itself. */
  title: string;
  email: string | null;
  phoneNumber: string | null;
  /** Every invitation sent to the student's address, or null when there are none. */
  invitations: InviteGroup | null;
}

/** What the page has been told: the accounts the search found, the accounts holding invited addresses, and the invitations. */
export interface StudentRowSources {
  /** The students the search matched, in the order the API gave them (by name). */
  accounts: readonly StudentSummaryDto[];
  /** The students holding addresses that were invited, found by address. They are not all in `accounts`: that stops at 200. */
  holdersOfInvitedAddresses: readonly StudentSummaryDto[];
  invites: readonly InviteDto[];
  /** What was typed in the search box. */
  search: string;
  /**
   * Students shown whatever the search says: the one staff have just changed. Their new name or address may no longer match what was
   * typed, and the row would vanish with the message that says what was done to them.
   */
  kept: readonly StudentSummaryDto[];
}

/**
 * Brings the students and the invitations together into the page's rows. A student is the account holding an address, and their
 * invitations are the ones sent to that address (without regard to case, as the API treats it); an invited address with no account is
 * a row of its own, waiting to register.
 *
 * The search is the API's for accounts. An address that was invited is held to it here, since the API's search never saw it: it stays when
 * the search is blank or part of the address. An account that holds an invited address but was not among the search's matches is
 * shown only on that same condition, which is how it is found by the address staff typed.
 */
export function buildStudentRows(sources: StudentRowSources): StudentRow[] {
  const wanted = sources.search.trim().toLowerCase();
  const matchesSearch = (email: string): boolean => wanted === '' || email.toLowerCase().includes(wanted);

  const groups = groupByStudent(sources.invites);
  const groupByAddress = new Map(groups.map((group) => [group.key, group]));

  const matched = new Set(sources.accounts.map((account) => account.id));
  const kept = sources.kept.filter((account) => !matched.has(account.id));
  const keptIds = new Set(kept.map((account) => account.id));
  const extras = sources.holdersOfInvitedAddresses.filter(
    (account) => !matched.has(account.id) && !keptIds.has(account.id) && account.email !== null && matchesSearch(account.email),
  );

  const accountRows = [...sources.accounts, ...kept, ...extras].map((account) => accountRow(account, groupByAddress));
  accountRows.sort(byNameThenAddress);

  const registered = new Set(
    [...sources.accounts, ...sources.kept, ...sources.holdersOfInvitedAddresses]
      .filter((a) => a.email !== null)
      .map((a) => studentKey(a.email as string)),
  );
  const invitedOnly = groups
    .filter((group) => !registered.has(group.key) && matchesSearch(group.email))
    .map((group): StudentRow => ({
      key: `invited:${group.key}`,
      account: null,
      title: group.email,
      email: group.email,
      phoneNumber: null,
      invitations: group,
    }));

  return [...accountRows, ...invitedOnly];
}

function accountRow(account: StudentSummaryDto, groupByAddress: ReadonlyMap<string, InviteGroup>): StudentRow {
  return {
    key: `account:${account.id}`,
    account,
    title: account.displayName,
    email: account.email,
    phoneNumber: account.phoneNumber,
    invitations: account.email === null ? null : (groupByAddress.get(studentKey(account.email)) ?? null),
  };
}

function byNameThenAddress(a: StudentRow, b: StudentRow): number {
  return a.title.localeCompare(b.title, undefined, { sensitivity: 'base' }) || (a.email ?? '').localeCompare(b.email ?? '');
}

/** The addresses that were invited and that none of the given accounts hold, which is whom the page asks the API about. */
export function invitedAddressesWithoutAccount(invites: readonly InviteDto[], accounts: readonly StudentSummaryDto[]): string[] {
  const known = new Set(accounts.filter((a) => a.email !== null).map((a) => studentKey(a.email as string)));
  return groupByStudent(invites)
    .filter((group) => !known.has(group.key))
    .map((group) => group.email);
}
