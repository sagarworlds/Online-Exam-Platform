namespace ExamPlatform.Modules.Identity.Domain.Rbac;

/// <summary>A permission the platform defines: the code endpoints check and what it allows.</summary>
/// <param name="Code">The stable, dotted code carried in a token's <c>perm</c> claims (e.g. <c>batch.manage</c>).</param>
/// <param name="Description">Human-readable description shown in admin tooling.</param>
public sealed record PermissionDefinition(string Code, string Description);

/// <summary>A role the platform defines, with the permissions it holds out of the box.</summary>
/// <param name="Name">The role's unique name.</param>
/// <param name="RequiresTwoFactor">Whether signing in as this role must complete a second OTP factor (FR-3).</param>
/// <param name="PermissionCodes">The codes of the permissions the role is granted, each one defined in <see cref="RbacCatalog.Permissions"/>.</param>
public sealed record RoleDefinition(string Name, bool RequiresTwoFactor, IReadOnlyList<string> PermissionCodes);

/// <summary>
/// The platform's role-based access control reference data (FR-2): every permission, every role,
/// and which permissions each role holds. Pure data with no dependencies, so it can be read by the
/// seeder that writes it to the database and by tests that pin the access matrix, without either
/// needing a database. Endpoints in other modules guard their routes with the permission codes
/// below (as <c>permission:{code}</c> policies) and must not be changed without updating the matrix.
/// </summary>
/// <remarks>
/// Roles are named after exam-platform-requirements.md section 3. A role with no permissions
/// (Candidate, Guardian, Reviewer, Proctor today) is still reference data: users
/// hold it, and later milestones grant it capabilities.
/// </remarks>
public static class RbacCatalog
{
    /// <summary>The codes of every permission the platform defines.</summary>
    public static class PermissionCodes
    {
        /// <summary>Read the admin audit log (FR-40).</summary>
        public const string AuditRead = "admin.audit.read";

        /// <summary>Record and withdraw consent on behalf of a candidate.</summary>
        public const string ConsentManage = "consent.manage";

        /// <summary>Assign roles to users and list the roles that can be assigned (FR-2).</summary>
        public const string RoleAssign = "identity.role.assign";

        /// <summary>Create and read questions in the question bank, answer key included (FR-5).</summary>
        public const string QuestionManage = "question.manage";

        /// <summary>Read questions, their history and review thread, and comment on them, without being able to change them (FR-8).</summary>
        public const string QuestionRead = "question.read";

        /// <summary>Approve a question in review or send it back to its author (FR-8).</summary>
        public const string QuestionReview = "question.review";

        /// <summary>View exams and their questions and schedules, without being able to change them.</summary>
        public const string ExamRead = "exam.read";

        /// <summary>Create and edit exams.</summary>
        public const string ExamManage = "exam.manage";

        /// <summary>Publish an exam so that candidates can be assigned to it.</summary>
        public const string ExamPublish = "exam.publish";

        /// <summary>Create batches, manage their rosters and open or close them (FR-50).</summary>
        public const string BatchManage = "batch.manage";

        /// <summary>View batches and their rosters.</summary>
        public const string BatchRead = "batch.read";

        /// <summary>Create invites, generate and revoke their codes (FR-50a).</summary>
        public const string InviteManage = "invite.manage";

        /// <summary>Read the unspent sign-in and registration codes of candidates, to help one who never received theirs.</summary>
        public const string OtpRead = "identity.otp.read";

        /// <summary>Score exams for risk, read the review queue of flagged attempts and decide on each flag (FR-27). Never changes a candidate's attempt.</summary>
        public const string ProctoringReview = "proctoring.review";

        /// <summary>Link guardians to candidates and revoke those links (FR-45).</summary>
        public const string GuardianLinkManage = "guardian.link.manage";

        /// <summary>Check the WhatsApp connection and send a test message through it: a real message to a real number.</summary>
        public const string WhatsAppTest = "admin.whatsapp.test";

        /// <summary>
        /// Log incidents and breaches, list the open ones with their escalation flags, and record their status changes (FR-52).
        /// Not <c>consent.manage</c>: that code is about recording a candidate's consent, which an incident log does not do.
        /// </summary>
        public const string IncidentManage = "consent.incident.manage";
    }

    /// <summary>The names of the roles the platform defines.</summary>
    public static class RoleNames
    {
        /// <summary>Holds every permission.</summary>
        public const string SuperAdmin = "SuperAdmin";

