# ADR 0002: Migrations and Reference-Data Seeding

- Status: Accepted
- Date: 2026-10-01

## Context

The platform cannot work on an empty database. Registration looks up the `Candidate` role and fails with `RoleNotFoundError` if it does not exist; every permission check compares a token's `perm` claims with codes that only exist once the RBAC reference data has been written; recording consent needs a notice version to point at. That reference data (roles, permissions and their grants, consent notice versions) is written by idempotent seeders owned by each module, next to the schema that holds it.

Until now the Host ran every module's `IModuleInstaller.MigrateAndSeedAsync` only when `ASPNETCORE_ENVIRONMENT=Development`. Any other environment had no way to get a schema or reference data at all, although the `IModuleInstaller` documentation described a production path that did not exist. The Identity seeder also returned as soon as any role existed, so a database seeded by an earlier release could never receive a permission added later (FR-2: exam, batch, invite and guardian permissions arrive after the first three).

Constraints: modules must not be reached into by the Host (ADR 0001), so the Host cannot resolve a `DbContext` and migrate it itself; the platform is deployed as several identical web replicas that start together; and a database the platform already runs on must keep working.

## Decision

1. **Schema and reference data are applied by the same call.** Each module's `IModuleInstaller.MigrateAndSeedAsync` runs `Database.MigrateAsync()` and then the module's seeders. Reference data is never written by an EF migration and never by a migration bundle: a bundle applies the schema only and would skip the seeders, leaving exactly the database in which registration fails.
2. **Seeders are idempotent upserts and additive only.** A seeder adds whatever reference data is missing and leaves the rest alone. It never revokes a grant and never changes a flag such as `RequiresTwoFactor` on a row that exists, because taking a capability away should be a deliberate, audited admin action and not a side effect of a deployment. Permissions are copied into the access token when it is issued, so a user picks up a new grant the next time they sign in.
3. **The Host has two ways to run the installers, and no other.**
   - `dotnet ExamPlatform.Api.dll --migrate-and-seed` runs every module's `MigrateAndSeedAsync` once, logs `Migrated and seeded {ModuleName}` for each, and returns before `app.Run()`. The web host is never started, so the job needs only the connection string, and the startup validation of a serving host (for example the OTP delivery options' `ValidateOnStart`) does not apply to it. A failure is an unhandled exception and a non-zero exit code, which fails the deployment step.
   - `Database:MigrateAndSeedOnStartup` does the same at the start of a normal run. It is `false` in `appsettings.json` and `true` in `appsettings.Development.json`, so `dotnet run` still gives a ready-to-use database. It replaces the earlier hard-coded `IsDevelopment()` check; when the key is absent the Development default still applies.
4. **One runner.** A deployment runs `--migrate-and-seed` once, as a pre-deploy job or an init container, and starts the web replicas with the setting `false`. Two processes seeding together can both try to insert the same role or permission, and one of them then fails on the unique index of `Roles.Name` or `Permissions.Code`. The seeders do not try to hide that race; they rely on there being a single runner.
5. **Development bootstrap administrator.** Staff sign in with a password and a second factor (FR-3), and nothing provisions a staff account, so a fresh development database has no way into the admin features. `Identity:Bootstrap:AdminEmail` and `AdminPassword` (the password from user-secrets) create one active `SuperAdmin`, hashed through `IPasswordHasher` and checked against the password policy. It is idempotent, never modifies an existing account, and is honoured in the Development environment only: in any other environment the settings are ignored with a warning, so a production database can never receive an administrator from configuration.
6. **Audit entries are written after the change commits.** `AssignRoleHandler` (like `ConsentService`) saves its change and then records the audit entry through `IAuditLogger`, in a separate `AdminDbContext` transaction. The two are not atomic: if the audit write fails the request returns a 500 for a change that did persist. This is an accepted trade-off, not an oversight. Making them atomic needs a transactional outbox, and ADR 0001 defers durable messaging until a module needs asynchronous work.

## Alternatives considered

- **EF migration bundles (`dotnet ef migrations bundle`) as the deployment path.** The standard recommendation, but a bundle only applies migrations. Reference data would have to be inserted by the migrations themselves, which couples schema history to data that changes with every release (new permissions would need a new migration each time, with `HasData` rewritten in every module), or a second mechanism would be needed for the seeders. Running the installers keeps one mechanism for both.
- **Migrate and seed on every startup in every environment.** Simplest, but with several replicas starting together it makes the unique-index race of decision 4 routine, and it lets a web process with a broken database role change the schema. Rejected in favour of an explicit job; the setting remains for single-instance environments that want it.
- **Seed with `HasData` in `OnModelCreating`.** Ties the data to model snapshots and needs fixed ids for every row, and the upsert behaviour of decision 2 (add what is missing, never overwrite) is not what `HasData` does.
- **Create the bootstrap administrator in every environment.** Rejected: an account provisioned from configuration is a convenience for an empty development database, and in production it would be a standing credential in a deployment file.

## Consequences

**Positive:** a deployed environment has a documented, testable way to reach a working database (`NonDevelopmentStartupTests` runs the real binary with the flag against an empty database and checks the roles, permissions and notice versions it leaves); seeders can be run on every deploy, and an environment seeded by an earlier release picks up new permissions without a migration; the deployment job needs no signing key or OTP provider.

**Negative:** a deployment now has an extra step that must run before the replicas start, and forgetting it leaves an empty database (registration then fails with `RoleNotFoundError`); the single-runner rule is a convention of the deployment, not something the code enforces; existing sessions keep the permission set they were issued with until the user signs in again.

## Follow-ups

- **Per-module migration history.** The Identity, Consent and Admin contexts set a default schema, so their `__EFMigrationsHistory` tables live in their own schemas. The ExamAuthoring, Batch, Invite and Guardian contexts do not call `HasDefaultSchema` (only each table is placed in a schema), so they share `public.__EFMigrationsHistory`. This works because their migration ids are distinct and each context only considers its own, but it is untidy and would collide if two contexts ever generated the same migration id. It is left alone on purpose: moving the history table would make EF re-run `InitialCreate` against databases that already have the tables. Revisit when those modules next get a breaking schema change, with a one-off script that moves the history rows.
- **Atomic audit.** Revisit decision 6 when an outbox exists (ADR 0001 follow-up on asynchronous work).
- **Revoking a grant.** There is deliberately no automated way to remove a permission from a role. If one is needed, it should be an admin action that is itself audited, not a seeder option.
