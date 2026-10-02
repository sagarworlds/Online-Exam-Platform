/** A permission code the API defines (see `RbacCatalog.PermissionCodes`); the UI mirrors only the ones it gates on. */
export const Permission = {
  QuestionManage: 'question.manage',
  ExamRead: 'exam.read',
  ExamManage: 'exam.manage',
  BatchManage: 'batch.manage',
  InviteManage: 'invite.manage',
  GuardianLinkManage: 'guardian.link.manage',
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
  { label: 'Exams', description: 'Build an exam from questions, schedule it and publish it.', path: '/exams', permission: Permission.ExamRead },
  { label: 'Invites', description: 'Invite candidates to an exam by email.', path: '/invites', permission: Permission.InviteManage },
  { label: 'Batches', description: 'Group candidates into batches.', path: '/batches', permission: Permission.BatchManage },
  { label: 'Guardians', description: 'Link a guardian to a candidate.', path: '/guardian/link-candidate', permission: Permission.GuardianLinkManage },
];