        /// <summary>Runs exams, batches, invites and guardian links.</summary>
        public const string ExamAdmin = "ExamAdmin";

        /// <summary>Authors question content.</summary>
        public const string ContentAuthor = "ContentAuthor";

        /// <summary>Reviews question content.</summary>
        public const string Reviewer = "Reviewer";

        /// <summary>Proctors a live exam.</summary>
        public const string Proctor = "Proctor";

        /// <summary>Manages the batches and invites of an institute.</summary>
        public const string InstituteTeacher = "InstituteTeacher";

        /// <summary>A self-registered exam taker; the role every registration assigns.</summary>
        public const string Candidate = "Candidate";

        /// <summary>A parent or guardian of a minor candidate.</summary>
        public const string Guardian = "Guardian";
    }

    /// <summary>Every permission the platform defines. Codes are unique.</summary>
    public static IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(PermissionCodes.AuditRead, "View the admin audit log"),
        new(PermissionCodes.ConsentManage, "Record and withdraw consent on behalf of a candidate"),
        new(PermissionCodes.RoleAssign, "Assign roles to users"),
        new(PermissionCodes.QuestionManage, "Create and read questions, answer key included"),
        new(PermissionCodes.QuestionRead, "Read questions and their review thread, and comment on them"),
        new(PermissionCodes.QuestionReview, "Approve questions in review or send them back"),
        new(PermissionCodes.ExamRead, "View exams, their questions and schedules"),
        new(PermissionCodes.ExamManage, "Create and edit exams"),
        new(PermissionCodes.ExamPublish, "Publish exams"),
        new(PermissionCodes.BatchManage, "Create batches and manage their rosters"),
        new(PermissionCodes.BatchRead, "View batches and their rosters"),
        new(PermissionCodes.InviteManage, "Create invites and manage their codes"),
        new(PermissionCodes.GuardianLinkManage, "Link guardians to candidates and revoke those links"),
        new(PermissionCodes.OtpRead, "Read candidates' unspent sign-in and registration codes"),
        new(PermissionCodes.WhatsAppTest, "Check the WhatsApp connection and send test messages through it"),
        new(PermissionCodes.ProctoringReview, "Score exams for risk and review the flags raised on attempts"),
        new(PermissionCodes.IncidentManage, "Log incidents and breaches, see the open ones, and record their status changes"),
    ];

    /// <summary>
    /// Every role the platform defines and the permissions it is granted. Names are unique, and
    /// every code in a grant is defined in <see cref="Permissions"/>. <c>RequiresTwoFactor</c>
    /// follows FR-3: mandatory for the admin-facing roles, not for candidates, guardians or teachers.
    /// </summary>
    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        // The administrator holds everything, including permissions defined later, so the
        // grant is derived from Permissions instead of being a second list to keep in step.
        new(RoleNames.SuperAdmin, RequiresTwoFactor: true, Permissions.Select(p => p.Code).ToList()),

        new(RoleNames.ExamAdmin, RequiresTwoFactor: true,
        [
            PermissionCodes.QuestionManage,
            PermissionCodes.QuestionRead,
            PermissionCodes.QuestionReview,
            PermissionCodes.ExamRead,
            PermissionCodes.ExamManage,
            PermissionCodes.ExamPublish,
            PermissionCodes.BatchManage,
            PermissionCodes.BatchRead,
            PermissionCodes.InviteManage,
            PermissionCodes.GuardianLinkManage,
        ]),

        new(RoleNames.ContentAuthor, RequiresTwoFactor: true, [PermissionCodes.QuestionManage, PermissionCodes.QuestionRead]),
        new(RoleNames.Reviewer, RequiresTwoFactor: true, [PermissionCodes.QuestionRead, PermissionCodes.QuestionReview]),
        new(RoleNames.Proctor, RequiresTwoFactor: true, [PermissionCodes.ProctoringReview]),

        new(RoleNames.InstituteTeacher, RequiresTwoFactor: false,
        [
            PermissionCodes.ExamRead,
            PermissionCodes.BatchManage,
            PermissionCodes.BatchRead,
            PermissionCodes.InviteManage,
        ]),

        new(RoleNames.Candidate, RequiresTwoFactor: false, []),
        new(RoleNames.Guardian, RequiresTwoFactor: false, []),
    ];
}
