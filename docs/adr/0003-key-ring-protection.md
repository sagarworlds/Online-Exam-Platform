# ADR 0003: Protect the Data Protection key ring with a key held outside the database

- Status: Accepted
- Date: 2026-10-15

## Context

NFR-5 requires encryption at rest. Question content is encrypted with ASP.NET Core Data Protection (#57, `QuestionContentCipher`). The key ring, the keys that decrypt that content, is stored in the same Postgres database (`QuestionBankDbContext`, table `questionBank.DataProtectionKeys`), so that every replica decrypts with the same keys.

The ciphertext and the keys that open it therefore sit side by side. A copy of the database (a backup, the dump taken to move to Neon, a leaked connection string) holds both and opens every question. The encryption protects against reading a column, but not against a copy of the database. The cipher's own remarks said the keys "should be wrapped by a key outside the database before launch", and the M8 plan (#86) found no `ProtectKeysWith*` call on `main`.

Constraints:

- The hosting is Render with a Neon database. Secrets are environment variables on Render. There is no key management service, and adding one is a hosting and data-location decision (NFR-12, counsel in #11) that has not been taken.
- Every deployment already has keys in the clear, and content encrypted under them. Those keys must keep working. Re-encrypting every question is not an acceptable price for this change.
- ADR 0002: the migrate-and-seed job needs only a connection string. It must not demand a secret it does not use.
- A wrong or lost key must stop the host. It must never make Data Protection create a new key, because every value protected so far would then be unreadable and nothing would say why.

## Decision

Each key ring entry is encrypted with AES-256-GCM under a 32-byte key-encryption key (KEK) that comes from configuration, `DataProtection:KeyEncryptionKey`. On Render it is an environment variable that Render generates once, as it does for `Jwt__SigningKey`.

- **Where it is applied.** The key ring's repository is wrapped (`ProtectedXmlRepository`, registered by `ProtectKeyRing()` in the QuestionBank installer). Data Protection and `QuestionContentCipher` are unchanged and see the same `key` and `revocation` entries as before. The whole entry is encrypted, so its dates and algorithms are hidden as well as its key material. The element is `<protectedKey version="1" keyId="…">`, and a short hash of the KEK names which key protected it.
- **Required outside Development.** A startup check (`KeyRingProtectionOptionsValidator`, with `ValidateOnStart`) refuses a host that has no valid key. A failed deploy leaves the previous version serving until the setting is added. A Development host may run without a key and stores the ring in the clear, with a warning, so a developer's database needs no secret.
- **Existing entries keep working.** An entry stored in the clear is read as it is. The migrate-and-seed step (`KeyRingReprotection`, before the content backfill) rewrites each entry in place under the KEK. Only how an entry is stored changes, never the key inside it, so everything already encrypted stays readable. A run on a protected ring changes nothing.
- **The key is asked for only when the ring is used.** Building Data Protection's services reads no keys. The migrate-and-seed job on an empty database never touches the ring, so it still needs only a connection string. A database that holds key ring entries needs the KEK in the job, and the job fails naming the setting when it is missing.
- **Failures stop the host.** An entry that cannot be opened (a wrong key, a lost key, a damaged entry) raises `KeyRingProtectionException` and is never skipped, so the ring is never read as smaller than it is.
- **Rotation.** Put the new key in `DataProtection:KeyEncryptionKey` and the old one in `DataProtection:PreviousKeyEncryptionKeys`. On start the ring is rewritten under the new key, after which the old one can be removed.

The seam is `IKeyRingProtector`. A protector backed by a key management service could replace `AesGcmKeyRingProtector` without touching the rest.

## Alternatives considered

- **`ProtectKeysWithCertificate`, the framework's own option.** It needs a certificate (a PFX and its password, two secrets) and certificate rotation. The deciding problem is that it protects only keys created after it is switched on. The keys already stored stay in the clear, and Data Protection has no supported way to rewrite them. Getting every key protected would mean creating a new key and re-encrypting every question and answer key across all content columns, a much larger and riskier change.
- **A custom `IXmlEncryptor`.** The same limit on existing keys, and custom cryptography around part of an entry.
- **A cloud key management service (for example Azure Key Vault).** It keeps the key outside the application's own environment, which is better. It needs a cloud account that the hosting and data-location decision has not approved. The `IKeyRingProtector` seam leaves the door open.
- **Encrypting the database or its disk.** The hosting provider's choice, and it does not protect a logical dump.
- **Recording an exception and leaving the ring as it is.** A copy of the database would open all question content, which defeats the point of #57.

## Consequences

- A copy of the database taken after this change (a dump, a backup, a leaked connection string) opens no question content without the KEK.
- Content encrypted before this change stays readable. Nothing is re-encrypted.
- **A lost KEK loses the question content.** The key must be copied to a password manager and kept apart from the database backups. This is a new duty for the owner, and the disaster recovery runbook (#85) has to restore the key separately from the database.
- The KEK sits in Render's environment, so anyone with dashboard access can read it. The trust boundary moves from "access to the database" to "access to the Render dashboard". It does not move to a key management service.
- **Copies taken before this change still hold the keys in the clear.** Protecting the ring cannot undo that. Rotating the Data Protection keys (a new key, with all content re-encrypted under it) and deleting the older copies would. That is not done here.
- **A rollback to a build from before this change cannot read the protected ring.** Data Protection skips the entries it does not recognise, so the older build sees no keys and makes a new one, and the content written earlier fails to open until a newer build runs again. Nothing is lost: the protected entries stay in the table, and the newer build reads them as well as the new key. Do not roll back past this change without being ready to deploy it again.
- ASP.NET Core still logs "No XML encryptor configured. Key … may be persisted to storage in unencrypted form" when it creates a key, because the protection is applied at the repository, which it cannot see. That line is expected and does not describe what is stored.
- The first deploy after this change needs the variable. `render.yaml` declares it with `generateValue: true`. If it is missing the API refuses to start and the log names the setting.

## Follow-ups

- Rotating the Data Protection keys and re-encrypting the content, if an earlier dump or backup is a concern. It needs its own plan, because every content column is rewritten.
- A key management service, if the hosting decision (NFR-12, #11) moves to a provider that has one.
- The restore runbook (#85) must say where the KEK is kept and how it is restored apart from the database.
