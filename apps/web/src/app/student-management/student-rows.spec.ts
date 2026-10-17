import { describe, expect, it } from 'vitest';
import { InviteDto } from '../invite-management/invite.models';
import { buildStudentRows, invitedAddressesWithoutAccount } from './student-rows';
import { StudentSummaryDto } from './student.models';

const account = (id: string, displayName: string, email: string | null, phoneNumber: string | null = null): StudentSummaryDto => ({
  id,
  displayName,
  email,
  phoneNumber,
  status: 'Active',
});

let sentAt = 1;
const invite = (id: string, email: string, status: InviteDto['status'] = 'Pending'): InviteDto =>
  ({ id, email, status, examName: `Exam ${id}`, sentAt: new Date(2026, 9, sentAt++).toISOString() }) as InviteDto;

const sources = (over: Partial<Parameters<typeof buildStudentRows>[0]> = {}) => ({
  accounts: [],
  holdersOfInvitedAddresses: [],
  invites: [],
  search: '',
  kept: [],
  ...over,
});

describe('buildStudentRows', () => {
  it('shows an account with the invitations sent to its address, whatever the case', () => {
    const rows = buildStudentRows(
      sources({ accounts: [account('a1', 'Asha', 'asha@example.com')], invites: [invite('i1', 'ASHA@Example.com'), invite('i2', 'asha@example.com')] }),
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].account?.id).toBe('a1');
    expect(rows[0].invitations?.invites.map((i) => i.id).sort()).toEqual(['i1', 'i2']);
  });

  it('shows an invited address that has no account as a row of its own, after the accounts', () => {
    const rows = buildStudentRows(
      sources({ accounts: [account('a1', 'Zed', 'zed@example.com')], invites: [invite('i1', 'newcomer@example.com')] }),
    );

    expect(rows.map((row) => row.title)).toEqual(['Zed', 'newcomer@example.com']);
    expect(rows[1].account).toBeNull();
    expect(rows[1].key).toBe('invited:newcomer@example.com');
  });

  it('shows an account with no invitations, and one with no e-mail address at all', () => {
    const rows = buildStudentRows(sources({ accounts: [account('a1', 'Asha', 'asha@example.com'), account('a2', 'Ben', null, '+919800000000')] }));

    expect(rows.map((row) => row.invitations)).toEqual([null, null]);
    expect(rows[1].phoneNumber).toBe('+919800000000');
  });

  it('orders accounts by name without regard to case, then by address', () => {
    const rows = buildStudentRows(
      sources({
        accounts: [account('a1', 'ben', 'b2@example.com'), account('a2', 'Asha', 'a@example.com'), account('a3', 'Ben', 'b1@example.com')],
      }),
    );

    expect(rows.map((row) => row.email)).toEqual(['a@example.com', 'b1@example.com', 'b2@example.com']);
  });

  it('does not call an invited address unregistered when an account beyond the search results holds it', () => {
    const rows = buildStudentRows(
      sources({
        accounts: [],
        holdersOfInvitedAddresses: [account('a1', 'Asha', 'asha@example.com')],
        invites: [invite('i1', 'asha@example.com')],
      }),
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].account?.id).toBe('a1');
    expect(rows.some((row) => row.account === null)).toBe(false);
  });

  describe('with a search', () => {
    it('keeps the accounts the API matched whether or not they were invited', () => {
      const rows = buildStudentRows(sources({ search: 'asha', accounts: [account('a1', 'Asha Rao', 'ar@example.com')] }));

      expect(rows.map((row) => row.account?.id)).toEqual(['a1']);
    });

    it('keeps only the invited addresses that contain it, without regard to case', () => {
      const rows = buildStudentRows(
        sources({ search: 'BEN', invites: [invite('i1', 'ben@example.com'), invite('i2', 'asha@example.com')] }),
      );

      expect(rows.map((row) => row.email)).toEqual(['ben@example.com']);
    });

    it('shows an account holding an invited address when the address contains the search, though the search did not match it', () => {
      const rows = buildStudentRows(
        sources({
          search: 'asha@',
          holdersOfInvitedAddresses: [account('a1', 'A. R.', 'asha@example.com'), account('a2', 'Other', 'other@example.com')],
          invites: [invite('i1', 'asha@example.com'), invite('i2', 'other@example.com')],
        }),
      );

      expect(rows.map((row) => row.account?.id)).toEqual(['a1']);
    });

    it('keeps showing a student staff have just changed though the search no longer matches them', () => {
      const rows = buildStudentRows(
        sources({
          search: 'old@example.com',
          kept: [account('a1', 'Asha', 'new@example.com')],
          invites: [invite('i1', 'new@example.com')],
        }),
      );

      expect(rows.map((row) => row.account?.id)).toEqual(['a1']);
      // Their invitations moved with the address, so they are the student's own and not an unregistered row of their own.
      expect(rows[0].invitations?.invites.map((i) => i.id)).toEqual(['i1']);
    });

    it('does not repeat a kept student the search also matched', () => {
      const asha = account('a1', 'Asha', 'asha@example.com');
      const rows = buildStudentRows(sources({ search: 'asha', accounts: [asha], kept: [asha], holdersOfInvitedAddresses: [asha] }));

      expect(rows).toHaveLength(1);
    });

    it('does not repeat an account that is both a match and the holder of an invited address', () => {
      const asha = account('a1', 'Asha', 'asha@example.com');
      const rows = buildStudentRows(
        sources({ search: 'asha', accounts: [asha], holdersOfInvitedAddresses: [asha], invites: [invite('i1', 'asha@example.com')] }),
      );

      expect(rows).toHaveLength(1);
    });
  });
});

describe('invitedAddressesWithoutAccount', () => {
  it('lists each invited address once that no account holds, in the case it was last written', () => {
    const addresses = invitedAddressesWithoutAccount(
      [invite('i1', 'asha@example.com'), invite('i2', 'Ben@Example.com'), invite('i3', 'ben@example.com')],
      [account('a1', 'Asha', 'ASHA@example.com')],
    );

    expect(addresses).toHaveLength(1);
    expect(addresses[0].toLowerCase()).toBe('ben@example.com');
  });

  it('lists nothing when every invited address has an account, or there are no invitations', () => {
    expect(invitedAddressesWithoutAccount([], [account('a1', 'Asha', 'asha@example.com')])).toEqual([]);
    expect(invitedAddressesWithoutAccount([invite('i1', 'asha@example.com')], [account('a1', 'Asha', 'asha@example.com')])).toEqual([]);
  });
});
