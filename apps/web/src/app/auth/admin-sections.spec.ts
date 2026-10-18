import { englishTranslate } from '../i18n/i18n.service';
import { ADMIN_SECTION_GROUPS, AdminSection, Permission, SETTINGS_PAGES, SETTINGS_SECTION, openAdminGroups, openSections } from './admin-sections';

/** A user who holds exactly these permissions, as far as the menu can tell. */
const holding = (...permissions: string[]) => ({
  hasAnyPermission: (codes: readonly string[]) => codes.some((code) => permissions.includes(code)),
});

const allSections = ADMIN_SECTION_GROUPS.flatMap((group) => group.sections);
const nameOf = (section: AdminSection) => englishTranslate(section.label);

describe('the admin menu', () => {
  it('has four headed groups, then Settings under no heading, in this order', () => {
    expect(ADMIN_SECTION_GROUPS.map((group) => (group.heading === null ? null : englishTranslate(group.heading)))).toEqual([
      'Content',
      'Exams',
      'People',
      'Reviews',
      null,
    ]);
  });

  it('puts each area in its group, in order', () => {
    expect(ADMIN_SECTION_GROUPS.map((group) => group.sections.map(nameOf))).toEqual([
      ['Questions', 'Books'],
      ['Exams'],
      ['Students', 'Batches', 'Guardians'],
      ['Attempt requests', 'Disputes', 'Reported issues', 'Data requests'],
      ['Settings'],
    ]);
  });

  it('keeps the four Settings pages out of the menu itself, and keeps their addresses', () => {
    const topLevel = allSections.map((section) => section.path);

    for (const path of ['/admin/branding', '/admin/instruction-templates', '/admin/whatsapp', '/admin/otp-codes']) {
      expect(topLevel).not.toContain(path);
    }
    expect(SETTINGS_PAGES.map((page) => page.path)).toEqual([
      '/admin/branding',
      '/admin/instruction-templates',
      '/admin/whatsapp',
      '/admin/otp-codes',
    ]);
  });

  it('never gives two entries the same address, since the sidebar tells its links apart by it', () => {
    const paths = [...allSections, ...SETTINGS_PAGES].map((section) => section.path);

    expect(new Set(paths).size).toBe(paths.length);
  });

  it('names every area and describes it, with a drawn icon', () => {
    for (const section of [...allSections, ...SETTINGS_PAGES]) {
      expect(nameOf(section), section.path).not.toBe('');
      expect(englishTranslate(section.description), section.path).not.toBe('');
      expect(section.icon, section.path).toBeTruthy();
    }
  });

  it('opens every area with at least one permission', () => {
    for (const section of [...allSections, ...SETTINGS_PAGES]) {
      expect(section.permissions.length, section.path).toBeGreaterThan(0);
    }
  });
});

describe('the Settings entry', () => {
  it('opens at /admin/settings and lists the four pages', () => {
    expect(SETTINGS_SECTION.path).toBe('/admin/settings');
    expect(SETTINGS_SECTION.icon).toBe('settings');
    expect(SETTINGS_SECTION.pages).toBe(SETTINGS_PAGES);
  });

  it('is opened by the permission of any one of its pages, each named once', () => {
    expect([...SETTINGS_SECTION.permissions].sort()).toEqual([Permission.ExamManage, Permission.OtpRead, Permission.WhatsAppTest].sort());
  });

  it('asks for nothing its pages do not ask for, so it cannot open for someone who can open none of them', () => {
    const pagePermissions = SETTINGS_PAGES.flatMap((page) => page.permissions);

    expect(SETTINGS_SECTION.permissions.every((permission) => pagePermissions.includes(permission))).toBe(true);
  });
});

describe('openSections', () => {
  const sections: AdminSection[] = [
    { label: 'admin.area.exams.name', description: 'admin.area.exams.description', path: '/a', permissions: ['x'], icon: 'exam' },
    { label: 'admin.area.books.name', description: 'admin.area.books.description', path: '/b', permissions: ['y', 'z'], icon: 'book' },
  ];

  it('keeps a section when the user holds any one of its permissions', () => {
    expect(openSections(sections, holding('z')).map((section) => section.path)).toEqual(['/b']);
    expect(openSections(sections, holding('y', 'z')).map((section) => section.path)).toEqual(['/b']);
  });

  it('keeps the order, and drops what the user may not open', () => {
    expect(openSections(sections, holding('z', 'x')).map((section) => section.path)).toEqual(['/a', '/b']);
    expect(openSections(sections, holding('other'))).toEqual([]);
  });
});

describe('openAdminGroups', () => {
  const headings = (permissions: string[]) => openAdminGroups(holding(...permissions)).map((group) => group.heading);
  const names = (permissions: string[]) =>
    openAdminGroups(holding(...permissions)).map((group) => group.sections.map(nameOf));

  it('offers nothing to a user with no admin permission', () => {
    expect(openAdminGroups(holding())).toEqual([]);
    expect(openAdminGroups(holding('admin.audit.read'))).toEqual([]);
  });

  it('offers a user with every permission the whole menu', () => {
    const everything = [...new Set(ADMIN_SECTION_GROUPS.flatMap((group) => group.sections.flatMap((section) => section.permissions)))];

    expect(openAdminGroups(holding(...everything))).toEqual(ADMIN_SECTION_GROUPS);
  });

  it('draws only the areas the permissions open, in menu order', () => {
    expect(names([Permission.StudentRead, Permission.QuestionRead, Permission.BatchManage])).toEqual([
      ['Questions'],
      ['Students', 'Batches'],
    ]);
  });

  it('draws no group, and so no heading, in which the user may open nothing', () => {
    expect(headings([Permission.QuestionRead])).toEqual(['admin.group.content']);
    expect(headings([Permission.ExamRead])).toEqual(['admin.group.exams']);
    expect(headings([Permission.GuardianLinkManage])).toEqual(['admin.group.people']);
  });

  it('gives the holder of exam.manage the reviews, and Settings, but not Content or People', () => {
    expect(names([Permission.ExamManage])).toEqual([['Attempt requests', 'Disputes', 'Reported issues'], ['Settings']]);
  });

  it('gives the holder of consent.manage the data requests, which the holder of exam.manage does not get, and nothing else', () => {
    expect(names([Permission.ConsentManage])).toEqual([['Data requests']]);
    expect(names([Permission.ExamManage]).flat()).not.toContain('Data requests');
  });

  it.each([
    { permission: Permission.ExamManage, opens: 'Branding and Instruction templates' },
    { permission: Permission.WhatsAppTest, opens: 'the WhatsApp test' },
    { permission: Permission.OtpRead, opens: 'Candidate codes' },
  ])('ends the menu with Settings, under no heading, for a user whose only permission is $permission (it opens $opens)', ({ permission }) => {
    const groups = openAdminGroups(holding(permission));
    const last = groups[groups.length - 1];

    expect(last.heading).toBeNull();
    expect(last.sections.map(nameOf)).toEqual(['Settings']);
  });

  it('keeps Settings away from a user who may open none of its pages, whatever else they hold', () => {
    const others = [Permission.QuestionManage, Permission.QuestionRead, Permission.ExamRead, Permission.InviteManage, Permission.BatchManage];

    expect(names(others).flat()).not.toContain('Settings');
    expect(headings(others)).not.toContain(null);
  });

  it('does not change the menu it reads from', () => {
    const before = JSON.stringify(ADMIN_SECTION_GROUPS);

    openAdminGroups(holding(Permission.QuestionRead));

    expect(JSON.stringify(ADMIN_SECTION_GROUPS)).toBe(before);
  });
});
