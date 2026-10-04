/** A permission code the API defines (see `RbacCatalog.PermissionCodes`); the UI mirrors only the ones it gates on. */
export const Permission = {
  QuestionManage: 'question.manage',
  ExamRead: 'exam.read',
  ExamManage: 'exam.manage',
  BatchManage: 'batch.manage',
  InviteManage: 'invite.manage',
  GuardianLinkManage: 'guardian.link.manage',
  OtpRead: 'identity.otp.read',
} as const;

/** One area of the admin section: where it lives and which permission opens it. */
export interface AdminSection {
  label: string;
  description: string;
  path: string;
  permission: string;
}

/**
 * The admin areas, in the order they are offered. The nav bar and the admin home page both read this one
 * list, and each route's guard names the same permission, so the three cannot drift apart.
 */
export const ADMIN_SECTIONS: readonly AdminSection[] = [
  { label: 'Questions', description: 'Write questions and mark the correct answer.', path: '/admin/questions', permission: Permission.QuestionManage },
  { label: 'Books', description: 'Organise questions into books and chapters.', path: '/admin/books', permission: Permission.QuestionManage },
  { label: 'Exams', description: 'Build an exam from questions, schedule it and publish it.', path: '/exams', permission: Permission.ExamRead },
  { label: 'Attempt requests', description: 'Answer candidates who ask for another attempt.', path: '/admin/attempt-requests', permission: Permission.ExamManage },
  { label: 'Invites', description: 'Invite candidates to an exam by email.', path: '/invites', permission: Permission.InviteManage },
  { label: 'Batches', description: 'Group candidates into batches.', path: '/batches', permission: Permission.BatchManage },
  { label: 'Guardians', description: 'Link a guardian to a candidate.', path: '/guardian/link-candidate', permission: Permission.GuardianLinkManage },
  { label: 'Candidate codes', description: 'Read the sign-in code a candidate is waiting for.', path: '/admin/otp-codes', permission: Permission.OtpRead },
];
