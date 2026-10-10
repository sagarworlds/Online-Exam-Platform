import { AdminIconName } from '../shared/admin-icon/admin-icon';
import { MessageKey } from '../i18n/messages.en';

/** A permission code the API defines (see `RbacCatalog.PermissionCodes`); the UI mirrors only the ones it gates on. */
export const Permission = {
  QuestionManage: 'question.manage',
  QuestionRead: 'question.read',
  QuestionReview: 'question.review',
  ExamRead: 'exam.read',
  ExamManage: 'exam.manage',
  BatchManage: 'batch.manage',
  InviteManage: 'invite.manage',
  GuardianLinkManage: 'guardian.link.manage',
  OtpRead: 'identity.otp.read',
  WhatsAppTest: 'admin.whatsapp.test',
  ProctoringReview: 'proctoring.review',
} as const;

/**
 * One area of the admin section: where it lives and which permission opens it. The name and the description are message keys, so
 * the nav, the sidebar and the admin home page show them in the language the staff member chose.
 */
export interface AdminSection {
  label: MessageKey;
  description: MessageKey;
  path: string;
  permission: string;
  /** Which icon the admin sidebar draws for this section; see {@link AdminIconName}. */
  icon: AdminIconName;
}

/**
 * The admin areas, in the order they are offered. The nav bar and the admin home page both read this one
 * list, and each route's guard names the same permission, so the three cannot drift apart.
 */
export const ADMIN_SECTIONS: readonly AdminSection[] = [
  { label: 'admin.area.questions.name', description: 'admin.area.questions.description', path: '/admin/questions', permission: Permission.QuestionRead, icon: 'question' },
  { label: 'admin.area.books.name', description: 'admin.area.books.description', path: '/admin/books', permission: Permission.QuestionManage, icon: 'book' },
  { label: 'admin.area.exams.name', description: 'admin.area.exams.description', path: '/exams', permission: Permission.ExamRead, icon: 'exam' },
  { label: 'admin.area.attemptRequests.name', description: 'admin.area.attemptRequests.description', path: '/admin/attempt-requests', permission: Permission.ExamManage, icon: 'attempt-request' },
  { label: 'admin.area.disputes.name', description: 'admin.area.disputes.description', path: '/admin/disputes', permission: Permission.ExamManage, icon: 'dispute' },
  { label: 'admin.area.issueReports.name', description: 'admin.area.issueReports.description', path: '/admin/issue-reports', permission: Permission.ExamManage, icon: 'issue' },
  { label: 'admin.area.invites.name', description: 'admin.area.invites.description', path: '/invites', permission: Permission.InviteManage, icon: 'invite' },
  { label: 'admin.area.batches.name', description: 'admin.area.batches.description', path: '/batches', permission: Permission.BatchManage, icon: 'batch' },
  { label: 'admin.area.guardians.name', description: 'admin.area.guardians.description', path: '/guardian/link-candidate', permission: Permission.GuardianLinkManage, icon: 'guardian' },
  { label: 'admin.area.candidateCodes.name', description: 'admin.area.candidateCodes.description', path: '/admin/otp-codes', permission: Permission.OtpRead, icon: 'otp' },
  { label: 'admin.area.whatsappTest.name', description: 'admin.area.whatsappTest.description', path: '/admin/whatsapp', permission: Permission.WhatsAppTest, icon: 'whatsapp' },
];
