import { AdminIconName } from '../shared/admin-icon/admin-icon';
import { MessageKey } from '../i18n/messages.en';
import type { AuthSessionService } from './auth-session.service';

/** A permission code the API defines (see `RbacCatalog.PermissionCodes`); the UI mirrors only the ones it gates on. */
export const Permission = {
  QuestionManage: 'question.manage',
  QuestionRead: 'question.read',
  QuestionReview: 'question.review',
  ExamRead: 'exam.read',
  ExamManage: 'exam.manage',
  BatchManage: 'batch.manage',
  InviteManage: 'invite.manage',
  InviteDirectLink: 'invite.direct-link',
  StudentRead: 'student.read',
  StudentManage: 'student.manage',
  GuardianLinkManage: 'guardian.link.manage',
  OtpRead: 'identity.otp.read',
  WhatsAppTest: 'admin.whatsapp.test',
  ProctoringReview: 'proctoring.review',
} as const;

/** A count of things waiting for staff, shown as a tile on the admin home page and, for a queue, as a badge on its sidebar entry. */
export type AttentionKey = 'attemptRequests' | 'disputes' | 'issues' | 'riskFlags' | 'liveExams';

/**
 * One area of the admin section: where it lives and which permissions open it. The name and the description are message keys, so
 * the nav, the sidebar and the admin home page show them in the language the staff member chose.
 */
export interface AdminSection {
  label: MessageKey;
  description: MessageKey;
  path: string;
  /** The permissions that open the area; holding any one of them is enough. */
  permissions: readonly string[];
  /** Which icon the admin sidebar draws for this section; see {@link AdminIconName}. */
  icon: AdminIconName;
  /** For a queue of things waiting for staff: which count the sidebar shows as a badge on its entry. */
  attention?: AttentionKey;
  /**
   * For a section that is only a doorway to other pages: the pages it lists. Its menu entry reads as the current one on any of them, and
   * the permissions of its pages are the ones that open it, so a user who may open a single page still finds the doorway.
   */
  pages?: readonly AdminSection[];
}

/** A run of admin areas under one heading in the sidebar and on the admin home page; a null heading is a run that needs none. */
export interface AdminSectionGroup {
  heading: MessageKey | null;
  sections: readonly AdminSection[];
}

/** The permissions that open any of the sections, each named once. */
function permissionsOf(sections: readonly AdminSection[]): string[] {
  return [...new Set(sections.flatMap((section) => section.permissions))];
}

/**
 * The pages that are needed now and then rather than every day, kept together under one Settings entry so the menu stays short. Each
 * keeps its own address, so a bookmark or a link to it still works; Settings only lists the ones the signed-in user may open.
 */
export const SETTINGS_PAGES: readonly AdminSection[] = [
  { label: 'admin.area.branding.name', description: 'admin.area.branding.description', path: '/admin/branding', permissions: [Permission.ExamManage], icon: 'brand' },
  { label: 'admin.area.instructionTemplates.name', description: 'admin.area.instructionTemplates.description', path: '/admin/instruction-templates', permissions: [Permission.ExamManage], icon: 'template' },
  { label: 'admin.area.whatsappTest.name', description: 'admin.area.whatsappTest.description', path: '/admin/whatsapp', permissions: [Permission.WhatsAppTest], icon: 'whatsapp' },
  { label: 'admin.area.candidateCodes.name', description: 'admin.area.candidateCodes.description', path: '/admin/otp-codes', permissions: [Permission.OtpRead], icon: 'otp' },
];

/** The Settings entry: opens the page that lists {@link SETTINGS_PAGES}, and is open to whoever may open any one of them. */
export const SETTINGS_SECTION: AdminSection = {
  label: 'admin.area.settings.name',
  description: 'admin.area.settings.description',
  path: '/admin/settings',
  permissions: permissionsOf(SETTINGS_PAGES),
  icon: 'settings',
  pages: SETTINGS_PAGES,
};

/**
 * The admin areas as the menu offers them: grouped, in the order shown, with Settings last and under no heading. The sidebar, the admin
 * home page and the landing page all read this one list, and each route's guard names the same permissions, so they cannot drift apart.
 */
export const ADMIN_SECTION_GROUPS: readonly AdminSectionGroup[] = [
  {
    heading: 'admin.group.content',
    sections: [
      { label: 'admin.area.questions.name', description: 'admin.area.questions.description', path: '/admin/questions', permissions: [Permission.QuestionRead], icon: 'question' },
      { label: 'admin.area.books.name', description: 'admin.area.books.description', path: '/admin/books', permissions: [Permission.QuestionManage], icon: 'book' },
    ],
  },
  {
    heading: 'admin.group.exams',
    sections: [{ label: 'admin.area.exams.name', description: 'admin.area.exams.description', path: '/exams', permissions: [Permission.ExamRead], icon: 'exam' }],
  },
  {
    heading: 'admin.group.people',
    sections: [
      { label: 'admin.area.students.name', description: 'admin.area.students.description', path: '/students', permissions: [Permission.StudentRead], icon: 'student' },
      { label: 'admin.area.batches.name', description: 'admin.area.batches.description', path: '/batches', permissions: [Permission.BatchManage], icon: 'batch' },
      { label: 'admin.area.guardians.name', description: 'admin.area.guardians.description', path: '/guardian/link-candidate', permissions: [Permission.GuardianLinkManage], icon: 'guardian' },
    ],
  },
  {
    heading: 'admin.group.reviews',
    sections: [
      { label: 'admin.area.attemptRequests.name', description: 'admin.area.attemptRequests.description', path: '/admin/attempt-requests', permissions: [Permission.ExamManage], icon: 'attempt-request', attention: 'attemptRequests' },
      { label: 'admin.area.disputes.name', description: 'admin.area.disputes.description', path: '/admin/disputes', permissions: [Permission.ExamManage], icon: 'dispute', attention: 'disputes' },
      { label: 'admin.area.issueReports.name', description: 'admin.area.issueReports.description', path: '/admin/issue-reports', permissions: [Permission.ExamManage], icon: 'issue', attention: 'issues' },
    ],
  },
  { heading: null, sections: [SETTINGS_SECTION] },
];

/** What a permission check needs from the signed-in user; {@link AuthSessionService} answers it, and a test can answer it with a stub. */
type PermissionHolder = Pick<AuthSessionService, 'hasAnyPermission'>;

/** The sections, in their order, that the user's permissions open. */
export function openSections(sections: readonly AdminSection[], user: PermissionHolder): AdminSection[] {
  return sections.filter((section) => user.hasAnyPermission(section.permissions));
}

/** The menu as this user may use it: each group keeps the sections the user may open, and a group left with none is dropped, heading and all. */
export function openAdminGroups(user: PermissionHolder): AdminSectionGroup[] {
  return ADMIN_SECTION_GROUPS.map((group) => ({ ...group, sections: openSections(group.sections, user) })).filter(
    (group) => group.sections.length > 0,
  );
}
