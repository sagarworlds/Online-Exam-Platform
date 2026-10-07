namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// The grants of <c>question.read</c> and <c>question.review</c> (FR-8) as SQL, so a database that already has roles gets them from its
/// migration rather than only from the seeder. The statements are idempotent, and they only add: a role that already has a grant, or a
/// database with no roles yet (a fresh one, which the seeder fills in), is left as it is.
/// </summary>
public static class QuestionReviewPermissions
{
    /// <summary>Adds the two permissions, then grants <c>question.read</c> to every role that can manage questions and to the Reviewer, and <c>question.review</c> to SuperAdmin, ExamAdmin and the Reviewer.</summary>
    public const string GrantSql =
        """
        INSERT INTO identity."Permissions" ("Id", "Code", "Description")
        SELECT gen_random_uuid(), v.code, v.description
        FROM (VALUES
            ('question.read', 'Read questions and their review thread, and comment on them'),
            ('question.review', 'Approve questions in review or send them back')
        ) AS v(code, description)
        WHERE NOT EXISTS (SELECT 1 FROM identity."Permissions" p WHERE p."Code" = v.code);

        INSERT INTO identity."RolePermissions" ("PermissionsId", "RoleId")
        SELECT read_permission."Id", managers."RoleId"
        FROM identity."RolePermissions" managers
        JOIN identity."Permissions" manage_permission ON manage_permission."Id" = managers."PermissionsId" AND manage_permission."Code" = 'question.manage'
        CROSS JOIN identity."Permissions" read_permission
        WHERE read_permission."Code" = 'question.read'
        ON CONFLICT DO NOTHING;

        INSERT INTO identity."RolePermissions" ("PermissionsId", "RoleId")
        SELECT p."Id", r."Id"
        FROM identity."Roles" r
        JOIN identity."Permissions" p ON (p."Code" = 'question.review' AND r."Name" IN ('SuperAdmin', 'ExamAdmin', 'Reviewer'))
                                      OR (p."Code" = 'question.read' AND r."Name" = 'Reviewer')
        ON CONFLICT DO NOTHING;
        """;
}
