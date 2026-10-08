import { AdminIconName } from '../shared/admin-icon/admin-icon';

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
} as const;

/** One area of the admin section: where it lives and which permission opens it. */
export interface AdminSection {
  label: string;
  description: string;
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
  { label: 'Questions', description: 'Write questions, mark the correct answer and review them.', path: '/admin/questions', permission: Permission.QuestionRead, icon: 'question' },
  { label: 'Books', description: 'Organise questions into books and chapters.', path: '/admin/books', permission: Permission.QuestionManage, icon: 'book' },
  { label: 'Exams', description: 'Build an exam from questions, schedule it and publish it.', path: '/exams', permission: Permission.ExamRead, icon: 'exam' },
  { label: 'Attempt requests', description: 'Answer candidates who ask for another attempt.', path: '/admin/attempt-requests', permission: Permission.ExamManage, icon: 'attempt-request' },
  { label: 'Disputes', description: 'Answer candidates who dispute an answer key: correct it or explain why it stands.', path: '/admin/disputes', permission: Permission.ExamManage, icon: 'dispute' },
  { label: 'Invites', description: 'Invite candidates to an exam by email.', path: '/invites', permission: Permission.InviteManage, icon: 'invite' },
  { label: 'Batches', description: 'Group candidates into batches.', path: '/batches', permission: Permission.BatchManage, icon: 'batch' },
  { label: 'Guardians', description: 'Link a guardian to a candidate.', path: '/guardian/link-candidate', permission: Permission.GuardianLinkManage, icon: 'guardian' },
  { label: 'Candidate codes', description: 'Read the sign-in code a candidate is waiting for.', path: '/admin/otp-codes', permission: Permission.OtpRead, icon: 'otp' },
  { label: 'WhatsApp test', description: 'Check that WhatsApp can send, and see exactly why not if it cannot.', path: '/admin/whatsapp', permission: Permission.WhatsAppTest, icon: 'whatsapp' },
];
