import { describe, expect, it } from 'vitest';
import { InviteDto, InviteStatus } from '../invite.models';
import { groupByStudent, studentKey } from './invite-groups';

let counter = 0;

function invite(email: string, over: Partial<InviteDto> = {}): InviteDto {
  counter += 1;
  return {
    id: `i${counter}`,
    examId: 'e1',
    examName: 'Maths Final',
    batchMemberId: null,
    email,
    status: 'Pending',
    sentAt: '2026-10-02T09:00:00Z',
    acceptedAt: null,
    declinedAt: null,
    createdAt: '2026-10-02T09:00:00Z',
    updatedAt: '2026-10-02T09:00:00Z',
    emailSent: false,
    whatsAppSent: false,
    inviteLink: null,
    inviteCode: null,
    ...over,
  };
}

describe('groupByStudent', () => {
  it('is empty when there are no invitations', () => {
    expect(groupByStudent([])).toEqual([]);
  });

  it('puts every invitation to one student in one group, wherever they were listed', () => {
    const first = invite('amy@example.com', { examName: 'Maths' });
    const other = invite('ben@example.com');
    const second = invite('amy@example.com', { examName: 'Physics' });

    const groups = groupByStudent([first, other, second]);

    expect(groups.map((group) => group.email)).toEqual(['amy@example.com', 'ben@example.com']);
    expect(groups[0].invites.map((i) => i.examName).sort()).toEqual(['Maths', 'Physics']);
    expect(groups[1].invites).toEqual([other]);
  });

  it('treats an address as the same address whatever its case or surrounding spaces', () => {
    const groups = groupByStudent([invite('Amy@Example.com'), invite(' amy@example.com ')]);

    expect(groups).toHaveLength(1);
    expect(groups[0].key).toBe('amy@example.com');
    expect(studentKey('  Amy@Example.com')).toBe('amy@example.com');
  });

  it('shows the address as it was written on the newest invitation', () => {
    const groups = groupByStudent([
      invite('amy@example.com', { sentAt: '2026-10-01T09:00:00Z' }),
      invite('Amy@Example.com', { sentAt: '2026-10-03T09:00:00Z' }),
    ]);

    expect(groups[0].email).toBe('Amy@Example.com');
  });

  it("lists a group's invitations newest first and the groups by their newest invitation, whatever order they came in", () => {
    const old = invite('amy@example.com', { sentAt: '2026-10-01T09:00:00Z', examName: 'Old' });
    const recentForBen = invite('ben@example.com', { sentAt: '2026-10-05T09:00:00Z' });
    const recentForAmy = invite('amy@example.com', { sentAt: '2026-10-03T09:00:00Z', examName: 'Recent' });

    const groups = groupByStudent([old, recentForAmy, recentForBen]);

    expect(groups.map((group) => group.email)).toEqual(['ben@example.com', 'amy@example.com']);
    expect(groups[1].invites.map((i) => i.examName)).toEqual(['Recent', 'Old']);
  });

  it('keeps the order the API gave for invitations sent at the same instant', () => {
    const a = invite('amy@example.com', { examName: 'A' });
    const b = invite('amy@example.com', { examName: 'B' });

    expect(groupByStudent([a, b])[0].invites.map((i) => i.examName)).toEqual(['A', 'B']);
  });

  it('counts a group by status, only the statuses it has, pending first', () => {
    const statuses: InviteStatus[] = ['Revoked', 'Accepted', 'Pending', 'Accepted'];

    const [group] = groupByStudent(statuses.map((status) => invite('amy@example.com', { status })));

    expect(group.statusCounts).toEqual([
      { status: 'Pending', count: 1 },
      { status: 'Accepted', count: 2 },
      { status: 'Revoked', count: 1 },
    ]);
    expect(group.hasPending).toBe(true);
  });

  it('says a group has nothing pending when every invitation is settled', () => {
    const [group] = groupByStudent([invite('amy@example.com', { status: 'Accepted' }), invite('amy@example.com', { status: 'Declined' })]);

    expect(group.hasPending).toBe(false);
  });

  it('sinks an invitation with an unreadable date instead of failing', () => {
    const broken = invite('amy@example.com', { sentAt: 'not a date', examName: 'Broken' });
    const fine = invite('amy@example.com', { examName: 'Fine' });

    expect(groupByStudent([broken, fine])[0].invites.map((i) => i.examName)).toEqual(['Fine', 'Broken']);
  });

  it('does not change the list it was given', () => {
    const list = [invite('amy@example.com', { sentAt: '2026-10-01T09:00:00Z' }), invite('amy@example.com', { sentAt: '2026-10-02T09:00:00Z' })];
    const before = list.map((i) => i.id);

    groupByStudent(list);

    expect(list.map((i) => i.id)).toEqual(before);
  });
});
